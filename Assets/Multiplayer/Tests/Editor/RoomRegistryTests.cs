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

            Assert.IsTrue(registry.TryCreateRoom(1, "Room A", 2, out var room, out _));
            Assert.IsTrue(registry.TryJoinRoom(2, room.Id, false, out _, out _));

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
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _);

            Assert.IsFalse(registry.TryStartRoom(1, 2, out _, out _));
            registry.TrySetReady(1, true, out _, out _);
            registry.TrySetReady(2, true, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out room, out _));
            Assert.AreEqual(RoomPhase.InGame, room.Phase);
        }

        [Test]
        public void LeavingLeaderTransfersLeadershipAndRemovesEmptyRoom()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryCreateRoom(1, "Room A", 2, out var room, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _);

            Assert.IsTrue(registry.TryLeaveRoom(1, out _, out var roomRemoved, out _));
            Assert.IsFalse(roomRemoved);
            Assert.IsTrue(registry.TryGetPlayer(2, out var nextLeader));
            Assert.IsTrue(nextLeader.IsLeader);

            Assert.IsTrue(registry.TryLeaveRoom(2, out var removedRoomId, out roomRemoved, out _));
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
            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out var first, out _));
            Assert.IsTrue(registry.TryCreateRoom(2, "B", 2, out var second, out _));
            Assert.AreNotEqual(Guid.Empty, first.Id);
            Assert.AreNotEqual(first.Id, second.Id);
        }

        [Test]
        public void RoomCapacityDoesNotPreventOtherRooms()
        {
            var registry = new RoomRegistry(2);
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);

            Assert.IsTrue(registry.TryCreateRoom(1, "A", 1, out var first, out _));
            Assert.IsTrue(registry.TryCreateRoom(2, "B", 1, out var second, out _));
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

            Assert.IsTrue(registry.TryCreateRoom(1, "A", 2, out _, out _));
            Assert.IsFalse(registry.TryCreateRoom(2, "B", 2, out _, out var error));
            Assert.AreEqual("Room limit reached.", error);
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
        public void SpectatorsDoNotConsumePlayerSeats()
        {
            var registry = new RoomRegistry();
            registry.TryAddPlayer(1, "Alice", out _);
            registry.TryAddPlayer(2, "Bob", out _);
            registry.TryAddPlayer(3, "Viewer", out _);
            registry.TryCreateRoom(1, "Room", 2, out var room, out _);
            registry.TryJoinRoom(2, room.Id, false, out _, out _);
            registry.TrySetReady(1, true, out _, out _);
            registry.TrySetReady(2, true, out _, out _);
            Assert.IsTrue(registry.TryStartRoom(1, 2, out _, out _));

            Assert.IsTrue(registry.TryJoinRoomAsSpectator(3, room.Id, 1, out room, out _));
            Assert.AreEqual(2, room.PlayerCount);
            Assert.AreEqual(1, room.SpectatorCount);
            Assert.AreEqual(3, room.MemberCount);
            Assert.IsFalse(registry.TryJoinRoomAsSpectator(1, room.Id, 1, out _, out _));
        }
    }
}
#endif
