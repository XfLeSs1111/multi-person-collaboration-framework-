using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

namespace Socket.Multiplayer
{
    /// <summary>SocketRoomManager 分部类：服务端每秒扫描：闲置房回收、回合超时、对局收尾、重连台账清理、成员对账。</summary>
    public partial class SocketRoomManager
    {
        // Idle-room sweep (M3 P2.15): runs only on the server, throttled to ~1s.
        // Zero-member rooms are removed immediately elsewhere; this handles rooms that
        // still have members but no tracked activity for config.roomIdleTimeout seconds.
        private void Update()
        {
            if (_registry == null || !NetworkServer.active) return;
            if (Time.unscaledTime < _nextIdleSweep) return;
            _nextIdleSweep = Time.unscaledTime + 1f;
            RecycleIdleRooms();
            PruneReconnectState();
            SweepMatchTimeouts();
            SweepFinishedMatches();
            ReconcileRoomMembers();
        }

        // Self-heal (framework): a member's replicated room/seat must always match the
        // authoritative registry. Room creation and player spawn can race, and a player
        // stuck on "lobby / no seat" cannot act on the board at all, so the sweep
        // re-applies room assignment and match seating once a second.
        [Server]
        private void ReconcileRoomMembers()
        {
            if (_registry == null) return;
            foreach (var room in _registry.Rooms)
            {
                foreach (var connectionId in room.PlayerIds)
                    if (NetworkServer.connections.TryGetValue(connectionId, out var conn) && conn.identity != null)
                    {
                        if (conn.identity.TryGetComponent<NetworkPlayer>(out var player) &&
                            _registry.TryGetPlayer(connectionId, out var state) && player.RoomId != state.RoomId)
                            SyncPlayer(conn);
                        AddPlayerToMatch(room, conn);
                    }
                foreach (var connectionId in room.SpectatorIds)
                    if (NetworkServer.connections.TryGetValue(connectionId, out var conn) && conn.identity != null)
                        AddSpectatorToMatch(room, conn);
            }
        }

        // Turn timer (config.turnTimeoutSeconds): whoever runs out of thinking time
        // loses the match, but the room survives so the pair can rematch.
        [Server]
        private void SweepMatchTimeouts()
        {
            var timeout = config == null ? 0f : config.turnTimeoutSeconds;
            if (timeout <= 0f) return;
            var now = NetworkTime.localTime;
            foreach (var pair in _roomMatches)
            {
                var match = pair.Value;
                if (match == null || match.Phase != MatchPhase.Active) continue;
                match.ServerRefreshTurnSeconds();
                if (match.TurnDeadline <= 0d || now <= match.TurnDeadline) continue;
                var stableId = match.ServerFindStableIdBySeat(match.CurrentSeat);
                if (string.IsNullOrEmpty(stableId)) continue;
                if (match.ServerForfeit(stableId, MatchForfeitCause.Timeout))
                    Debug.Log($"Turn timeout in room {pair.Key}: seat {match.CurrentSeat} forfeits.", this);
            }
        }

        // A finished board keeps its final position on screen, but the room returns to
        // the lobby so the same players can ready up and start the next game instead of
        // waiting for the leader to click 返回大厅.
        [Server]
        private void SweepFinishedMatches()
        {
            if (_registry == null) return;
            foreach (var pair in _roomMatches)
            {
                var match = pair.Value;
                if (match == null || match.Phase != MatchPhase.Completed) continue;
                if (!_registry.ResetToLobby(pair.Key)) continue;
                // The room going back to the lobby is the single "match just finished"
                // edge, so the record is written exactly once per game.
                RecordFinishedMatch(pair.Key, match);
                // 收尾会话：否则房间回 Lobby 后仍带着上一局的参与者名册（与手动返回大厅路径一致）。
                match.ServerResetMatch();
                SyncPlayers(pair.Key);
                RefreshRoom(pair.Key);
                BroadcastClientState(ClientRoomOperation.ReturnedToLobby, pair.Key);
                Debug.Log($"Match in room {pair.Key} ended ({match.DescribeResult()} / {match.EndDetail}); room returned to lobby.", this);
            }
        }

        [Server]
        private void RecycleIdleRooms()        {
            var timeout = config == null ? 120f : config.roomIdleTimeout;
            if (timeout <= 0f) return;
            if (_registry.CollectIdleRooms(NetworkTime.localTime, timeout, _idleRooms) == 0) return;

            foreach (var room in _idleRooms)
            {
                DestroyRoomObjects(room.Id);
                foreach (var connectionId in room.MemberIds)
                    if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
                    {
                        SyncPlayer(conn);
                        PlacePlayerInLobby(conn);
                    }
                BroadcastClientState(ClientRoomOperation.Cancelled, room.Id);
                Debug.Log($"Recycled idle room '{room.Name}' after {timeout:F0}s of inactivity.", this);
            }
            _idleRooms.Clear();
        }

        // Seat expiry (M3 P2.19): once a reconnect seat lapses the kept-warm room can be
        // collected, and the name reservation is released so other players may use it.
        [Server]
        private void PruneReconnectState()
        {
            var now = NetworkTime.localTime;
            if (_registry.PrunePendingSeats(now, _expiredSeatRooms) > 0)
            {
                foreach (var roomId in _expiredSeatRooms)
                {
                    if (!_registry.TryRemoveRoomIfEmpty(roomId)) continue;
                    DestroyRoomObjects(roomId);
                    BroadcastClientState(ClientRoomOperation.Cancelled, roomId);
                    Debug.Log($"Reconnect window lapsed; collected empty room {roomId}.", this);
                }
                _expiredSeatRooms.Clear();
            }
            if (authenticator is SocketAuthenticator socketAuthenticator)
                socketAuthenticator.CleanupNameReservations(now);
        }
    }
}
