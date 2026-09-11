using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

namespace Socket.Multiplayer
{
    /// <summary>SocketRoomManager 分部类：房间对象与玩家落位：摆件生成/销毁、出生点放置、房间视图刷新。</summary>
    public partial class SocketRoomManager
    {
        [Server]
        private void SpawnRoomInteractables(RoomRegistry.Room room)
        {
            if (room == null || _roomInteractables.ContainsKey(room.Id)) return;

            var templateSpawns = config != null && config.defaultRoomTemplate != null
                ? config.defaultRoomTemplate.interactables
                : null;
            if ((templateSpawns == null || templateSpawns.Length == 0) && interactablePrefab == null)
            {
                Debug.LogWarning("SocketRoomManager has no interactablePrefab; room started without interactables.", this);
                return;
            }

            var instances = new List<NetworkInteractable>();
            if (templateSpawns != null && templateSpawns.Length > 0)
            {
                foreach (var spawn in templateSpawns)
                {
                    if (spawn == null) continue;
                    var prefab = spawn.prefab == null ? interactablePrefab : spawn.prefab;
                    SpawnRoomInteractable(room.Id, prefab, spawn.position, Quaternion.Euler(spawn.eulerAngles), instances);
                }
            }
            else
                for (var i = 0; i < 3; i++)
                    SpawnRoomInteractable(room.Id, interactablePrefab, new Vector3(i * 2f - 2f, 0.4f, 2f), Quaternion.identity, instances);

            _roomInteractables.Add(room.Id, instances);
        }

        private static void SpawnRoomInteractable(
            Guid roomId,
            NetworkInteractable prefab,
            Vector3 position,
            Quaternion rotation,
            ICollection<NetworkInteractable> instances)
        {
            if (prefab == null) return;
            var instanceObject = Instantiate(prefab.gameObject, position, rotation);
            var networkMatch = instanceObject.GetComponent<NetworkMatch>();
            if (networkMatch != null) networkMatch.matchId = roomId;
            var instance = instanceObject.GetComponent<NetworkInteractable>();
            if (instance != null) instances.Add(instance);
            NetworkServer.Spawn(instanceObject);
        }

        [Server]
        private void PlacePlayerInRoom(NetworkConnectionToClient conn, RoomRegistry.Room room)
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            if (template == null || conn == null || room == null || conn.identity == null) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            var playerIndex = room.PlayerIds.IndexOf(conn.connectionId);
            player.ServerTeleport(template.GetPlayerSpawnPose(playerIndex, config.spawnSpacing));
        }

        [Server]
        private void PlacePlayerInLobby(NetworkConnectionToClient conn)
        {
            if (conn == null || conn.identity == null) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            var start = GetStartPosition();
            var pose = start == null
                ? new Pose(Vector3.zero, Quaternion.identity)
                : new Pose(start.position, start.rotation);
            player.ServerTeleport(pose);
        }

        private void RegisterTemplatePrefabs()
        {
            var spawns = config == null || config.defaultRoomTemplate == null
                ? null
                : config.defaultRoomTemplate.interactables;
            if (spawns == null) return;

            foreach (var spawn in spawns)
            {
                if (spawn == null || spawn.prefab == null) continue;
                var prefabObject = spawn.prefab.gameObject;
                if (!spawnPrefabs.Contains(prefabObject)) spawnPrefabs.Add(prefabObject);
            }
        }

        [Server]
        private void DestroyRoomInteractables(Guid roomId)
        {
            if (!_roomInteractables.TryGetValue(roomId, out var instances)) return;
            foreach (var interactable in instances)
                if (interactable != null) NetworkServer.Destroy(interactable.gameObject);
            _roomInteractables.Remove(roomId);
        }

        [Server]
        private void DestroyRoomObjects(Guid roomId)
        {
            DestroyRoomInteractables(roomId);
            if (_roomStates.TryGetValue(roomId, out var state) && state != null)
                NetworkServer.Destroy(state.gameObject);
            if (_roomChats.TryGetValue(roomId, out var chat) && chat != null)
                NetworkServer.Destroy(chat.gameObject);
            _roomStates.Remove(roomId);
            _roomChats.Remove(roomId);
            if (_roomMatches.TryGetValue(roomId, out var match) && match != null)
                NetworkServer.Destroy(match.gameObject);
            _roomMatches.Remove(roomId);
        }

        [Server]
        private void RefreshRoom(Guid roomId)
        {
            if (_registry == null || !_registry.TryGetRoom(roomId, out var room)) return;
            EnsureRoomObjects(room);
            if (_roomStates.TryGetValue(roomId, out var state) && state != null)
                state.ServerRefresh(room);
        }

        [Server]
        private void RefreshAllRoomObjects()
        {
            if (_registry == null) return;
            foreach (var room in _registry.Rooms.ToArray())
                RefreshRoom(room.Id);
        }
    }
}
