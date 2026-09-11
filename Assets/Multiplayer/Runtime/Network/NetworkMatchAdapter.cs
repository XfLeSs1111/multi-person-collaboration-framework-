using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Framework roster entry: which member holds which seat. Every game needs to tell
    /// "who is playing and as what" from "who is only watching", and the wording of
    /// that lives in the game (DescribeSeat), not here.
    /// </summary>
    [Serializable]
    public struct MatchSeatInfo
    {
        public string stableId;
        public string displayName;
        /// <summary>Board/duel seat; -1 marks a watcher.</summary>
        public int seat;
    }

    /// <summary>
    /// Framework half of a networked match: turn timer, resignation, end-state
    /// replication and the server hooks SocketRoomManager drives. Everything the room
    /// lifecycle needs (disconnect / timeout / resign / rematch) is implemented here
    /// exactly once, so a new game only derives, implements the rules hooks and adds
    /// its own view.
    /// </summary>
    public abstract class NetworkMatchAdapter : NetworkBehaviour
    {
        // 绝对截止时刻只在服务端判定用：客户端进程的 NetworkTime 基准不同，拿它做减算必然偏。
        private double turnDeadline;
        // 同步给客户端的“剩余秒数 + 每次重新计时的代号”；客户端凭代号重锚点后本地插值，读秒才顺滑。
        [SyncVar] private float turnSecondsRemaining;
        [SyncVar] private byte turnArmStamp;
        [SyncVar] private MatchEndReason endReason;
        [SyncVar] private MatchForfeitCause forfeitCause;
        [SyncVar] private string endDetail;

        /// <summary>Who sits where; drives the 对战/观战 labels in every game's UI.</summary>
        public readonly SyncList<MatchSeatInfo> seats = new SyncList<MatchSeatInfo>();

        // ---- replicated match state -------------------------------------------------------

        public abstract Guid MatchId { get; }
        public abstract MatchPhase Phase { get; }

        /// <summary>Seat allowed to act right now; -1 when nobody can act.</summary>
        public abstract int CurrentSeat { get; }

        /// <summary>Game progress counter for the status line (moves, rounds…).</summary>
        public virtual int Progress => 0;

        /// <summary>How many duel seats this game has; extra room members watch instead.</summary>
        public virtual int MaxSeats => 2;

        /// <summary>Seat held by a display name; -1 when that member only watches.</summary>
        public int SeatOf(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return -1;
            foreach (var entry in seats)
                if (entry.displayName == displayName) return entry.seat;
            return -1;
        }

        /// <summary>Number of members currently holding a duel seat.</summary>
        public int SeatedCount
        {
            get
            {
                var count = 0;
                foreach (var entry in seats)
                    if (entry.seat >= 0) count++;
                return count;
            }
        }

        /// <summary>True when this member is only watching the running match.</summary>
        public bool IsWatcher(string displayName) => SeatOf(displayName) < 0;

        public MatchEndReason EndReason => endReason;
        public MatchForfeitCause ForfeitCause => forfeitCause;

        /// <summary>Game-side label for the ending ("五连"/"和棋"); may be empty.</summary>
        public string EndDetail => endDetail ?? string.Empty;

        /// <summary>绝对截止时刻（服务端时钟）；仅服务端判定用，客户端请读 TurnSecondsLeft。</summary>
        public double TurnDeadline => turnDeadline;

        // 客户端插值锚点：最近一次「重新计时」的代号、当时的剩余秒数、收到它的本地时刻。
        private byte lastSeenTurnArmStamp;
        private float lastSeenTurnSeconds;
        private double lastSeenTurnAt;

        /// <summary>Seconds left for the current turn (0 when disabled or not running).</summary>
        public double TurnSecondsLeft
        {
            get
            {
                if (turnArmStamp != lastSeenTurnArmStamp)
                {
                    lastSeenTurnArmStamp = turnArmStamp;
                    lastSeenTurnSeconds = turnSecondsRemaining;
                    lastSeenTurnAt = NetworkTime.localTime;
                }

                if (Phase != MatchPhase.Active || lastSeenTurnSeconds <= 0f) return 0d;
                var left = lastSeenTurnSeconds - (NetworkTime.localTime - lastSeenTurnAt);
                return left > 0d ? left : 0d;
            }
        }

        // ---- labels the shared match HUD uses; games own the wording ----------------------

        /// <summary>Short label for a seat, e.g. "黑棋 X" / "Player 1".</summary>
        public abstract string DescribeSeat(int seat);

        /// <summary>Short label for the finished match, e.g. "黑棋 X 胜" / "和棋".</summary>
        public abstract string DescribeResult();

        /// <summary>True when this player holds the seat that may act right now.</summary>
        public bool IsLocalTurn(NetworkPlayer local) =>
            local != null && local.SeatIndex >= 0 && local.SeatIndex == CurrentSeat;

        // ---- server hooks the room manager drives -----------------------------------------
        // NOTE: Mirror's Weaver rejects [Server] on abstract members — the attributes live
        // on the overrides in the concrete adapter (they are not inherited).

        public abstract void ServerInitialize(Guid roomId, RoomTemplate template);

        public abstract bool ServerAddPlayer(string stableId, string displayName, int seatIndex);

        public abstract bool ServerAddSpectator(string stableId, string displayName);

        public abstract bool ServerDropParticipant(string stableId);

        public abstract bool ServerSetParticipantConnected(string stableId, bool connected);

        public abstract bool ServerStartMatch();

        public abstract void ServerResetMatch();

        public abstract bool ServerForfeit(string stableId, MatchForfeitCause cause);

        public abstract string ServerFindStableIdBySeat(int seat);

        /// <summary>
        /// Snapshot of this match for the record book. The framework owns storage and
        /// transport; the game owns the labels and the opaque replay payload.
        /// </summary>
        public abstract MatchRecordInfo ServerCreateRecord(string roomName);

        /// <summary>Seat summary for a record, e.g. "A（黑棋 X） vs B（白棋 O）".</summary>
        protected string DescribeSeats()
        {
            var text = string.Empty;
            foreach (var entry in seats)
            {
                if (entry.seat < 0) continue;
                if (text.Length > 0) text += " vs ";
                text += $"{entry.displayName}（{DescribeSeat(entry.seat)}）";
            }
            return text.Length == 0 ? "对局已结束" : text;
        }

        /// <summary>
        /// A member left the room (disconnect or quit). An active match forfeits so the
        /// survivor is not deadlocked on a turn that will never come; a waiting match
        /// simply drops the participant. Shared by every game, hence framework-level.
        /// </summary>
        [Server]
        public virtual void ServerHandleParticipantLeft(string stableId, MatchForfeitCause cause)
        {
            if (string.IsNullOrEmpty(stableId)) return;
            if (Phase == MatchPhase.Active)
            {
                ServerForfeit(stableId, cause);
                ServerSetParticipantConnected(stableId, false);
                return;
            }

            ServerSetParticipantConnected(stableId, false);
            if (Phase == MatchPhase.Waiting) ServerDropParticipant(stableId);
        }

        /// <summary>Resign. Every match supports it, so clients only need this one command.</summary>        [Command(requiresAuthority = false)]
        public void CmdResign(NetworkConnectionToClient sender = null)
        {
            if (!isServer || sender == null || sender.identity == null) return;
            if (!sender.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            if (NetworkManager.singleton is SocketRoomManager manager &&
                !manager.ServerTryConsumeRate(sender.connectionId, RateLimitKind.GameCommand)) return;
            ServerForfeit(player.StableId, MatchForfeitCause.Surrender);
        }

        // ---- helpers for derived adapters -------------------------------------------------

        /// <summary>Configured thinking time per turn; 0 disables the turn timer.</summary>
        protected float TurnTimeoutSeconds =>
            NetworkManager.singleton is SocketRoomManager manager && manager.Config != null
                ? manager.Config.turnTimeoutSeconds
                : 0f;

        /// <summary>Re-armed after every confirmed state change; cleared once finished.</summary>
        [Server]
        protected void ArmTurnDeadline()
        {
            var timeout = TurnTimeoutSeconds;
            if (Phase == MatchPhase.Active && timeout > 0f)
            {
                turnDeadline = NetworkTime.localTime + timeout;
                turnSecondsRemaining = timeout;
            }
            else
            {
                turnDeadline = 0d;
                turnSecondsRemaining = 0f;
            }

            turnArmStamp++;
        }

        /// <summary>服务端每秒心跳：翻新同步给客户端的剩余秒数（值不变时 Mirror 不会发包）。</summary>
        [Server]
        public void ServerRefreshTurnSeconds()
        {
            if (turnDeadline <= 0d)
            {
                if (turnSecondsRemaining != 0f) turnSecondsRemaining = 0f;
                return;
            }

            var left = (float)(turnDeadline - NetworkTime.localTime);
            turnSecondsRemaining = left > 0f ? left : 0f;
        }

        [Server]
        protected void ClearEndState()
        {
            endReason = MatchEndReason.None;
            forfeitCause = MatchForfeitCause.None;
            endDetail = string.Empty;
        }

        [Server]
        protected void SetEndState(MatchEndReason reason, MatchForfeitCause cause, string detail)
        {
            endReason = reason;
            forfeitCause = cause;
            endDetail = detail ?? string.Empty;
        }

        /// <summary>名册签名：没变化就不碰 SyncList，避免每秒 Clear+Add 的整表重发。</summary>
        private string seatsSignature = string.Empty;

        /// <summary>Replicates the seat roster; games call it whenever seating changes.</summary>
        [Server]
        protected void SyncSeats(System.Collections.Generic.IEnumerable<MatchSeatInfo> roster)
        {
            var builder = new System.Text.StringBuilder();
            if (roster != null)
                foreach (var entry in roster)
                    builder.Append(entry.stableId).Append('|').Append(entry.displayName).Append('|').Append(entry.seat).Append(';');

            var signature = builder.ToString();
            if (signature == seatsSignature) return;
            seatsSignature = signature;

            seats.Clear();
            if (roster == null) return;
            foreach (var entry in roster) seats.Add(entry);
        }
    }
}
