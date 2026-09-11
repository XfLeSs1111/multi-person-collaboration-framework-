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

        [Test]
        public void LeavingSeatedPlayerForfeitsTheMatchInsteadOfDeadlocking()
        {
            var state = new GomokuState(15);
            var session = CreateSession(state);
            var store = new InMemoryMatchEventStore<GomokuEvent>();
            session.AttachEventSink(store);

            Assert.IsTrue(session.Forfeit("alice", MatchForfeitCause.Surrender, 2d));

            Assert.AreEqual(MatchPhase.Completed, session.Phase);
            Assert.AreEqual(MatchEndReason.Forfeit, session.EndReason);
            Assert.AreEqual(MatchForfeitCause.Surrender, session.ForfeitCause);
            Assert.AreEqual(GomokuResult.WhiteWin, state.Result);
            Assert.IsFalse(session.Submit("bob", new PlaceStoneCommand(0), 3d).Accepted);
            Assert.AreEqual(1, store.Records.Count);
            Assert.AreEqual(GomokuEventKind.MatchEnded, store.Records[0].Event.Kind);
        }

        [Test]
        public void ForfeitIgnoresSpectatorsUnknownIdsAndWaitingMatches()
        {
            // Waiting match: forfeit is not applicable until play starts.
            var waitingSession = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), new GomokuState(15), new GomokuRules(Ruleset));
            waitingSession.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            waitingSession.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsFalse(waitingSession.Forfeit("alice", MatchForfeitCause.Timeout, 1d));

            var state = new GomokuState(15);
            var session = CreateSession(state);
            Assert.IsTrue(session.AddParticipant(new MatchParticipant("viewer", "Viewer", -1, MatchParticipantRole.Spectator)));
            Assert.IsFalse(session.Forfeit("viewer", MatchForfeitCause.Leave, 1d));
            Assert.IsFalse(session.Forfeit("unknown", MatchForfeitCause.Leave, 1d));
            Assert.AreEqual(MatchPhase.Active, session.Phase);
        }

        [Test]
        public void DiagonalWinAndFullBoardDraw()
        {
            var state = new GomokuState(15);
            var session = CreateSession(state);

            // Anti-diagonal (1,-1): 60=(4,0), 46=(3,1), 32=(2,2), 18=(1,3), 4=(0,4)
            var moves = new[] { 60, 1, 46, 2, 32, 3, 18, 5, 4 };
            for (var i = 0; i < moves.Length; i++)
                Assert.IsTrue(session.Submit(i % 2 == 0 ? "alice" : "bob", new PlaceStoneCommand(moves[i]), i).Accepted);

            Assert.AreEqual(GomokuResult.BlackWin, state.Result);
            Assert.AreEqual(4, state.LastCellIndex);
            Assert.AreEqual(MatchPhase.Completed, session.Phase);

            // 3x3 with an unreachable win length: filling the board is a draw.
            var tinyState = new GomokuState(3);
            var tinySession = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), tinyState, new GomokuRules(new GomokuRuleset(3, 5, true, true, true)));
            tinySession.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            tinySession.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsTrue(tinySession.Start());
            for (var i = 0; i < 9; i++)
                Assert.IsTrue(tinySession.Submit(i % 2 == 0 ? "alice" : "bob", new PlaceStoneCommand(i), i).Accepted);

            Assert.AreEqual(GomokuResult.Draw, tinyState.Result);
            Assert.AreEqual(8, tinyState.LastCellIndex);
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
