using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

namespace Socket.Multiplayer
{
    /// <summary>SocketRoomManager 分部类：房间指令、对局接线与拒绝回执。</summary>
    public partial class SocketRoomManager
    {
        [Server]
        public void TryStartGame(NetworkConnectionToClient requester)
        {
            if (_registry == null || requester == null) return;
            var minimum = GetRequiredMatchPlayers();
            if (!_registry.TryStartRoom(requester.connectionId, minimum, out var room, out var error, out var errorCode))
            {
                SendError(requester, error, errorCode);
                return;
            }
            if (_roomMatches.TryGetValue(room.Id, out var match) && match != null)
            {
                // Rematch: reset the finished board and re-seat the current members so
                // the same two players can start again without leaving the room.
                if (match.Phase == MatchPhase.Completed || match.Phase == MatchPhase.Cancelled)
                {
                    match.ServerResetMatch();
                    RebindMatchParticipants(room);
                }
                if (!match.ServerStartMatch())
                {
                    // Do not leave the room stuck InGame without a running match.
                    _registry.RollbackStart(room.Id);
                    SendError(requester, "The match could not be started.", MultiplayerErrorCode.StartFailed);
                    return;
                }
            }
            SpawnRoomInteractables(room);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.Started, room.Id);
        }

        [Server]
        public void ServerReturnToLobby(NetworkConnectionToClient requester)
        {
            if (_registry == null || requester == null) return;
            if (!_registry.TryReturnToLobby(requester.connectionId, out var room, out var error, out var errorCode))
            {
                SendError(requester, error, errorCode);
                return;
            }
            DestroyRoomInteractables(room.Id);
            if (_roomMatches.TryGetValue(room.Id, out var match) && match != null)
                match.ServerResetMatch();
            RebindMatchParticipants(room);
            SyncPlayers(room);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.ReturnedToLobby, room.Id);
        }

        [Server]
        private void OnServerRoomMessage(NetworkConnectionToClient conn, ServerRoomMessage message)
        {
            if (_registry == null || conn == null) return;
            if (!ServerTryConsumeRate(conn.connectionId, RateLimitKind.RoomOperation))
            {
                SendRateLimited(conn);
                return;
            }
            if (!_registry.TryGetPlayer(conn.connectionId, out var requesterState))
            {
                SendError(conn, "Player is not ready.", MultiplayerErrorCode.NotRegistered);
                return;
            }
            // Every accepted room request counts as activity for idle recycling (M3 P2.15).
            if (requesterState.RoomId != LobbyRoom.Id)
                _registry.TouchRoom(requesterState.RoomId, NetworkTime.localTime);

            switch (message.serverRoomOperation)
            {
                case ServerRoomOperation.Create:
                    CreateRoom(conn, message.roomName);
                    break;
                case ServerRoomOperation.Cancel:
                    CancelRoom(conn);
                    break;
                case ServerRoomOperation.Join:
                    JoinRoom(conn, message.roomId);
                    break;
                case ServerRoomOperation.JoinAsSpectator:
                    JoinRoomAsSpectator(conn, message.roomId);
                    break;
                case ServerRoomOperation.Leave:
                    LeaveRoom(conn);
                    break;
                case ServerRoomOperation.Ready:
                    SetReady(conn, message.ready);
                    break;
                case ServerRoomOperation.Start:
                    TryStartGame(conn);
                    break;
                case ServerRoomOperation.ReturnToLobby:
                    ServerReturnToLobby(conn);
                    break;
                default:
                    SendError(conn, "Unknown room operation.", MultiplayerErrorCode.UnknownOperation);
                    break;
            }
        }

        [Server]
        private void CreateRoom(NetworkConnectionToClient conn, string requestedName)
        {
            var name = SanitizeRoomName(requestedName);
            if (!_registry.TryCreateRoom(conn.connectionId, name, config == null ? maxConnections : config.EffectiveMaxPlayers, out var room, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }

            _registry.TouchRoom(room.Id, NetworkTime.localTime);
            SyncPlayers(room);
            PlacePlayerInRoom(conn, room);
            EnsureRoomObjects(room);
            AddPlayerToMatch(room, conn);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.Created, room.Id);
        }

        [Server]
        private void JoinRoom(NetworkConnectionToClient conn, Guid roomId)
        {
            if (_registry.TryGetRoom(roomId, out var existingRoom) && existingRoom.Phase != RoomPhase.Lobby && HasRoomMatch())
            {
                JoinRoomAsSpectator(conn, roomId);
                return;
            }

            var allowLateJoiners = config != null && config.allowLateJoiners;
            if (!_registry.TryJoinRoom(conn.connectionId, roomId, allowLateJoiners, out var room, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }

            SyncPlayers(room);
            PlacePlayerInRoom(conn, room);
            EnsureRoomObjects(room);
            AddPlayerToMatch(room, conn);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.Joined, room.Id);
        }

        [Server]
        private void LeaveRoom(NetworkConnectionToClient conn)
        {
            if (!_registry.TryLeaveRoom(conn.connectionId, out var roomId, out var roomRemoved, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }

            if (roomRemoved) DestroyRoomObjects(roomId);
            else
            {
                HandleParticipantLeft(roomId, conn, MatchForfeitCause.Leave);
                SyncPlayers(roomId);
                RefreshRoom(roomId);
            }
            SyncPlayer(conn);
            PlacePlayerInLobby(conn);
            BroadcastClientState(ClientRoomOperation.Departed, roomId);
        }

        [Server]
        private void JoinRoomAsSpectator(NetworkConnectionToClient conn, Guid roomId)
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            var maxSpectators = template == null ? config == null ? 20 : config.maxSpectators : template.maxSpectators;
            var rules = template == null ? null : template.rules;
            if (rules != null && !rules.AllowsSpectators)
            {
                SendError(conn, "This room does not allow spectators.", MultiplayerErrorCode.SpectatorNotAllowed);
                return;
            }
            if (!_registry.TryJoinRoomAsSpectator(conn.connectionId, roomId, maxSpectators, out var room, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }

            SyncPlayer(conn);
            EnsureRoomObjects(room);
            AddSpectatorToMatch(room, conn);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.Joined, room.Id);
        }

        [Server]
        private void CancelRoom(NetworkConnectionToClient conn)
        {
            if (!_registry.TryCancelRoom(conn.connectionId, out var roomId, out var affectedPlayers, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }

            DestroyRoomObjects(roomId);
            foreach (var connectionId in affectedPlayers)
                if (NetworkServer.connections.TryGetValue(connectionId, out var affectedConnection))
                {
                    RemovePlayerFromMatch(roomId, affectedConnection);
                    SyncPlayer(affectedConnection);
                    PlacePlayerInLobby(affectedConnection);
                }
            BroadcastClientState(ClientRoomOperation.Cancelled, roomId);
        }

        [Server]
        private void SetReady(NetworkConnectionToClient conn, bool value)
        {
            if (!_registry.TrySetReady(conn.connectionId, value, out var room, out var error, out var errorCode))
            {
                SendError(conn, error, errorCode);
                return;
            }
            SyncPlayers(room);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.UpdateRoom, room.Id);

            if (config != null && config.autoStartWhenAllReady && room.PlayerIds.All(id => _registry.TryGetPlayer(id, out var player) && player.Ready))
            {
                var leaderId = room.PlayerIds.FirstOrDefault(id => _registry.TryGetPlayer(id, out var player) && player.IsLeader);
                if (NetworkServer.connections.TryGetValue(leaderId, out var leaderConnection))
                    TryStartGame(leaderConnection);
            }
        }

        [Server]
        private void SyncPlayers(RoomRegistry.Room room)
        {
            if (room == null) return;
            foreach (var connectionId in room.MemberIds)
                if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
                    SyncPlayer(conn);
        }

        [Server]
        private void SyncPlayers(Guid roomId)
        {
            if (_registry != null && _registry.TryGetRoom(roomId, out var room))
                SyncPlayers(room);
        }

        [Server]
        private void SyncPlayer(NetworkConnectionToClient conn)
        {
            if (conn == null || conn.identity == null || _registry == null) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            if (!_registry.TryGetPlayer(conn.connectionId, out var state)) return;
            player.ServerAssignRoom(state.RoomId, state.IsLeader, state.Ready, state.IsSpectator);
        }

        [Server]
        private void EnsureRoomObjects(RoomRegistry.Room room)
        {
            if (room == null) return;
            if (!_roomStates.ContainsKey(room.Id) && roomStatePrefab != null)
            {
                var stateObject = Instantiate(roomStatePrefab.gameObject);
                var state = stateObject.GetComponent<NetworkRoomState>();
                state.ServerAssignRoom(room.Id);
                NetworkServer.Spawn(stateObject);
                _roomStates.Add(room.Id, state);
            }

            if (!_roomChats.ContainsKey(room.Id) && roomChatPrefab != null)
            {
                var chatObject = Instantiate(roomChatPrefab.gameObject);
                var chat = chatObject.GetComponent<NetworkRoomChat>();
                chat.ServerAssignRoom(room.Id);
                NetworkServer.Spawn(chatObject);
                _roomChats.Add(room.Id, chat);
            }

            EnsureRoomMatch(room);
        }

        [Server]
        private void EnsureRoomMatch(RoomRegistry.Room room)
        {
            if (room == null || _roomMatches.ContainsKey(room.Id) || matchPrefab == null) return;
            var template = config == null ? null : config.defaultRoomTemplate;
            // The adapter decides how the template turns into rules; the manager only
            // owns the lifecycle, so a new game is a new adapter + prefab.
            var matchObject = Instantiate(matchPrefab.gameObject);
            var match = matchObject.GetComponent<NetworkMatchAdapter>();
            match.ServerInitialize(room.Id, template);
            NetworkServer.Spawn(matchObject);
            _roomMatches.Add(room.Id, match);
        }

        [Server]
        private void AddPlayerToMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            // 座位来自注册表的显式绑定（RoomRegistry.Player.Seat），不再用成员表下标推导：
            // 下标会随其他成员退出而平移，导致观战者突然“继承”座位而内核却仍是观战者。
            if (!_registry.TryGetPlayer(conn.connectionId, out var registered)) return;
            var seat = registered.Seat;
            // Past the game's seat count the member watches the duel: without this a 3rd
            // player would silently take seat 2, never get a turn and still look like a
            // player in every client's UI.
            if (seat < 0 || seat >= match.MaxSeats)
            {
                match.ServerAddSpectator(player.StableId, player.displayName);
                player.ServerSetSeat(-1);
                return;
            }

            // AddParticipant is false when the player is already seated, but the seat still
            // has to be re-applied: it is a SyncVar that must survive re-joins and resets.
            match.ServerAddPlayer(player.StableId, player.displayName, seat);
            player.ServerSetSeat(seat);
        }

        [Server]
        private void AddSpectatorToMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            if (match.ServerAddSpectator(player.StableId, player.displayName))
                player.ServerSetSeat(-1);
        }

        [Server]
        private void RebindMatchParticipants(RoomRegistry.Room room)
        {
            if (room == null) return;
            foreach (var connectionId in room.PlayerIds)
                if (NetworkServer.connections.TryGetValue(connectionId, out var connection))
                    AddPlayerToMatch(room, connection);
            foreach (var connectionId in room.SpectatorIds)
                if (NetworkServer.connections.TryGetValue(connectionId, out var connection))
                    AddSpectatorToMatch(room, connection);
        }

        [Server]
        private void RemovePlayerFromMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerDropParticipant(player.StableId);
        }

        [Server]
        private void RemovePlayerFromMatch(Guid roomId, NetworkConnectionToClient conn)
        {
            if (_registry != null && _registry.TryGetRoom(roomId, out var room))
                RemovePlayerFromMatch(room, conn);
        }

        /// <summary>
        /// A member left a room (leave or disconnect): hand the framework cause to the
        /// match adapter, which forfeits an active match instead of deadlocking on the
        /// leaver's turn and keeps the survivor seated.
        /// </summary>
        [Server]
        private void HandleParticipantLeft(RoomRegistry.Room room, NetworkConnectionToClient conn, MatchForfeitCause cause)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerHandleParticipantLeft(player.StableId, cause);
        }

        [Server]
        private void HandleParticipantLeft(Guid roomId, NetworkConnectionToClient conn, MatchForfeitCause cause)
        {
            if (_registry != null && _registry.TryGetRoom(roomId, out var room))
                HandleParticipantLeft(room, conn, cause);
        }
    }
}
