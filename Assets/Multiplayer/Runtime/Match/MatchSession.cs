using System;
using System.Collections.Generic;
using System.Linq;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Network-agnostic authoritative match kernel. Mirror adapters call Submit;
    /// the kernel owns permissions, rules, phase and confirmed event ordering.
    /// </summary>
    public sealed class MatchSession<TState, TCommand, TEvent> : IMatchSnapshotProvider<TState>
        where TState : class, IMatchState<TState>
    {
        private readonly Dictionary<string, MatchParticipant> participants = new Dictionary<string, MatchParticipant>(StringComparer.Ordinal);
        private readonly List<IMatchEventSink<TEvent>> eventSinks = new List<IMatchEventSink<TEvent>>();
        private readonly IMatchRules<TState, TCommand, TEvent> rules;
        private readonly TState state;
        private int eventSequence;

        public Guid MatchId { get; }
        public MatchPhase Phase { get; private set; }
        /// <summary>Why the match left the Active phase; None while waiting/active.</summary>
        public MatchEndReason EndReason { get; private set; }
        /// <summary>Which kind of forfeit ended the match (Surrender/Disconnect/Leave/Timeout).</summary>
        public MatchForfeitCause ForfeitCause { get; private set; }
        public TState State => state;
        public IReadOnlyCollection<MatchParticipant> Participants => participants.Values;
        public int PlayerCount => participants.Values.Count(item => item.Role == MatchParticipantRole.Player);
        public event Action<MatchEventRecord<TEvent>> EventConfirmed;

        public MatchSession(Guid matchId, TState state, IMatchRules<TState, TCommand, TEvent> rules)
        {
            if (matchId == Guid.Empty) throw new ArgumentException("Match id is required.", nameof(matchId));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            MatchId = matchId;
            Phase = MatchPhase.Waiting;
        }

        public bool AddParticipant(MatchParticipant participant)
        {
            if (participant == null || participants.ContainsKey(participant.StableId)) return false;
            participants.Add(participant.StableId, participant);
            return true;
        }

        public bool RemoveParticipant(string stableId) => participants.Remove(stableId);

        /// <summary>
        /// 显式座位绑定：新增落座玩家，或把已有参与者（如观战者）升级为指定座位的玩家。
        /// 升级会保留原连接状态；对局进行中拒绝角色变更（避免中途插入打乱回合），
        /// 名册实际发生变化时返回 true。
        /// </summary>
        public bool AssignSeat(string stableId, string displayName, int seatIndex)
        {
            if (string.IsNullOrEmpty(stableId) || seatIndex < 0) return false;
            if (participants.TryGetValue(stableId, out var existing))
            {
                if (existing.Role == MatchParticipantRole.Player && existing.SeatIndex == seatIndex) return false;
                if (Phase == MatchPhase.Active) return false;
                participants[stableId] = CopyWith(existing, MatchParticipantRole.Player, seatIndex);
                return true;
            }

            participants[stableId] = new MatchParticipant(stableId, displayName, seatIndex, MatchParticipantRole.Player);
            return true;
        }

        /// <summary>
        /// 观战位分配：只新增观战者，绝不把已有玩家降级（座位释放由房间层决定）。
        /// 名册实际发生变化时返回 true。
        /// </summary>
        public bool AssignSpectator(string stableId, string displayName)
        {
            if (string.IsNullOrEmpty(stableId)) return false;
            if (participants.ContainsKey(stableId)) return false;
            participants[stableId] = new MatchParticipant(stableId, displayName, -1, MatchParticipantRole.Spectator);
            return true;
        }

        private static MatchParticipant CopyWith(MatchParticipant source, MatchParticipantRole role, int seatIndex)
        {
            var copy = new MatchParticipant(source.StableId, source.DisplayName, seatIndex, role);
            if (!source.IsConnected) copy.SetConnected(false);
            return copy;
        }

        public bool SetParticipantConnection(string stableId, bool connected)
        {
            return participants.TryGetValue(stableId, out var participant) && SetConnection(participant, connected);
        }

        /// <summary>
        /// Ends an active match because a seated participant left (disconnect or quit).
        /// Without this the remaining players deadlock on a turn that can never come
        /// (Gomoku only advances the seat on a placement). Scoring is delegated to
        /// <see cref="IMatchForfeitRules{TState,TEvent}"/> and the final event flows
        /// through the same sinks and EventConfirmed as regular commands.
        /// </summary>
        public bool Forfeit(string stableId, MatchForfeitCause cause, double serverTime)
        {
            if (Phase != MatchPhase.Active) return false;
            if (stableId == null || !participants.TryGetValue(stableId, out var leaver)) return false;
            if (leaver.Role != MatchParticipantRole.Player) return false;
            if (!(rules is IMatchForfeitRules<TState, TEvent> forfeitRules)) return false;
            if (!forfeitRules.TryForfeit(state, leaver, out var finalEvent)) return false;

            Phase = MatchPhase.Completed;
            EndReason = MatchEndReason.Forfeit;
            ForfeitCause = cause;
            var record = new MatchEventRecord<TEvent>(++eventSequence, serverTime, finalEvent);
            foreach (var sink in eventSinks.ToArray()) sink.Append(record);
            EventConfirmed?.Invoke(record);
            return true;
        }

        public bool Start()
        {
            if (Phase != MatchPhase.Waiting) return false;
            if (!participants.Values.Any(item => item.Role == MatchParticipantRole.Player)) return false;
            Phase = MatchPhase.Active;
            EndReason = MatchEndReason.None;
            ForfeitCause = MatchForfeitCause.None;
            return true;
        }

        public MatchCommandResult<TEvent> Submit(string stableId, TCommand command, double serverTime)
        {
            if (Phase != MatchPhase.Active)
                return MatchCommandResult<TEvent>.Reject(MatchRejectReason.NotActive, "Match is not active.");
            if (!participants.TryGetValue(stableId, out var actor))
                return MatchCommandResult<TEvent>.Reject(MatchRejectReason.NotParticipant, "Participant is not registered.");
            if (actor.Role != MatchParticipantRole.Player)
                return MatchCommandResult<TEvent>.Reject(MatchRejectReason.NotParticipant, "Only players can submit commands.");
            if (!actor.IsConnected)
                return MatchCommandResult<TEvent>.Reject(MatchRejectReason.NotConnected, "Participant is disconnected.");

            var result = rules.TryApply(state, actor, command);
            if (!result.Accepted) return result;

            var record = new MatchEventRecord<TEvent>(++eventSequence, serverTime, result.Event);
            foreach (var sink in eventSinks.ToArray()) sink.Append(record);
            EventConfirmed?.Invoke(record);
            if (rules.IsFinished(state))
            {
                Phase = MatchPhase.Completed;
                EndReason = MatchEndReason.RulesDecided;
            }
            return result;
        }

        public void Cancel()
        {
            if (Phase == MatchPhase.Completed) return;
            Phase = MatchPhase.Cancelled;
            EndReason = MatchEndReason.Cancelled;
        }

        public void AttachEventSink(IMatchEventSink<TEvent> sink)
        {
            if (sink != null && !eventSinks.Contains(sink)) eventSinks.Add(sink);
        }

        public void DetachEventSink(IMatchEventSink<TEvent> sink) => eventSinks.Remove(sink);

        public TState CreateSnapshot() => state.Clone();

        private static bool SetConnection(MatchParticipant participant, bool connected)
        {
            participant.SetConnected(connected);
            return true;
        }
    }

    public sealed class InMemoryMatchEventStore<TEvent> : IMatchEventSink<TEvent>
    {
        private readonly List<MatchEventRecord<TEvent>> records = new List<MatchEventRecord<TEvent>>();
        public IReadOnlyList<MatchEventRecord<TEvent>> Records => records;
        public void Append(MatchEventRecord<TEvent> record) => records.Add(record);
    }
}
