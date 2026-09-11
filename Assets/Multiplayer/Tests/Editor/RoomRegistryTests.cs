#if UNITY_EDITOR
using NUnit.Framework;
using System;
using UnityEngine;

namespace Socket.Multiplayer.Tests
{
    public sealed class RoomRegistryTests
    {
        [Test]
        public void CreateAndJoinAssignsPlayersToOneRoom()
        {
            var registry = new RoomRegistry();
            Assert.IsTrue(registry.TryAddPlayer(1, "Alice", out _));
            Assert.IsTrue(registry.TryAddPlayer(2, "Bob", out _));

            Assert.IsTrue(registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _));
            Assert.IsTrue(registry.TryJoinRoom(2, room.Id, false, out _, out _, out _));

            Assert.AreEqual(2, room.PlayerCount);
            Assert.IsTrue(registry.TryGetPlayer(1, out var leader));
            Assert.IsTrue(registry.TryGetPlayer(2, out var member));
            Assert.AreEqual(room.Id, leader.RoomId);
            Assert.AreEqual(room.Id, member.RoomId);
            Assert.IsTrue(leader.IsLeader);
            Assert.IsFalse(member.IsLeader);
        }

        [Test]
        public void StartRequiresAllPlayersReady()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);

            Assert.IsFalse(registry.TryStartRoom(1, 2, out _, out _, out var notReadyCode));
            Assert.AreEqual(MultiplayerErrorCode.NotAllReady, notReadyCode);
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out room, out _, out _));
            Assert.AreEqual(RoomPhase.InGame, room.Phase);
        }

        [Test]
        public void ResetToLobbyClearsReadyAndUnsticksRoom()
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
        public void LeavingLeaderTransfersLeadershipAndRemovesEmptyRoom()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);

            Assert.IsTrue(registry.TryLeaveRoom(1, out _, out var roomRemoved, out _, out _));
            Assert.IsFalse(roomRemoved);
            Assert.IsTrue(registry.TryGetPlayer(2, out var nextLeader));
            Assert.IsTrue(nextLeader.IsLeader);

            Assert.IsTrue(registry.TryLeaveRoom(2, out var removedRoomId, out roomRemoved, out _, out _));
            Assert.IsTrue(roomRemoved);
            Assert.AreEqual(room.Id, removedRoomId);
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
        }

        [Test]
        public void DifferentRoomsCanExistAtTheSameTime()
        {
            var registry = new RoomRegistry(2);
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out var first, out _, out _));
            Assert.IsTrue(registry.TryCreateRoom(2, "B", 2, out var second, out _, out _));
            Assert.AreNotEqual(Guid.Empty, first.Id);
            Assert.AreNotEqual(first.Id, second.Id);
        }

        [Test]
        public void RoomCapacityDoesNotPreventOtherRooms()
        {
            var registry = new RoomRegistry(2);
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);

            Assert.IsTrue(registry.TryCreateRoom(1, "A", 1, out var first, out _, out _));
            Assert.IsTrue(registry.TryCreateRoom(2, "B", 1, out var second, out _, out _));
            Assert.AreEqual(1, first.MaxPlayers);
            Assert.AreEqual(1, second.MaxPlayers);
            Assert.AreEqual(1, first.PlayerCount);
            Assert.AreEqual(1, second.PlayerCount);
        }

        [Test]
        public void RoomLimitRejectsAdditionalRooms()
        {
            var registry = new RoomRegistry(1);
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);

            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out _, out _, out _));
            Assert.IsFalse(registry.TryCreateRoom(2, "B", 2, out _, out var error, out var errorCode));
            Assert.AreEqual("Room limit reached.", error);
            Assert.AreEqual(MultiplayerErrorCode.RoomLimitReached, errorCode);
        }

        [Test]
        public void ServerConnectionLimitNeverFallsBelowRoomCapacity()
        {
            var config = ScriptableObject.CreateInstance<MultiplayerConfig>();
            config.maxPlayers = 8;
            config.maxServerPlayers = 2;

            Assert.AreEqual(8, config.ServerConnectionLimit);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void RoomTemplateOverridesLegacyCapacity()
        {
            var config = ScriptableObject.CreateInstance<MultiplayerConfig>();
            var template = ScriptableObject.CreateInstance<RoomTemplate>();
            config.minPlayers = 1;
            config.maxPlayers = 4;
            config.defaultRoomTemplate = template;
            template.minPlayers = 3;
            template.maxPlayers = 12;

            Assert.AreEqual(3, config.EffectiveMinPlayers);
            Assert.AreEqual(12, config.EffectiveMaxPlayers);

            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void RoomTemplateExtendsSpawnLayoutWhenPlayerCountExceedsList()
        {
            var template = ScriptableObject.CreateInstance<RoomTemplate>();
            template.playerSpawns = new[]
            {
                new RoomPlayerSpawn { position = new Vector3(10f, 0f, 5f) }
            };

            var pose = template.GetPlayerSpawnPose(5, 2f);

            Assert.AreEqual(new Vector3(12f, 0f, 3f), pose.position);
            UnityEngine.Object.DestroyImmediate(template);
        }

        [Test]
        public void InputAdmissionRejectsOldAndBurstFrames()
        {
            var admission = new InputAdmission(20f);

            Assert.IsTrue(admission.TryAccept(1, 0d));
            Assert.IsFalse(admission.TryAccept(2, 0.01d));
            Assert.IsFalse(admission.TryAccept(1, 0.1d));
            Assert.IsTrue(admission.TryAccept(2, 0.03d));
        }

        [Test]
        public void ProtocolSignatureChangesWhenInputRuleChanges()
        {
            var config = ScriptableObject.CreateInstance<MultiplayerConfig>();
            var first = MultiplayerProtocol.GetConfigSignature(config);
            config.inputSendRate = 10f;
            var second = MultiplayerProtocol.GetConfigSignature(config);

            Assert.AreNotEqual(first, second);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void ProtocolSignatureCoversCapacityAndVersionIsStable()
        {
            var config = ScriptableObject.CreateInstance<MultiplayerConfig>();
            var baseline = MultiplayerProtocol.GetConfigSignature(config);
            config.maxRooms += 1;
            Assert.AreNotEqual(baseline, MultiplayerProtocol.GetConfigSignature(config));
            config.maxRooms -= 1;
            Assert.AreEqual(baseline, MultiplayerProtocol.GetConfigSignature(config));
            Assert.AreEqual(5, MultiplayerProtocol.Version);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void InputAdmissionRejectsClockRollback()
        {
            var admission = new InputAdmission(20f); // minimum interval 25ms
            Assert.IsTrue(admission.TryAccept(1, 10d));
            Assert.IsFalse(admission.TryAccept(2, 9.9d));  // clock went backwards
            Assert.IsTrue(admission.TryAccept(3, 10.06d)); // forward again once interval passes
        }

        [Test]
        public void SpectatorsDoNotConsumePlayerSeats()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryAddPlayer(3, "Viewer", out _);
            registry.TryCreateRoom(1, "Room", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out _, out _, out _));

            Assert.IsTrue(registry.TryJoinRoomAsSpectator(3, room.Id, 1, out room, out _, out _));
            Assert.AreEqual(2, room.PlayerCount);
            Assert.AreEqual(1, room.SpectatorCount);
            Assert.AreEqual(3, room.MemberCount);
            Assert.IsFalse(registry.TryJoinRoomAsSpectator(1, room.Id, 1, out _, out _, out var code));
            Assert.AreEqual(MultiplayerErrorCode.AlreadyInRoom, code);
        }

        [Test]
        public void IdleRoomsAreRecycledAndMembersReturnToLobby()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);
            registry.TouchRoom(room.Id, 100d);

            var recycled = new System.Collections.Generic.List<RoomRegistry.Room>();
            Assert.AreEqual(0, registry.CollectIdleRooms(150d, 120d, recycled));
            Assert.IsTrue(registry.TryGetRoom(room.Id, out _));

            registry.TouchRoom(room.Id, 200d);
            Assert.AreEqual(1, registry.CollectIdleRooms(320d, 120d, recycled));
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
            Assert.IsTrue(registry.TryGetPlayer(1, out var alice));
            Assert.AreEqual(LobbyRoom.Id, alice.RoomId);

            // Untracked rooms (LastActivityTime == 0) are never recycled by the sweep.
            registry.TryCreateRoom(1, "Room B", 2, out var fresh, out _, out _);
            Assert.AreEqual(0, registry.CollectIdleRooms(999999d, 120d, recycled));
            Assert.IsTrue(registry.TryGetRoom(fresh.Id, out _));
        }

        [Test]
        public void PendingSeatKeepsEmptyRoomAliveUntilExpiry()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);

            // Disconnect inside the reconnect window: the seat is recorded before removal.
            registry.RecordPendingSeat("Alice", room.Id, false, 500d);
            Assert.IsTrue(registry.RemovePlayer(1, 150d, out var previous));
            Assert.AreEqual(room.Id, previous);
            Assert.IsTrue(registry.TryGetRoom(room.Id, out var kept));
            Assert.AreEqual(0, kept.MemberCount);
            Assert.IsTrue(registry.HasLivePendingSeat(room.Id, 150d));

            // While the seat lives the sweep must skip the room entirely.
            var recycled = new System.Collections.Generic.List<RoomRegistry.Room>();
            registry.TouchRoom(room.Id, 100d);
            Assert.AreEqual(0, registry.CollectIdleRooms(400d, 120d, recycled));

            // After expiry the seat is pruned and the kept-warm room is collected.
            var expired = new System.Collections.Generic.List<Guid>();
            Assert.AreEqual(0, registry.PrunePendingSeats(400d, expired));
            Assert.AreEqual(1, registry.PrunePendingSeats(510d, expired));
            Assert.AreEqual(room.Id, expired[0]);
            Assert.IsTrue(registry.TryRemoveRoomIfEmpty(expired[0]));
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
        }

        [Test]
        public void RejoinWithinWindowRestoresMembershipAndLeadership()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _, out _);

            registry.RecordPendingSeat("Alice", room.Id, false, 500d);
            registry.RemovePlayer(1, 400d, out _);

            // The owner returns under a fresh connection id before the seat expires.
            Assert.IsTrue(registry.TryAddPlayer(9, "Alice", out _));
            Assert.IsTrue(registry.TryConsumePendingSeat("Alice", 450d, out var seat));
            Assert.AreEqual(room.Id, seat.RoomId);
            Assert.IsFalse(seat.IsSpectator);
            Assert.IsTrue(registry.TryRejoinRoom(9, seat.RoomId, seat.IsSpectator, out var rejoined, out _));
            Assert.IsTrue(registry.TryGetPlayer(9, out var alice));
            Assert.AreEqual(room.Id, alice.RoomId);
            Assert.IsTrue(alice.IsLeader); // the empty room had no leader left behind
            Assert.AreEqual(1, rejoined.MemberCount);

            // A seat can only be consumed once, and expired seats are not consumable.
            Assert.IsFalse(registry.TryConsumePendingSeat("Alice", 450d, out _));
            registry.RecordPendingSeat("Alice", room.Id, false, 460d);
            Assert.IsFalse(registry.TryConsumePendingSeat("Alice", 470d, out _));
        }

        [Test]
        public void JoinGuards_CoverFullStartedAndUnregistered()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryAddPlayer(3, "Carol", out _);

            Assert.IsFalse(registry.TryJoinRoom(99, Guid.NewGuid(), false, out _, out _, out var unknown));
            Assert.AreEqual(MultiplayerErrorCode.NotRegistered, unknown);

            registry.TryCreateRoom(1, "Room", 2, out var room, out _, out _);
            Assert.IsTrue(registry.TryJoinRoom(2, room.Id, false, out _, out _, out _));
            Assert.IsFalse(registry.TryJoinRoom(3, room.Id, false, out _, out _, out var full));
            Assert.AreEqual(MultiplayerErrorCode.RoomFull, full);

            // After start, joins are gated by the allowLateJoiners flag.
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out _, out _, out _));
            Assert.IsFalse(registry.TryJoinRoom(3, room.Id, false, out _, out _, out var started));
            Assert.AreEqual(MultiplayerErrorCode.RoomStarted, started);

            // A late joiner fits once a seat frees up.
            Assert.IsTrue(registry.TryLeaveRoom(2, out _, out _, out _, out _));
            Assert.IsTrue(registry.TryJoinRoom(3, room.Id, true, out _, out _, out _));
        }

        [Test]
        public void SpectatorGuards_RequireStartedRoomAndRespectLimit()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryAddPlayer(3, "V1", out _);
            registry.TryAddPlayer(4, "V2", out _);
            registry.TryCreateRoom(1, "Room", 1, out var room, out _, out _);

            Assert.IsFalse(registry.TryJoinRoomAsSpectator(3, room.Id, 1, out _, out _, out var notStarted));
            Assert.AreEqual(MultiplayerErrorCode.RoomNotStarted, notStarted);

            registry.TrySetReady(1, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 1, out _, out _, out _));
            Assert.IsTrue(registry.TryJoinRoomAsSpectator(3, room.Id, 1, out room, out _, out _));
            Assert.IsFalse(registry.TryJoinRoomAsSpectator(4, room.Id, 1, out _, out _, out var capped));
            Assert.AreEqual(MultiplayerErrorCode.SpectatorLimitReached, capped);
            Assert.IsFalse(registry.TryToggleReady(3, out _, out _, out var canReady));
            Assert.AreEqual(MultiplayerErrorCode.SpectatorCannotReady, canReady);
        }

        [Test]
        public void CancelRoomReturnsEveryoneToLobbyAndOnlyLeaderMayCancel()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room", 1, out var room, out _, out _);
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TryStartRoom(1, 1, out _, out _, out _);
            registry.TryJoinRoomAsSpectator(2, room.Id, 4, out _, out _, out _);

            Assert.IsFalse(registry.TryCancelRoom(2, out _, out _, out _, out var notLeader));
            Assert.AreEqual(MultiplayerErrorCode.NotLeader, notLeader);

            Assert.IsTrue(registry.TryCancelRoom(1, out var cancelledId, out var affected, out _, out _));
            Assert.AreEqual(room.Id, cancelledId);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, affected);
            Assert.IsFalse(registry.TryGetRoom(room.Id, out _));
            Assert.IsTrue(registry.TryGetPlayer(1, out var alice));
            Assert.IsTrue(registry.TryGetPlayer(2, out var bob));
            Assert.AreEqual(LobbyRoom.Id, alice.RoomId);
            Assert.AreEqual(LobbyRoom.Id, bob.RoomId);

            Assert.IsFalse(registry.TryCancelRoom(1, out _, out _, out _, out var notInRoom));
            Assert.AreEqual(MultiplayerErrorCode.NotInRoom, notInRoom);
        }

        [Test]
        public void ReturnToLobbyResetsReadyAndRequiresLeader()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room", 2, out var room, out _, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _, out _);
            registry.TrySetReady(1, true, out _, out _, out _);
            registry.TrySetReady(2, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out _, out _, out _));

            Assert.IsFalse(registry.TryReturnToLobby(2, out _, out _, out var notLeader));
            Assert.AreEqual(MultiplayerErrorCode.NotLeader, notLeader);

            Assert.IsTrue(registry.TryReturnToLobby(1, out var returned, out _, out _));
            Assert.AreEqual(RoomPhase.Lobby, returned.Phase);
            Assert.IsTrue(registry.TryGetPlayer(1, out var alice));
            Assert.IsTrue(registry.TryGetPlayer(2, out var bob));
            Assert.IsFalse(alice.Ready);
            Assert.IsFalse(bob.Ready);

            Assert.IsFalse(registry.TryReturnToLobby(1, out _, out _, out var alreadyLobby));
            Assert.AreEqual(MultiplayerErrorCode.AlreadyInLobby, alreadyLobby);
        }

        [Test]
        public void RollbackStartRevertsInGameRoomsOnly()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryCreateRoom(1, "Room", 1, out var room, out _, out _);

            Assert.IsFalse(registry.RollbackStart(room.Id)); // still in lobby, nothing to revert
            registry.TrySetReady(1, true, out _, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 1, out _, out _, out _));
            Assert.IsTrue(registry.RollbackStart(room.Id));

            Assert.IsTrue(registry.TryGetRoom(room.Id, out var reverted));
            Assert.AreEqual(RoomPhase.Lobby, reverted.Phase);
            Assert.IsTrue(registry.TryGetPlayer(1, out var alice));
            Assert.IsFalse(alice.Ready);
            Assert.IsFalse(registry.RollbackStart(Guid.NewGuid())); // unknown room
        }
    }
}
#endif
