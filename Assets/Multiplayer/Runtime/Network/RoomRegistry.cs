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

        public bool TryCreateRoom(int connectionId, string name, int maxPlayers, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                return false;
            }
            if (_rooms.Count >= _maxRooms)
            {
                error = "Room limit reached.";
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

        public bool TryJoinRoom(int connectionId, Guid roomId, bool allowLateJoiners, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                return false;
            }
            if (!_rooms.TryGetValue(roomId, out room))
            {
                error = "Room does not exist.";
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                return false;
            }
            if (room.Phase != RoomPhase.Lobby && !allowLateJoiners)
            {
                error = "Room has already started.";
                return false;
            }
            if (room.PlayerIds.Count >= room.MaxPlayers)
            {
                error = "Room is full.";
                return false;
            }

            room.PlayerIds.Add(connectionId);
            AssignPlayer(player, room, false, false);
            return true;
        }

        public bool TryJoinRoomAsSpectator(int connectionId, Guid roomId, int maxSpectators, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                return false;
            }
            if (!_rooms.TryGetValue(roomId, out room))
            {
                error = "Room does not exist.";
                return false;
            }
            if (player.RoomId != LobbyRoom.Id)
            {
                error = "Player is already in a room.";
                return false;
            }
            if (room.Phase == RoomPhase.Lobby)
            {
                error = "Room has not started.";
                return false;
            }

            if (room.SpectatorIds.Count >= Math.Max(0, maxSpectators))
            {
                error = "Spectator limit reached.";
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

        public bool TryLeaveRoom(int connectionId, out Guid roomId, out bool roomRemoved, out string error)
        {
            roomId = LobbyRoom.Id;
            roomRemoved = false;
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out var player))
            {
                error = "Player is not registered.";
                return false;
            }
            roomId = player.RoomId;
            if (roomId == LobbyRoom.Id || !_rooms.TryGetValue(roomId, out var room))
            {
                error = "Player is not in a room.";
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

        public bool TryCancelRoom(int connectionId, out Guid roomId, out int[] affectedPlayers, out string error)
        {
            roomId = LobbyRoom.Id;
            affectedPlayers = Array.Empty<int>();
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out var requester))
            {
                error = "Player is not registered.";
                return false;
            }
            roomId = requester.RoomId;
            if (roomId == LobbyRoom.Id || !_rooms.TryGetValue(roomId, out var room))
            {
                error = "Player is not in a room.";
                return false;
            }
            if (!requester.IsLeader)
            {
                error = "Only the room leader can cancel the room.";
                return false;
            }

            affectedPlayers = room.PlayerIds.Concat(room.SpectatorIds).ToArray();
            foreach (var playerId in affectedPlayers)
                if (_players.TryGetValue(playerId, out var player))
                    AssignPlayer(player, null, false, false);
            _rooms.Remove(room.Id);
            return true;
        }

        public bool TryToggleReady(int connectionId, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var player, out room, out error)) return false;
            if (player.IsSpectator)
            {
                error = "Spectators cannot change ready state.";
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Ready state can only change in the lobby.";
                return false;
            }
            player.Ready = !player.Ready;
            return true;
        }

        public bool TrySetReady(int connectionId, bool value, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var player, out room, out error)) return false;
            if (player.IsSpectator)
            {
                error = "Spectators cannot change ready state.";
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Ready state can only change in the lobby.";
                return false;
            }
            player.Ready = value;
            return true;
        }

        public bool TryStartRoom(int connectionId, int minPlayers, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var requester, out room, out error)) return false;
            if (!requester.IsLeader)
            {
                error = "Only the room leader can start the room.";
                return false;
            }
            if (room.Phase != RoomPhase.Lobby)
            {
                error = "Room has already started.";
                return false;
            }
            if (room.PlayerIds.Count < Math.Max(1, minPlayers))
            {
                error = "Not enough players.";
                return false;
            }
            if (room.PlayerIds.Any(id => !_players[id].Ready))
            {
                error = "All players must be ready.";
                return false;
            }

            room.Phase = RoomPhase.InGame;
            return true;
        }

        public bool TryReturnToLobby(int connectionId, out Room room, out string error)
        {
            room = null;
            error = string.Empty;
            if (!TryGetPlayerRoom(connectionId, out var requester, out room, out error)) return false;
            if (!requester.IsLeader)
            {
                error = "Only the room leader can return to the lobby.";
                return false;
            }
            if (room.Phase != RoomPhase.InGame)
            {
                error = "Room is already in the lobby.";
                return false;
            }

            room.Phase = RoomPhase.Lobby;
            foreach (var playerId in room.PlayerIds)
                _players[playerId].Ready = false;
            return true;
        }

        public bool TryGetPlayer(int connectionId, out Player player) =>
            _players.TryGetValue(connectionId, out player);

        public bool TryGetRoom(Guid roomId, out Room room) =>
            _rooms.TryGetValue(roomId, out room);

        private bool TryGetPlayerRoom(int connectionId, out Player player, out Room room, out string error)
        {
            player = null;
            room = null;
            error = string.Empty;
            if (!_players.TryGetValue(connectionId, out player))
            {
                error = "Player is not registered.";
                return false;
            }
            if (player.RoomId == LobbyRoom.Id || !_rooms.TryGetValue(player.RoomId, out room))
            {
                error = "Player is not in a room.";
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
