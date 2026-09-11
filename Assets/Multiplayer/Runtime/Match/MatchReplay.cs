using System;
using System.Collections.Generic;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Offline replay cursor. It replays confirmed domain events and never touches Mirror.
    /// </summary>
    public sealed class MatchReplay<TState, TEvent>
        where TState : class, IMatchState<TState>
    {
        private readonly TState initialState;
        private readonly IReadOnlyList<MatchEventRecord<TEvent>> records;
        private readonly IMatchEventApplier<TState, TEvent> applier;

        public int EventCount => records.Count;

        public MatchReplay(TState initialState, IReadOnlyList<MatchEventRecord<TEvent>> records, IMatchEventApplier<TState, TEvent> applier)
        {
            this.initialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
            this.records = records ?? throw new ArgumentNullException(nameof(records));
            this.applier = applier ?? throw new ArgumentNullException(nameof(applier));
        }

        public TState RestoreThrough(int eventCount)
        {
            eventCount = Math.Max(0, Math.Min(eventCount, records.Count));
            var restored = initialState.Clone();
            for (var i = 0; i < eventCount; i++)
                applier.Apply(restored, records[i].Event);
            return restored;
        }
    }
}
