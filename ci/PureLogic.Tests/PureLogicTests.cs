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
        public void InGameRooms_AreNotRecycledByIdleSweep()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryCreateRoom(1, "A", 2, out var room, out _, out _);
            registry.TouchRoom(room.Id, 100d);

            // 进行中的对局不能被闲置扫描销毁（由对局流程自行收尾）。
            room.Phase = RoomPhase.InGame;
            var recycled = new System.Collections.Generic.List<RoomRegistry.Room>();
            Assert.AreEqual(0, registry.CollectIdleRooms(100000d, 120d, recycled));
            Assert.IsTrue(registry.TryGetRoom(room.Id, out _));

            // 回到大厅后可正常回收。
            room.Phase = RoomPhase.Lobby;
            Assert.AreEqual(1, registry.CollectIdleRooms(100000d, 120d, recycled));
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
        }

        [Test]
        public void InteractionLease_RejectsNonFiniteDistance()
        {
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(true, 0, 1, float.NaN, 3f).Accepted);
            Assert.IsFalse(InteractionLeaseRules.TryAcquire(true, 0, 1, float.PositiveInfinity, 3f).Accepted);
            Assert.IsTrue(InteractionLeaseRules.TryAcquire(true, 0, 1, 2.5f, 3f).Accepted);
        }

        [Test]
        public void Seats_AreStableAcrossMemberLeaves()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "A", out _);
            registry.TryAddPlayer(2, "B", out _);
            registry.TryAddPlayer(3, "C", out _);
            registry.TryCreateRoom(1, "Room", 8, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);
            registry.TryJoinRoom(3, room.Id, false, out _, out _, out _);

            registry.TryGetPlayer(1, out var a);
            registry.TryGetPlayer(2, out var b);
            registry.TryGetPlayer(3, out var c);
            Assert.AreEqual(0, a.Seat);
            Assert.AreEqual(1, b.Seat);
            Assert.AreEqual(2, c.Seat);

            // B 退出后 C 的座位不平移（旧实现用下标推导，C 会被“继承”到 1 号位）。
            registry.RemovePlayer(2, 0d, out _);
            registry.TryGetPlayer(3, out c);
            Assert.AreEqual(2, c.Seat);

            // 空出的 1 号座位仍可被新人使用 → 两人满座，房间可正常开局。
            registry.TryAddPlayer(4, "D", out _);
            registry.TryJoinRoom(4, room.Id, false, out _, out _, out _);
            registry.TryGetPlayer(4, out var d);
            Assert.AreEqual(1, d.Seat);
        }

        [Test]
        public void MatchSession_AssignSeat_OnlyPromotesWhileWaiting()
        {
            var session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), new GomokuState(15), new GomokuRules(new GomokuRuleset(15, 5, true, true, true)));

            Assert.IsTrue(session.AssignSpectator("c", "C"));
            Assert.IsFalse(session.AssignSpectator("c", "C"));
            Assert.AreEqual(0, session.PlayerCount);

            Assert.IsTrue(session.AssignSeat("c", "C", 1));
            Assert.AreEqual(1, session.PlayerCount);
            Assert.IsFalse(session.AssignSeat("c", "C", 1));

            Assert.IsTrue(session.AssignSeat("a", "A", 0));
            Assert.IsTrue(session.Start());
            Assert.IsFalse(session.AssignSeat("c", "C", 2));
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

            Assert.IsTrue(session.Forfeit("alice", MatchForfeitCause.Disconnect, 2d));
            Assert.AreEqual(MatchPhase.Completed, session.Phase);
            Assert.AreEqual(MatchEndReason.Forfeit, session.EndReason);
            Assert.AreEqual(MatchForfeitCause.Disconnect, session.ForfeitCause);
            Assert.AreEqual(GomokuResult.WhiteWin, state.Result);
            Assert.IsFalse(session.Submit("bob", new PlaceStoneCommand(0), 3d).Accepted);
        }

        [Test]
        public void MatchRecordBook_KeepsOnlyTheConfiguredTail()
        {
            var roomId = Guid.NewGuid();
            var book = new MatchRecordBook(2);
            Assert.IsTrue(book.Enabled);

            for (var i = 0; i < 3; i++)
                book.Record(roomId, new MatchRecordInfo { matchId = Guid.NewGuid(), resultLabel = "r" + i, replayPayload = "0:B" });

            var records = book.Get(roomId);
            Assert.AreEqual(2, records.Count);
            Assert.AreEqual("r1", records[0].resultLabel);
            Assert.IsTrue(records[0].HasReplay);

            book.Forget(roomId);
            Assert.AreEqual(0, book.Get(roomId).Count);

            // 0 条 = 策略关闭：既不存也不报错。
            var disabled = new MatchRecordBook(0);
            Assert.IsFalse(disabled.Enabled);
            disabled.Record(roomId, new MatchRecordInfo());
            Assert.AreEqual(0, disabled.Get(roomId).Count);
        }

        [Test]
        public void MatchRecordLabels_DescribeEveryEnding()
        {
            Assert.AreEqual("认输", MatchRecordLabels.DescribeEnd(MatchEndReason.Forfeit, MatchForfeitCause.Surrender, string.Empty));
            Assert.AreEqual("对方掉线", MatchRecordLabels.DescribeEnd(MatchEndReason.Forfeit, MatchForfeitCause.Disconnect, string.Empty));
            Assert.AreEqual("超时判负", MatchRecordLabels.DescribeEnd(MatchEndReason.Forfeit, MatchForfeitCause.Timeout, string.Empty));
            Assert.AreEqual("和棋", MatchRecordLabels.DescribeEnd(MatchEndReason.RulesDecided, MatchForfeitCause.None, "和棋"));
            Assert.AreEqual("已结束", MatchRecordLabels.DescribeEnd(MatchEndReason.RulesDecided, MatchForfeitCause.None, string.Empty));
        }

        [Test]
        public void ResetToLobby_ClearsReadyAndUnsticksTheRoom()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out _, out _, out _));

            Assert.IsTrue(registry.ResetToLobby(room.Id));

            Assert.AreEqual(RoomPhase.Lobby, room.Phase);
            registry.TryGetPlayer(1, out var first);
            registry.TryGetPlayer(2, out var second);
            Assert.IsFalse(first.Ready);
            Assert.IsFalse(second.Ready);
            Assert.IsFalse(registry.ResetToLobby(room.Id), "Already in the lobby.");
        }

        [Test]
        public void Gomoku_DiagonalWinAndFullBoardDraw()
        {
            var ruleset = new GomokuRuleset(15, 5, true, true, true);
            var state = new GomokuState(15);
            var session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                Guid.NewGuid(), state, new GomokuRules(ruleset));
            session.AddParticipant(new MatchParticipant("alice", "Alice", 0, MatchParticipantRole.Player));
            session.AddParticipant(new MatchParticipant("bob", "Bob", 1, MatchParticipantRole.Player));
            Assert.IsTrue(session.Start());

            // Anti-diagonal (1,-1): 60=(4,0), 46=(3,1), 32=(2,2), 18=(1,3), 4=(0,4)
            var moves = new[] { 60, 1, 46, 2, 32, 3, 18, 5, 4 };
            for (var i = 0; i < moves.Length; i++)
                Assert.IsTrue(session.Submit(i % 2 == 0 ? "alice" : "bob", new PlaceStoneCommand(moves[i]), i).Accepted);

            Assert.AreEqual(GomokuResult.BlackWin, state.Result);
            Assert.AreEqual(4, state.LastCellIndex);

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

        [Test]
        public void ReconnectSeats_KeepRoomAliveAndRestoreMembership()
        {
            var registry = new RoomRegistry();
            Assert.IsTrue(registry.TryAddPlayer(1, "Alice", out _));
            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out var room, out _, out _));

            // Drop inside the window: the seat keeps the now-empty room alive.
            registry.RecordPendingSeat("Alice", room.Id, false, 500d);
            Assert.IsTrue(registry.RemovePlayer(1, 150d, out var previous));
            Assert.AreEqual(room.Id, previous);
            Assert.IsTrue(registry.TryGetRoom(room.Id, out _));
            registry.TouchRoom(room.Id, 100d);
            var recycled = new System.Collections.Generic.List<RoomRegistry.Room>();
            Assert.AreEqual(0, registry.CollectIdleRooms(400d, 120d, recycled));

            // Rejoin under a fresh connection id before expiry: same room, leader seat.
            Assert.IsTrue(registry.TryAddPlayer(9, "Alice", out _));
            Assert.IsTrue(registry.TryConsumePendingSeat("Alice", 450d, out var seat));
            Assert.IsTrue(registry.TryRejoinRoom(9, seat.RoomId, seat.IsSpectator, out var rejoined, out _));
            Assert.IsTrue(registry.TryGetPlayer(9, out var alice));
            Assert.AreEqual(room.Id, alice.RoomId);
            Assert.IsTrue(alice.IsLeader);
            Assert.AreEqual(1, rejoined.MemberCount);
            Assert.IsFalse(registry.TryConsumePendingSeat("Alice", 450d, out _));

            // Once the window lapses, the prune sweep releases the room for collection.
            registry.RecordPendingSeat("Alice", room.Id, false, 600d);
            registry.RemovePlayer(9, 590d, out _);
            var expired = new System.Collections.Generic.List<Guid>();
            Assert.AreEqual(0, registry.PrunePendingSeats(590d, expired));
            Assert.AreEqual(1, registry.PrunePendingSeats(610d, expired));
            Assert.IsTrue(registry.TryRemoveRoomIfEmpty(room.Id));
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
        }

        [Test]
        public void RoomGuards_CoverJoinLimitsCancelAndRollback()
        {
            var registry = new RoomRegistry();
            Assert.IsTrue(registry.TryAddPlayer(1, "Alice", out _));
            Assert.IsTrue(registry.TryAddPlayer(2, "Bob", out _));
            Assert.IsTrue(registry.TryAddPlayer(3, "Carol", out _));
            Assert.IsFalse(registry.TryJoinRoom(99, Guid.NewGuid(), false, out _, out _, out var unknown));
            Assert.AreEqual(MultiplayerErrorCode.NotRegistered, unknown);

            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out var room, out _, out _));
            Assert.IsTrue(registry.TryJoinRoom(2, room.Id, false, out _, out _, out _));
            Assert.IsFalse(registry.TryJoinRoom(3, room.Id, false, out _, out _, out var full));
            Assert.AreEqual(MultiplayerErrorCode.RoomFull, full);
            Assert.IsFalse(registry.TryJoinRoomAsSpectator(3, room.Id, 1, out _, out _, out var notStarted));
            Assert.AreEqual(MultiplayerErrorCode.RoomNotStarted, notStarted);

            Assert.IsTrue(registry.TrySetReady(1, true, out _, out _, out _));
            Assert.IsTrue(registry.TrySetReady(2, true, out _, out _, out _));
            Assert.IsTrue(registry.TryStartRoom(1, 1, out _, out _, out _));
            Assert.IsFalse(registry.TryReturnToLobby(2, out _, out _, out var notLeader));
            Assert.AreEqual(MultiplayerErrorCode.NotLeader, notLeader);
            Assert.IsTrue(registry.TryReturnToLobby(1, out _, out _, out _));
            Assert.IsFalse(registry.RollbackStart(room.Id)); // lobby again after return
            Assert.IsTrue(registry.TrySetReady(1, true, out _, out _, out _));
            Assert.IsTrue(registry.TrySetReady(2, true, out _, out _, out _));
            Assert.IsTrue(registry.TryStartRoom(1, 1, out _, out _, out _));
            Assert.IsTrue(registry.RollbackStart(room.Id));

            Assert.IsFalse(registry.TryCancelRoom(3, out _, out _, out _, out var noRoom));
            Assert.AreEqual(MultiplayerErrorCode.NotInRoom, noRoom);
            Assert.IsTrue(registry.TryCancelRoom(1, out _, out var affected, out _, out _));
            Assert.AreEqual(2, affected.Length);
        }
    }
}
