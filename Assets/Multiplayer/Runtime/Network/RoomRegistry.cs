using System;
using System.Collections.Generic;
using System.Linq;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Server-side room state. It intentionally knows nothing about Mirror objects,
    /// scenes, or Unity so the room rules can be tested without starting a server.
    /// </summary>
    public sealed class RoomRegistry
    {
        public sealed class Player
        {
            public int ConnectionId;
            public int PlayerIndex;
            public string DisplayName;
            public Guid RoomId;
            public bool Ready;
            public bool IsLeader;
            public bool IsSpectator;
        }

        public sealed class Room
        {
            public Guid Id;
            public string Name;
            public int MaxPlayers;
            public RoomPhase Phase;
            /// <summary>Server clock of the last tracked room activity; 0 = not tracked yet.</summary>
            public double LastActivityTime;
            internal readonly List<int> PlayerIds = new List<int>();
            internal readonly List<int> SpectatorIds = new List<int>();
            internal IEnumerable<int> MemberIds => PlayerIds.Concat(SpectatorIds);
            public int PlayerCount => PlayerIds.Count;
            public int SpectatorCount => SpectatorIds.Count;
            public int MemberCount => PlayerIds.Count + SpectatorIds.Count;
        }

        private readonly Dictionary<Guid, Room> _rooms = new Dictionary<Guid, Room>();
        private readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        private readonly int _maxRooms;
        private int _nextPlayerIndex;

        public RoomRegistry(int maxRooms = 64)
        {
            _maxRooms = Math.Max(1, maxRooms);
        }

        public IEnumerable<Room> Rooms => _rooms.Values;
        public IEnumerable<Player> Players => _players.Values;

        public bool TryAddPlayer(int connectionId, string displayName, out Player player)
        {
            player = null;
            if (_players.ContainsKey(connectionId)) return false;

            player = new Player
            {
                ConnectionId = connectionId,
                PlayerIndex = _nextPlayerIndex++,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                RoomId = LobbyRoom.Id,
                Ready = false,
                IsLeader = false,
                IsSpectator = false
            };
            _players.Add(connectionId, player);
            return true;
        }

        public bool RemovePlayer(int connectionId, out Guid previousRoomId)
        {
            previousRoomId = LobbyRoom.Id;
            if (!_players.TryGetValue(connectionId, out var player)) return false;

            previousRoomId = player.RoomId;
            if (previousRoomId != LobbyRoom.Id && _rooms.TryGetValue(previousRoomId, out var room))
            {
                room.PlayerIds.Remove(connectionId);
                room.SpectatorIds.Remove(connectionId);
                TransferLeader(room);
                if (room.MemberCount == 0) _rooms.Remove(room.Id);
            }

            _players.Remove(connectionId);
            return true;
        }

        public bool TryCreateRoom(int connectionId, string name, int maxPlayers, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                errorCode = MultiplayerErrorCode.AlreadyInRoom;
                return false;
            }
            if (_rooms.Count >= _maxRooms)
            {
                error = "Room limit reached.";
                errorCode = MultiplayerErrorCode.RoomLimitReached;
                return false;
            }

            room = new Room
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "Socket Room" : name.Trim(),
                MaxPlayers = Math.Max(1, maxPlayers),
                Phase = RoomPhase.Lobby
            };
            room.PlayerIds.Add(connectionId);
            _rooms.Add(room.Id, room);
            AssignPlayer(player, room, true, false);
            return true;
        }

        public bool TryJoinRoom(int connectionId, Guid roomId, bool allowLateJoiners, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            if (!_rooms.TryGetValue(roomId, out room))
            {
                error = "Room does not exist.";
                errorCode = MultiplayerErrorCode.RoomNotFound;
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                errorCode = MultiplayerErrorCode.AlreadyInRoom;
                return false;
            }
            if (room.Phase != RoomPhase.Lobby && !allowLateJoiners)
            {
                error = "Room has already started.";
                errorCode = MultiplayerErrorCode.RoomStarted;
                return false;
            }
            if (room.PlayerIds.Count >= room.MaxPlayers)
            {
                error = "Room is full.";
                errorCode = MultiplayerErrorCode.RoomFull;
                return false;
            }

            room.PlayerIds.Add(connectionId);
            AssignPlayer(player, room, false, false);
            return true;
        }

        public bool TryJoinRoomAsSpectator(int connectionId, Guid roomId, int maxSpectators, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            if (!_rooms.TryGetValue(roomId, out room))
            {
                error = "Room does not exist.";
                errorCode = MultiplayerErrorCode.RoomNotFound;
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                errorCode = MultiplayerErrorCode.AlreadyInRoom;
                return false;
            }
            if (room.Phase == RoomPhase.Lobby)
            {
                error = "Room has not started.";
                errorCode = MultiplayerErrorCode.RoomNotStarted;
                return false;
            }

            if (room.SpectatorIds.Count >= Math.Max(0, maxSpectators))
            {
                error = "Spectator limit reached.";
                errorCode = MultiplayerErrorCode.SpectatorLimitReached;
                return false;
            }

            room.SpectatorIds.Add(connectionId);
            AssignPlayer(player, room, false, true);
            return true;
        }

        public bool TryGetMemberIds(Guid roomId, out int[] playerIds, out int[] spectatorIds)
        {
            playerIds = Array.Empty<int>();
            spectatorIds = Array.Empty<int>();
            if (!_rooms.TryGetValue(roomId, out var room)) return false;
            playerIds = room.PlayerIds.ToArray();
            spectatorIds = room.SpectatorIds.ToArray();
            return true;
        }

        public bool TryLeaveRoom(int connectionId, out Guid roomId, out bool roomRemoved, out string error, out MultiplayerErrorCode errorCode)
        {
            roomId = LobbyRoom.Id;
            roomRemoved = false;
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            roomId = player.RoomId;
            if (roomId == LobbyRoom.Id || !_rooms.TryGetValue(roomId, out var room))
            {
                error = "Player is not in a room.";
                errorCode = MultiplayerErrorCode.NotInRoom;
                return false;
            }

            room.PlayerIds.Remove(connectionId);
            room.SpectatorIds.Remove(connectionId);
            AssignPlayer(player, null, false, false);
            TransferLeader(room);
            if (room.MemberCount == 0)
            {
                _rooms.Remove(room.Id);
                roomRemoved = true;
            }
            return true;
        }

        public bool TryCancelRoom(int connectionId, out Guid roomId, out int[] affectedPlayers, out string error, out MultiplayerErrorCode errorCode)
        {
            roomId = LobbyRoom.Id;
            affectedPlayers = Array.Empty<int>();
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out var requester))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            roomId = requester.RoomId;
            if (roomId == LobbyRoom.Id || !_rooms.TryGetValue(roomId, out var room))
            {
                error = "Player is not in a room.";
                errorCode = MultiplayerErrorCode.NotInRoom;
                return false;
            }
            if (!requester.IsLeader)
            {
                error = "Only the room leader can cancel the room.";
                errorCode = MultiplayerErrorCode.NotLeader;
                return false;
            }

            affectedPlayers = room.PlayerIds.Concat(room.SpectatorIds).ToArray();
            foreach (var playerId in affectedPlayers)
                if (_players.TryGetValue(playerId, out var player))
                    AssignPlayer(player, null, false, false);
            _rooms.Remove(room.Id);
            return true;
        }

        public bool TryToggleReady(int connectionId, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var player, out room, out error, out errorCode)) return false;
            if (player.IsSpectator)
            {
                error = "Spectators cannot change ready state.";
                errorCode = MultiplayerErrorCode.SpectatorCannotReady;
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Ready state can only change in the lobby.";
                errorCode = MultiplayerErrorCode.ReadyOnlyInLobby;
                return false;
            }
            player.Ready = !player.Ready;
            return true;
        }

        public bool TrySetReady(int connectionId, bool value, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var player, out room, out error, out errorCode)) return false;
            if (player.IsSpectator)
            {
                error = "Spectators cannot change ready state.";
                errorCode = MultiplayerErrorCode.SpectatorCannotReady;
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Ready state can only change in the lobby.";
                errorCode = MultiplayerErrorCode.ReadyOnlyInLobby;
                return false;
            }
            player.Ready = value;
            return true;
        }

        public bool TryStartRoom(int connectionId, int minPlayers, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var requester, out room, out error, out errorCode)) return false;
            if (!requester.IsLeader)
            {
                error = "Only the room leader can start the room.";
                errorCode = MultiplayerErrorCode.NotLeader;
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Room has already started.";
                errorCode = MultiplayerErrorCode.RoomStarted;
                return false;
            }
            if (room.PlayerIds.Count < Math.Max(1, minPlayers))
            {
                error = "Not enough players.";
                errorCode = MultiplayerErrorCode.NotEnoughPlayers;
                return false;
            }
            if (room.PlayerIds.Any(id => !_players[id].Ready))
            {
                error = "All players must be ready.";
                errorCode = MultiplayerErrorCode.NotAllReady;
                return false;
            }

            room.Phase = RoomPhase.InGame;
            return true;
        }

        public bool TryReturnToLobby(int connectionId, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var requester, out room, out error, out errorCode)) return false;
            if (!requester.IsLeader)
            {
                error = "Only the room leader can return to the lobby.";
                errorCode = MultiplayerErrorCode.NotLeader;
                return false;
            }
            if (room.Phase != RoomPhase.InGame)
            {
                error = "Room is already in the lobby.";
                errorCode = MultiplayerErrorCode.AlreadyInLobby;
                return false;
            }

            room.Phase = RoomPhase.Lobby;
            foreach (var playerId in room.PlayerIds)
                _players[playerId].Ready = false;
            return true;
        }

        /// <summary>
        /// Reverts an InGame room to Lobby when the match adapter failed to start,
        /// so the room does not stay stuck in InGame without a running match.
        /// </summary>
        public bool RollbackStart(Guid roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room) || room.Phase != RoomPhase.InGame) return false;
            room.Phase = RoomPhase.Lobby;
            foreach (var playerId in room.PlayerIds)
                if (_players.TryGetValue(playerId, out var player)) player.Ready = false;
            return true;
        }

        public bool TryGetPlayer(int connectionId, out Player player) =>
            _players.TryGetValue(connectionId, out player);

        public bool TryGetRoom(Guid roomId, out Room room) =>
            _rooms.TryGetValue(roomId, out room);

        /// <summary>Marks a room as recently active; drives idle recycling (M3 P2.15).</summary>
        public void TouchRoom(Guid roomId, double now)
        {
            if (_rooms.TryGetValue(roomId, out var room)) room.LastActivityTime = now;
        }

        /// <summary>
        /// Removes rooms with no tracked activity for <paramref name="timeout"/> seconds and
        /// returns their members to the lobby. Rooms whose LastActivityTime was never set (0)
        /// are never recycled — creation paths must TouchRoom first, so a missing clock can
        /// not destroy a fresh room. Member connections stay alive so the network adapter can
        /// notify and teleport them; this method only mutates registry state.
        /// </summary>
        public int CollectIdleRooms(double now, double timeout, List<Room> recycled)
        {
            recycled.Clear();
            if (timeout <= 0d) return 0;

            foreach (var room in _rooms.Values.ToArray())
                if (room.LastActivityTime > 0d && now - room.LastActivityTime >= timeout)
                    recycled.Add(room);

            foreach (var room in recycled)
            {
                foreach (var playerId in room.PlayerIds.Concat(room.SpectatorIds).ToArray())
                    if (_players.TryGetValue(playerId, out var player))
                        AssignPlayer(player, null, false, false);
                _rooms.Remove(room.Id);
            }
            return recycled.Count;
        }

        private bool TryGetPlayerRoom(int connectionId, out Player player, out Room room, out string error, out MultiplayerErrorCode errorCode)
        {
            player = null;
            room = null;
            error = string.Empty;
            errorCode = MultiplayerErrorCode.None;
            if (!_players.TryGetValue(connectionId, out player))
            {
                error = "Player is not registered.";
                errorCode = MultiplayerErrorCode.NotRegistered;
                return false;
            }
            if (player.RoomId == LobbyRoom.Id || !_rooms.TryGetValue(player.RoomId, out room))
            {
                error = "Player is not in a room.";
                errorCode = MultiplayerErrorCode.NotInRoom;
                return false;
            }
            return true;
        }

        private static void AssignPlayer(Player player, Room room, bool leader, bool spectator)
        {
            player.RoomId = room == null ? LobbyRoom.Id : room.Id;
            player.Ready = false;
            player.IsLeader = room != null && leader;
            player.IsSpectator = room != null && spectator;
        }

        private void TransferLeader(Room room)
        {
            if (room == null || room.PlayerIds.Count == 0) return;
            if (room.PlayerIds.Any(id => _players.TryGetValue(id, out var player) && player.IsLeader)) return;
            var nextLeader = room.PlayerIds.FirstOrDefault(id => _players.TryGetValue(id, out var player) && !player.IsSpectator);
            if (_players.TryGetValue(nextLeader, out var playerToPromote))
                playerToPromote.IsLeader = true;
        }
    }
}
