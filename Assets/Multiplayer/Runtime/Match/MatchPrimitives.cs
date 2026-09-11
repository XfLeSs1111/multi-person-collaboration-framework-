using System;

namespace Socket.Multiplayer
{
    public enum MatchPhase : byte
    {
        Waiting,
        Active,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Structured rejection reason for a match command. Framework-level on purpose: no
    /// layer should have to parse kernel debug text to tell "not your turn" apart from
    /// "that command is illegal".
    /// </summary>
    public enum MatchRejectReason : byte
    {
        None = 0,
        NotActive,
        NotParticipant,
        NotConnected,
        NotYourTurn,
        InvalidCommand
    }

    /// <summary>How a match left the Active phase (framework lifecycle).</summary>
    public enum MatchEndReason : byte
    {
        None = 0,
        RulesDecided,
        Forfeit,
        Cancelled
    }

    /// <summary>
    /// Why a forfeit happened. Game-agnostic: any real-time match can lose a player to
    /// resignation, a disconnect, leaving the room, or running out of turn time.
    /// </summary>
    public enum MatchForfeitCause : byte
    {
        None = 0,
        Surrender,
        Disconnect,
        Leave,
        Timeout
    }

    public readonly struct MatchCommandResult<TEvent>
    {
        public bool Accepted { get; }
        public TEvent Event { get; }
        public MatchRejectReason Reason { get; }
        public string Error { get; }

        private MatchCommandResult(bool accepted, TEvent gameEvent, MatchRejectReason reason, string error)
        {
            Accepted = accepted;
            Event = gameEvent;
            Reason = reason;
            Error = error ?? string.Empty;
        }

        public static MatchCommandResult<TEvent> Accept(TEvent gameEvent) =>
            new MatchCommandResult<TEvent>(true, gameEvent, MatchRejectReason.None, string.Empty);

        /// <summary>Structured rejection; <paramref name="error"/> is debug text only.</summary>
        public static MatchCommandResult<TEvent> Reject(MatchRejectReason reason, string error) =>
            new MatchCommandResult<TEvent>(false, default(TEvent), reason, error);

        /// <summary>A rule rejected the command itself (occupied point, illegal shape…).</summary>
        public static MatchCommandResult<TEvent> Reject(string error) =>
            Reject(MatchRejectReason.InvalidCommand, error);
    }

    public readonly struct MatchEventRecord<TEvent>
    {
        public int Sequence { get; }
        public double ServerTime { get; }
        public TEvent Event { get; }

        public MatchEventRecord(int sequence, double serverTime, TEvent gameEvent)
        {
            Sequence = sequence;
            ServerTime = serverTime;
            Event = gameEvent;
        }
    }

    public interface IMatchRules<TState, in TCommand, TEvent>
    {
        MatchCommandResult<TEvent> TryApply(TState state, MatchParticipant actor, TCommand command);
        bool IsFinished(TState state);
    }

    /// <summary>
    /// Optional companion to <see cref="IMatchRules{TState,TCommand,TEvent}"/>: how an
    /// active match ends when a seated participant leaves (disconnect or quit). Without
    /// it the remaining players deadlock on a turn that can never come. The rules map
    /// the leaver to a game-specific result and produce the final confirmed event.
    /// </summary>
    public interface IMatchForfeitRules<TState, TEvent>
    {
        bool TryForfeit(TState state, MatchParticipant leaver, out TEvent finalEvent);
    }

    public interface IMatchEventSink<TEvent>
    {
        void Append(MatchEventRecord<TEvent> record);
    }

    public interface IMatchSnapshotProvider<out TState>
    {
        TState CreateSnapshot();
    }

    public interface IMatchState<TState>
    {
        TState Clone();
    }

    public interface IMatchEventApplier<in TState, in TEvent>
    {
        void Apply(TState state, TEvent gameEvent);
    }
}
