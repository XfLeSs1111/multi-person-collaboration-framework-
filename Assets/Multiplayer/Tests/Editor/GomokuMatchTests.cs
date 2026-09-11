#if UNITY_EDITOR
using System;
using NUnit.Framework;

namespace Socket.Multiplayer.Tests
{
    public sealed class GomokuMatchTests
    {
        private static readonly GomokuRuleset Ruleset = new GomokuRuleset(15, 5, true, true, true);

        [Test]
        public void PlayersMustTakeTurnsAndCannotReuseACell()
        {
            var state = new GomokuState(15);
            var session = CreateSession(state);

            Assert.IsTrue(session.Submit("alice", new PlaceStoneCommand(0), 1d).Accepted);
            Assert.IsFalse(session.Submit("alice", new PlaceStoneCommand(1), 2d).Accepted);
            Assert.IsFalse(session.Submit("bob", new PlaceStoneCommand(0), 3d).Accepted);
            Assert.IsTrue(session.Submit("bob", new PlaceStoneCommand(1), 4d).Accepted);
        }

        [Test]
        public void SpectatorCannotSubmitCommands()
        {
            var state = new GomokuState(15);
            var session = CreateSession(state);
            Assert.IsTrue(session.AddParticipant(new MatchParticipant("viewer", "Viewer", -1, MatchParticipantRole.Spectator)));

            var result = session.Submit("viewer", new PlaceStoneCommand(0), 1d);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(GomokuCell.Empty, state.GetCell(0));
        }

        [Test]
        public void WinningEventCompletesMatchAndCanBeReplayed()
        {
            var state = new GomokuState(15);
            var rules = new GomokuRules(Ruleset);
            var session = CreateSession(state, rules);
            var store = new InMemoryMatchEventStore<GomokuEvent>();
            session.AttachEventSink(store);

            var moves = new[] { 0, 15, 1, 16, 2, 17, 3, 18, 4 };
            for (var i = 0; i < moves.Length; i++)
                Assert.IsTrue(session.Submit(i % 2 == 0 ? "alice" : "bob", new PlaceStoneCommand(moves[i]), i).Accepted);

            Assert.AreEqual(MatchPhase.Completed, session.Phase);
            Assert.AreEqual(GomokuResult.BlackWin, state.Result);
            Assert.AreEqual(9, store.Records.Count);

            var replay = new MatchReplay<GomokuState, GomokuEvent>(new GomokuState(15), store.Records, rules);
            var restored = replay.RestoreThrough(store.Records.Count);
            Assert.AreEqual(GomokuResult.BlackWin, restored.Result);
            Assert.AreEqual(GomokuCell.Black, restored.GetCell(4));
            Assert.AreEqual(GomokuCell.White, restored.GetCell(18));
        }

        [Test]
        public void SnapshotDoesNotExposeMutableMatchState()
        {
            var state = new GomokuState(15);
            var session = CreateSession(state);
            var snapshot = session.CreateSnapshot();

            Assert.IsTrue(session.Submit("alice", new PlaceStoneCommand(0), 1d).Accepted);

            Assert.AreEqual(GomokuCell.Empty, snapshot.GetCell(0));
            Assert.AreEqual(GomokuCell.Black, session.State.GetCell(0));
        }

        private static MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent> CreateSession(
            GomokuState state,
            GomokuRules rules = null)
        {
            var session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), state, rules ?? new GomokuRules(Ruleset));
            session.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            session.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsTrue(session.Start());
            return session;
        }
    }
}
#endif
