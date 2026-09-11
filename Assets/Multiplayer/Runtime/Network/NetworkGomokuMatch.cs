using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Gomoku adapter on top of the framework match base. The server owns MatchSession;
    /// clients only receive snapshots and confirmed events. Everything shared with other
    /// games — turn timer, resignation, end state, disconnect handling — comes from
    /// <see cref="NetworkMatchAdapter"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class NetworkGomokuMatch : NetworkMatchAdapter
    {
        [SyncVar] private string matchIdText;
        [SyncVar] private MatchPhase phase;
        [SyncVar] private int boardSize;
        [SyncVar] private int turn;
        [SyncVar] private int currentSeat;
        [SyncVar] private GomokuResult result;
        [SyncVar] private string cellsText;
        [SyncVar] private int lastCellIndex = -1;

        private MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent> session;
        private GomokuRuleset ruleset;
        // Event-sourced replay data: the kernel already streams confirmed events into sinks,
        // so keeping them is all a replay needs (no separate move journal).
        private InMemoryMatchEventStore<GomokuEvent> eventStore;

        public override Guid MatchId => Guid.TryParse(matchIdText, out var value) ? value : Guid.Empty;
        public override MatchPhase Phase => phase;
        public override int CurrentSeat => currentSeat;
        public override int Progress => turn;

        public int BoardSize => boardSize;
        public int Turn => turn;
        public GomokuResult Result => result;
        public string CellsText => cellsText ?? string.Empty;
        public int LastCellIndex => lastCellIndex;

        // Game-side wording for the shared match HUD.
        public override string DescribeSeat(int seat) => seat == 0 ? "黑棋 X" : seat == 1 ? "白棋 O" : "观战";

        public override string DescribeResult()
        {
            switch (result)
            {
                case GomokuResult.BlackWin: return "黑棋 X 胜";
                case GomokuResult.WhiteWin: return "白棋 O 胜";
                case GomokuResult.Draw: return "和棋";
                default: return "无结果";
            }
        }

        public GomokuCell GetCell(int index)
        {
            var cells = CellsText;
            return index >= 0 && index < cells.Length
                ? cells[index] == 'B' ? GomokuCell.Black : cells[index] == 'W' ? GomokuCell.White : GomokuCell.Empty
                : GomokuCell.Empty;
        }

        public event Action StateChanged;
        public event Action<GomokuEvent> StoneConfirmed;

        [Server]
        public override void ServerInitialize(Guid roomId, RoomTemplate template)
        {
            var ruleConfig = template == null ? null : template.rules as GomokuRuleConfig;
            var matchRuleset = ruleConfig == null
                ? new GomokuRuleset(15, 5, true, true, false)
                : ruleConfig.CreateRuleset();
            matchIdText = roomId.ToString();
            ruleset = matchRuleset;
            var networkMatch = GetComponent<NetworkMatch>();
            networkMatch.matchId = roomId;
            session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                roomId, new GomokuState(matchRuleset.BoardSize), new GomokuRules(matchRuleset));
            eventStore = new InMemoryMatchEventStore<GomokuEvent>();
            session.AttachEventSink(eventStore);
            boardSize = matchRuleset.BoardSize;
            phase = MatchPhase.Waiting;
            result = GomokuResult.None;
            ClearEndState();
            RefreshSnapshot();
        }

        [Server]
        public override bool ServerAddPlayer(string stableId, string displayName, int seatIndex)
        {
            if (session == null) return false;
            var added = session.AddParticipant(new MatchParticipant(stableId, displayName, seatIndex, MatchParticipantRole.Player));
            SyncSeatRoster();
            return added;
        }

        [Server]
        public override bool ServerAddSpectator(string stableId, string displayName)
        {
            if (session == null) return false;
            var added = session.AddParticipant(new MatchParticipant(stableId, displayName, -1, MatchParticipantRole.Spectator));
            SyncSeatRoster();
            return added;
        }

        [Server]
        public override bool ServerDropParticipant(string stableId)
        {
            var removed = session != null && !string.IsNullOrEmpty(stableId) && session.RemoveParticipant(stableId);
            SyncSeatRoster();
            return removed;
        }

        [Server]
        public override bool ServerSetParticipantConnected(string stableId, bool connected)
        {
            return session != null && session.SetParticipantConnection(stableId, connected);
        }

        /// <summary>Stable id of the seated player in <paramref name="seat"/>; null when empty.</summary>
        [Server]
        public override string ServerFindStableIdBySeat(int seat)
        {
            if (session == null) return null;
            foreach (var participant in session.Participants)
                if (participant.Role == MatchParticipantRole.Player && participant.SeatIndex == seat)
                    return participant.StableId;
            return null;
        }

        /// <summary>
        /// Ends an active match with a framework cause (认输/掉线/离开/超时). Nobody is
        /// removed from the room, so the survivor can ready up for a rematch at once.
        /// </summary>
        [Server]
        public override bool ServerForfeit(string stableId, MatchForfeitCause cause)
        {
            if (session == null || string.IsNullOrEmpty(stableId)) return false;
            if (!session.Forfeit(stableId, cause, NetworkTime.localTime)) return false;
            RefreshSnapshot();
            return true;
        }

        [Server]
        public override MatchRecordInfo ServerCreateRecord(string roomName)
        {
            var keepReplay = NetworkManager.singleton is SocketRoomManager manager && manager.Config != null &&
                             manager.Config.recordReplays;
            return new MatchRecordInfo
            {
                matchId = MatchId,
                roomName = roomName ?? string.Empty,
                finishedAtLabel = DateTime.Now.ToString("HH:mm:ss"),
                resultLabel = DescribeResult(),
                endLabel = MatchRecordLabels.DescribeEnd(EndReason, ForfeitCause, EndDetail),
                seatsLabel = DescribeSeats(),
                replayPayload = keepReplay ? BuildReplayPayload() : string.Empty
            };
        }

        // "index:colour,index:colour" — compact, and the colour makes a replay verifiable.
        [Server]
        private string BuildReplayPayload()
        {
            if (eventStore == null) return string.Empty;
            var builder = new System.Text.StringBuilder();
            foreach (var record in eventStore.Records)
            {
                if (record.Event.Kind != GomokuEventKind.StonePlaced) continue;
                if (builder.Length > 0) builder.Append(',');
                builder.Append(record.Event.CellIndex);
                builder.Append(record.Event.Cell == GomokuCell.Black ? ":B" : ":W");
            }
            return builder.ToString();
        }

        [Server]
        public override void ServerResetMatch()
        {
            if (ruleset.BoardSize < 1) return;
            session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                MatchId, new GomokuState(ruleset.BoardSize), new GomokuRules(ruleset));
            eventStore = new InMemoryMatchEventStore<GomokuEvent>();
            session.AttachEventSink(eventStore);
            phase = MatchPhase.Waiting;
            turn = 0;
            currentSeat = 0;
            result = GomokuResult.None;
            ClearEndState();
            RefreshSnapshot();
        }

        [Server]
        public override bool ServerStartMatch()
        {
            if (session == null || session.PlayerCount < 2 || !session.Start()) return false;
            RefreshSnapshot();
            return true;
        }

        [Command(requiresAuthority = false)]
        public void CmdPlaceStone(int cellIndex, NetworkConnectionToClient sender = null)
        {
            if (!isServer || sender == null || sender.identity == null) return;
            if (!sender.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            if (player.IsSpectator) return;
            // Rule rejections are cheap to spam; cap the source before touching the kernel.
            if (NetworkManager.singleton is SocketRoomManager rateManager &&
                !rateManager.ServerTryConsumeRate(sender.connectionId, RateLimitKind.GameCommand)) return;

            var result = session == null
                ? MatchCommandResult<GomokuEvent>.Reject(MatchRejectReason.NotActive, "Match is not initialized.")
                : session.Submit(player.StableId, new PlaceStoneCommand(cellIndex), NetworkTime.localTime);
            if (!result.Accepted)
            {
                // Silent drops read as "the board is broken"; tell the mover why. The reason
                // is a structured enum, so no layer parses kernel debug text.
                if (NetworkManager.singleton is SocketRoomManager rejectionManager)
                    rejectionManager.ServerReportMatchRejection(sender, result.Reason, result.Error);
                return;
            }

            RefreshSnapshot();
            RpcStoneConfirmed(result.Event.PlayerId, result.Event.Cell, result.Event.CellIndex, result.Event.Turn, result.Event.Result);
        }

        [ClientRpc]
        private void RpcStoneConfirmed(string playerId, GomokuCell cell, int cellIndex, int confirmedTurn, GomokuResult confirmedResult)
        {
            var gameEvent = new GomokuEvent(GomokuEventKind.StonePlaced, playerId, cell, cellIndex, confirmedTurn, confirmedResult);
            StoneConfirmed?.Invoke(gameEvent);
            StateChanged?.Invoke();
        }

        [Server]
        private void RefreshSnapshot()
        {
            if (session == null) return;
            var state = session.State;
            phase = session.Phase;
            boardSize = state.BoardSize;
            turn = state.Turn;
            currentSeat = state.CurrentSeat;
            result = state.Result;
            cellsText = SerializeCells(state);
            lastCellIndex = state.LastCellIndex;
            SyncEndStateFromSession();
            SyncSeatRoster();
            ArmTurnDeadline();
            StateChanged?.Invoke();
        }

        // Framework roster (name + seat) so any client can label 对战/观战 without guessing.
        [Server]
        private void SyncSeatRoster()
        {
            if (session == null)
            {
                SyncSeats(null);
                return;
            }

            var roster = new List<MatchSeatInfo>();
            foreach (var participant in session.Participants)
                roster.Add(new MatchSeatInfo
                {
                    stableId = participant.StableId,
                    displayName = participant.DisplayName,
                    seat = participant.SeatIndex
                });
            SyncSeats(roster);
        }

        // End state lives in the kernel (reason + forfeit cause); only the winning-shape
        // label is game-side.
        [Server]
        private void SyncEndStateFromSession()
        {
            if (phase == MatchPhase.Waiting)
            {
                ClearEndState();
                return;
            }
            if (phase != MatchPhase.Completed) return;

            switch (session.EndReason)
            {
                case MatchEndReason.Forfeit:
                    SetEndState(MatchEndReason.Forfeit, session.ForfeitCause, string.Empty);
                    return;
                case MatchEndReason.Cancelled:
                    SetEndState(MatchEndReason.Cancelled, MatchForfeitCause.None, string.Empty);
                    return;
                default:
                    SetEndState(MatchEndReason.RulesDecided, MatchForfeitCause.None, result == GomokuResult.Draw ? "和棋" : "五连");
                    return;
            }
        }

        private static string SerializeCells(GomokuState state)
        {
            var chars = new char[state.CellCount];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = state.GetCell(i) == GomokuCell.Black ? 'B' : state.GetCell(i) == GomokuCell.White ? 'W' : '.';
            return new string(chars);
        }
    }
}
