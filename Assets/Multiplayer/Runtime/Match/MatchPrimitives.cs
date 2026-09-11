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

    public readonly struct MatchCommandResult<TEvent>
    {
        public bool Accepted { get; }
        public TEvent Event { get; }
        public string Error { get; }

        private MatchCommandResult(bool accepted, TEvent gameEvent, string error)
        {
            Accepted = accepted;
            Event = gameEvent;
            Error = error ?? string.Empty;
        }

        public static MatchCommandResult<TEvent> Accept(TEvent gameEvent) =>
            new MatchCommandResult<TEvent>(true, gameEvent, string.Empty);

        public static MatchCommandResult<TEvent> Reject(string error) =>
            new MatchCommandResult<TEvent>(false, default(TEvent), error);
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
