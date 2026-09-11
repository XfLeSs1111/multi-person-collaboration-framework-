using System;
using NUnit.Framework;

namespace Socket.Multiplayer.Tests
{
    /// <summary>
    /// Pure-logic CI mirror of the Unity EditMode suite: runs in seconds via
    /// `dotnet test` (no editor, no domain reload). Keep in sync with
    /// Assets/Multiplayer/Tests/Editor/*.
    /// </summary>
    public sealed class PureLogicTests
    {
        [Test]
        public void RoomLifecycle_CreateJoinReadyStartAndLeave()
        {
            var registry = new RoomRegistry();
            Assert.IsTrue(registry.TryAddPlayer(1, "Alice", out _));
            Assert.IsTrue(registry.TryAddPlayer(2, "Bob", out _));
            Assert.IsTrue(registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _));
            Assert.IsTrue(registry.TryJoinRoom(2, room.Id, false, out _, out _, out _));
            Assert.IsFalse(registry.TryStartRoom(1, 2, out _, out _, out var notReady));
            Assert.AreEqual(MultiplayerErrorCode.NotAllReady, notReady);

            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out room, out _, out _));
            Assert.AreEqual(RoomPhase.InGame, room.Phase);

            Assert.IsTrue(registry.TryLeaveRoom(1, out _, out _, out _, out _));
            Assert.IsTrue(registry.TryGetPlayer(2, out var promoted));
            Assert.IsTrue(promoted.IsLeader);
        }

        [Test]
        public void RoomLimit_ReturnsStructuredError()
        {
            var registry = new RoomRegistry(1);
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out _, out _, out _));
            Assert.IsFalse(registry.TryCreateRoom(2, "B", 2, out _, out var error, out var code));
            Assert.AreEqual("Room limit reached.", error);
            Assert.AreEqual(MultiplayerErrorCode.RoomLimitReached, code);
        }

        [Test]
        public void AdmissionAndRateLimits_BlockSpam()
        {
            var admission = new InputAdmission(20f);
            Assert.IsTrue(admission.TryAccept(1, 0d));
            Assert.IsFalse(admission.TryAccept(2, 0.01d));
            Assert.IsFalse(admission.TryAccept(1, 0.1d));
            Assert.IsTrue(admission.TryAccept(2, 0.03d));

            var bucket = new TokenBucket(2, 2, 0);
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsFalse(bucket.TryConsume(0));
            Assert.IsTrue(bucket.TryConsume(0.5d));

            var limiter = new ConnectionRateLimiter(2f, 2f, 2f, 2f);
            for (var i = 0; i < 4; i++) Assert.IsTrue(limiter.TryAcquire(9, RateLimitKind.Chat, 0d));
            Assert.IsFalse(limiter.TryAcquire(9, RateLimitKind.Chat, 0d));
            Assert.IsTrue(limiter.TryAcquire(9, RateLimitKind.Interact, 0d));
            limiter.Forget(9);
            Assert.IsTrue(limiter.TryAcquire(9, RateLimitKind.Chat, 0d));
        }

        [Test]
        public void InteractionLeaseRules_CoverAllBranches()
        {
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(false, 0, 1, 1f, 3f).Accepted); // inactive
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(true, 0, 0, 1f, 3f).Accepted);  // invalid requester
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(true, 7, 1, 3.1f, 3f).Accepted); // held by other wins over range check
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(true, 0, 1, 3.1f, 3f).Accepted); // out of range
            Assert.IsTrue(InteractionLeaseRules.TryAcquire(true, 0, 1, 3f, 3f).Accepted);   // boundary is in range
            Assert.IsTrue(InteractionLeaseRules.TryAcquire(true, 7, 7, 1f, 3f).Accepted);   // holder re-entry

            Assert.IsFalse(InteractionLeaseRules.CanRelease(0, 1));
            Assert.IsFalse(InteractionLeaseRules.CanRelease(7, 1));
            Assert.IsTrue(InteractionLeaseRules.CanRelease(7, 7));
        }

        [Test]
        public void IdleRooms_RecycleAfterTimeoutAndReturnMembers()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryCreateRoom(1, "A", 2, out var room, out _, out _);
            registry.TouchRoom(room.Id, 100d);

            var recycled = new System.Collections.Generic.List<RoomRegistry.Room>();
            Assert.AreEqual(0, registry.CollectIdleRooms(200d, 120d, recycled));
            Assert.AreEqual(1, registry.CollectIdleRooms(220d, 120d, recycled));
            Assert.IsTrue(registry.TryGetPlayer(1, out var alice));
            Assert.AreEqual(LobbyRoom.Id, alice.RoomId);

            // Untracked rooms (LastActivityTime == 0) are never recycled by the sweep.
            registry.TryCreateRoom(1, "B", 2, out var fresh, out _, out _);
            Assert.AreEqual(0, registry.CollectIdleRooms(999999d, 120d, recycled));
            Assert.IsTrue(registry.TryGetRoom(fresh.Id, out _));
        }

        [Test]
        public void Gomoku_TurnsAndWinCompleteTheMatch()
        {
            var ruleset = new GomokuRuleset(15, 5, true, true, true);
            var state = new GomokuState(15);
            var session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), state, new GomokuRules(ruleset));
            session.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            session.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsTrue(session.Start());

            var moves = new[] { 0, 15, 1, 16, 2, 17, 3, 18, 4 };
            for (var i = 0; i < moves.Length; i++)
                Assert.IsTrue(session.Submit(i % 2 == 0 ? "alice" : "bob", new PlaceStoneCommand(moves[i]), i).Accepted);

            Assert.AreEqual(MatchPhase.Completed, session.Phase);
            Assert.AreEqual(GomokuResult.BlackWin, state.Result);
        }

        [Test]
        public void Gomoku_LeaverForfeitsInsteadOfDeadlocking()
        {
            var ruleset = new GomokuRuleset(15, 5, true, true, true);
            var state = new GomokuState(15);
            var session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), state, new GomokuRules(ruleset));
            session.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            session.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsTrue(session.Start());

            Assert.IsTrue(session.Forfeit("alice", 2d));
            Assert.AreEqual(MatchPhase.Completed, session.Phase);
            Assert.AreEqual(GomokuResult.WhiteWin, state.Result);
            Assert.IsFalse(session.Submit("bob", new PlaceStoneCommand(0), 3d).Accepted);
        }
    }
}
