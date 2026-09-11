using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Network adapter for the room domain.
    ///
    /// The server stays in one physical scene. RoomRegistry owns room rules and
    /// NetworkMatch owns visibility. This keeps room state independent from Mirror's
    /// global NetworkRoomManager scene transition lifecycle.
    /// </summary>
    [AddComponentMenu("Socket/Network Room Manager")]
    [RequireComponent(typeof(MatchInterestManagement))]
    [RequireComponent(typeof(SocketNetworkMetrics))]
    public sealed class SocketRoomManager : NetworkManager
    {
        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private NetworkRoomState roomStatePrefab;
        [SerializeField] private NetworkRoomChat roomChatPrefab;
        [SerializeField] private NetworkInteractable interactablePrefab;
        [SerializeField] private NetworkGomokuMatch gomokuMatchPrefab;
        private SocketNetworkMetrics metrics;

        private RoomRegistry _registry;
        private readonly Dictionary<Guid, NetworkRoomState> _roomStates = new Dictionary<Guid, NetworkRoomState>();
        private readonly Dictionary<Guid, NetworkRoomChat> _roomChats = new Dictionary<Guid, NetworkRoomChat>();
        private readonly Dictionary<Guid, NetworkGomokuMatch> _roomMatches = new Dictionary<Guid, NetworkGomokuMatch>();
        private readonly Dictionary<Guid, List<NetworkInteractable>> _roomInteractables = new Dictionary<Guid, List<NetworkInteractable>>();

        private readonly List<RoomInfo> _availableRooms = new List<RoomInfo>();
        private readonly List<PlayerInfo> _currentPlayers = new List<PlayerInfo>();
        private Guid _localRoomId = LobbyRoom.Id;
        private RoomPhase _localPhase = RoomPhase.Lobby;
        private string _localRoomName = string.Empty;
        private string _lastError = string.Empty;
        private MultiplayerErrorCode _lastErrorCode = MultiplayerErrorCode.None;
        private Guid _pendingRoomId;
        private ConnectionRateLimiter _rateLimiter;
        private readonly List<RoomRegistry.Room> _idleRooms = new List<RoomRegistry.Room>();
        private readonly List<Guid> _expiredSeatRooms = new List<Guid>();
        private float _nextIdleSweep;

        public event Action RoomStateChanged;

        public MultiplayerConfig Config => config;
        public string RoomName => string.IsNullOrWhiteSpace(_localRoomName) ? (config == null ? "Socket Room" : config.roomName) : _localRoomName;
        public int ConnectedPlayerCount => _registry == null ? numPlayers : _registry.Players.Count();
        public RoomPhase CurrentPhase => _localPhase;
        public Guid LocalRoomId => _localRoomId;
        public IReadOnlyList<RoomInfo> AvailableRooms => _availableRooms;
        public IReadOnlyList<PlayerInfo> CurrentPlayers => _currentPlayers;
        public string LastError => _lastError;
        public MultiplayerErrorCode LastErrorCode => _lastErrorCode;
        public SocketNetworkMetrics Metrics => metrics == null ? metrics = GetComponent<SocketNetworkMetrics>() : metrics;
        public RoomInfo[] GetDiscoveryRooms() => BuildRoomInfos();

        // Kept for old scene/prefab data while the generated assets migrate to M2.
        [Server]
        public void ServerEnsureLeader(SocketRoomPlayer joiningPlayer) { }

        public override void Awake()
        {
            base.Awake();

            if (config == null)
            {
                Debug.LogWarning("SocketRoomManager has no MultiplayerConfig assigned.", this);
                return;
            }

            maxConnections = config.ServerConnectionLimit;
            sendRate = config.sendRate;
            networkAddress = config.defaultAddress;
            offlineScene = config.offlineScene;
            onlineScene = config.lobbyScene;
            ApplyPort(config.port);
            RegisterTemplatePrefabs();
            metrics = GetComponent<SocketNetworkMetrics>();
        }

        public void ApplyPort(ushort value)
        {
            var transport = GetComponent<kcp2k.KcpTransport>();
            if (transport != null)
            {
                transport.Port = value;
                return;
            }
            Debug.LogWarning("KcpTransport was not found; configure the active Mirror transport port manually.", this);
        }

        public void StartConfigured()
        {
            var mode = config == null ? NetworkStartMode.Manual : config.defaultStartMode;
            switch (mode)
            {
                case NetworkStartMode.Host: StartHost(); break;
                case NetworkStartMode.Client: StartClient(); break;
                case NetworkStartMode.Server: StartServer(); break;
            }
        }

        public void StartClient(string address)
        {
            networkAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address;
            StartClient();
        }

        public void StartClient(SocketRoomDiscovery.RoomInfo room)
        {
            networkAddress = room.address;
            if (room.port > 0) ApplyPort(room.port);
            _pendingRoomId = room.roomId;
            StartClient();
        }

        [Client]
        public void ClientJoinPendingRoom()
        {
            if (_pendingRoomId == Guid.Empty || !NetworkClient.active || NetworkClient.localPlayer == null) return;
            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Join,
                roomId = _pendingRoomId
            });
            _pendingRoomId = Guid.Empty;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _registry = new RoomRegistry(config == null ? 16 : config.maxRooms);
            // Server-only admission policy (M3-S.5); capacities are twice the rates
            // so short UI bursts pass while sustained spam is rejected.
            _rateLimiter = new ConnectionRateLimiter(
                config == null ? 2f : config.roomCommandPerSecond,
                config == null ? 2f : config.chatPerSecond,
                config == null ? 2f : config.interactPerSecond,
                config == null ? 4f : config.gameCommandPerSecond);
            NetworkServer.RegisterHandler<ServerRoomMessage>(OnServerRoomMessage);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkClient.ReplaceHandler<ClientRoomMessage>(OnClientRoomMessage);
            ClearClientRoomState();
        }

        public override void OnStopServer()
        {
            foreach (var roomId in _roomStates.Keys.ToArray())
                DestroyRoomObjects(roomId);
            _roomStates.Clear();
            _roomChats.Clear();
            _roomMatches.Clear();
            _roomInteractables.Clear();
            _registry = null;
            _rateLimiter = null;
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            ClearClientRoomState();
            base.OnStopClient();
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);
            if (conn != null) SendClientState(conn);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (conn == null || playerPrefab == null) return;

            var start = GetStartPosition();
            var playerObject = start == null
                ? Instantiate(playerPrefab)
                : Instantiate(playerPrefab, start.position, start.rotation);
            playerObject.name = $"{playerPrefab.name} [connId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, playerObject);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // Mirror reuses connection ids; a fresh client must not inherit exhausted buckets.
            _rateLimiter?.Forget(conn.connectionId);
            var stableId = conn == null ? null : conn.authenticationData as string;
            var window = config == null ? 60f : config.reconnectWindow;
            var now = NetworkTime.localTime;
            var reserved = false;
            var previousRoomId = LobbyRoom.Id;
            if (_registry != null && conn != null)
            {
                // Remember the seat before removal: RemovePlayer keeps an empty room alive
                // while an unexpired seat points at it (M3 P2.19).
                if (window > 0f &&
                    !string.IsNullOrWhiteSpace(stableId) &&
                    _registry.TryGetPlayer(conn.connectionId, out var state) &&
                    state.RoomId != LobbyRoom.Id)
                {
                    _registry.RecordPendingSeat(stableId, state.RoomId, state.IsSpectator, now + window);
                    reserved = true;
                }

                _registry.RemovePlayer(conn.connectionId, now, out previousRoomId);
                if (previousRoomId != LobbyRoom.Id)
                {
                    if (_registry.TryGetRoom(previousRoomId, out var previousRoom))
                    {
                        HandleGomokuParticipantLeft(previousRoom, conn);
                        SyncPlayers(previousRoom);
                    }
                    else
                        DestroyRoomObjects(previousRoomId);
                }
            }

            if (authenticator is SocketAuthenticator socketAuthenticator && !string.IsNullOrWhiteSpace(stableId))
            {
                // Reserve the name for the window so nobody can steal the seat's identity.
                if (reserved)
                    socketAuthenticator.ReserveName(stableId, now + window);
                else
                    socketAuthenticator.ReleaseName(stableId);
            }

            base.OnServerDisconnect(conn);
            RefreshAllRoomObjects();
            BroadcastClientState();
        }

        [Server]
        public void RegisterPlayer(NetworkPlayer player)
        {
            if (player == null || player.connectionToClient == null) return;
            if (_registry == null)
                _registry = new RoomRegistry(config == null ? 16 : config.maxRooms);
            var connection = player.connectionToClient;
            if (!_registry.TryAddPlayer(
                    connection.connectionId,
                    connection.authenticationData as string,
                    out var state))
                return;

            var name = state.DisplayName;
            var color = Color.HSVToRGB((player.netId * 0.17f) % 1f, 0.7f, 0.95f);
            player.ServerSetIdentity(name, color);

            // Reconnect (M3 P2.19): a live seat for this stable id sends the player
            // straight back into the old room instead of the lobby.
            if (_registry.TryConsumePendingSeat(name, NetworkTime.localTime, out var seat) &&
                _registry.TryGetRoom(seat.RoomId, out _) &&
                _registry.TryRejoinRoom(connection.connectionId, seat.RoomId, seat.IsSpectator, out var rejoinedRoom, out _))
            {
                _registry.TouchRoom(rejoinedRoom.Id, NetworkTime.localTime);
                SyncPlayer(connection);
                if (!seat.IsSpectator)
                    PlacePlayerInRoom(connection, rejoinedRoom);
                RefreshRoom(rejoinedRoom.Id);
                if (_roomMatches.TryGetValue(rejoinedRoom.Id, out var match) && match != null && match.Phase == MatchPhase.Waiting)
                {
                    if (seat.IsSpectator) AddSpectatorToGomokuMatch(rejoinedRoom, connection);
                    else AddPlayerToGomokuMatch(rejoinedRoom, connection);
                }
                BroadcastClientState(ClientRoomOperation.Joined, rejoinedRoom.Id);
                return;
            }

            player.ServerAssignRoom(LobbyRoom.Id, false, false);
            BroadcastClientState();
        }

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
            if (_roomMatches.TryGetValue(room.Id, out var match) && match != null && !match.ServerStartMatch())
            {
                // Do not leave the room stuck InGame without a running match.
                _registry.RollbackStart(room.Id);
                SendError(requester, "The match could not be started.", MultiplayerErrorCode.StartFailed);
                return;
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
            RebindGomokuParticipants(room);
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
                SendError(conn, "Too many room requests.", MultiplayerErrorCode.RateLimited);
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
            AddPlayerToGomokuMatch(room, conn);
            RefreshRoom(room.Id);
            BroadcastClientState(ClientRoomOperation.Created, room.Id);
        }

        [Server]
        private void JoinRoom(NetworkConnectionToClient conn, Guid roomId)
        {
            if (_registry.TryGetRoom(roomId, out var existingRoom) && existingRoom.Phase != RoomPhase.Lobby && HasGomokuMatch())
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
            AddPlayerToGomokuMatch(room, conn);
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
                HandleGomokuParticipantLeft(roomId, conn);
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
            var gomokuRules = template == null ? null : template.gomokuRules;
            if (gomokuRules != null && !gomokuRules.allowSpectators)
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
            AddSpectatorToGomokuMatch(room, conn);
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
                    RemovePlayerFromGomokuMatch(roomId, affectedConnection);
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

            EnsureGomokuMatch(room);
        }

        [Server]
        private void EnsureGomokuMatch(RoomRegistry.Room room)
        {
            if (room == null || _roomMatches.ContainsKey(room.Id) || gomokuMatchPrefab == null) return;
            var template = config == null ? null : config.defaultRoomTemplate;
            var gomokuRules = template == null ? null : template.gomokuRules;
            if (gomokuRules == null) return;

            var matchObject = Instantiate(gomokuMatchPrefab.gameObject);
            var match = matchObject.GetComponent<NetworkGomokuMatch>();
            match.ServerInitialize(room.Id, gomokuRules.CreateRuleset());
            NetworkServer.Spawn(matchObject);
            _roomMatches.Add(room.Id, match);
        }

        [Server]
        private void AddPlayerToGomokuMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerAddPlayer(player.StableId, player.displayName, room.PlayerIds.IndexOf(conn.connectionId));
        }

        [Server]
        private void AddSpectatorToGomokuMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerAddSpectator(player.StableId, player.displayName);
        }

        [Server]
        private void RebindGomokuParticipants(RoomRegistry.Room room)
        {
            if (room == null) return;
            foreach (var connectionId in room.PlayerIds)
                if (NetworkServer.connections.TryGetValue(connectionId, out var connection))
                    AddPlayerToGomokuMatch(room, connection);
            foreach (var connectionId in room.SpectatorIds)
                if (NetworkServer.connections.TryGetValue(connectionId, out var connection))
                    AddSpectatorToGomokuMatch(room, connection);
        }

        [Server]
        private void RemovePlayerFromGomokuMatch(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerRemoveParticipant(player.StableId);
        }

        [Server]
        private void RemovePlayerFromGomokuMatch(Guid roomId, NetworkConnectionToClient conn)
        {
            if (_registry != null && _registry.TryGetRoom(roomId, out var room))
                RemovePlayerFromGomokuMatch(room, conn);
        }

        /// <summary>
        /// A member left a room (leave or disconnect): mark the match participant gone.
        /// Active matches forfeit instead of deadlocking on the leaver's turn.
        /// </summary>
        [Server]
        private void HandleGomokuParticipantLeft(RoomRegistry.Room room, NetworkConnectionToClient conn)
        {
            if (room == null || conn == null || conn.identity == null || !_roomMatches.TryGetValue(room.Id, out var match)) return;
            if (!conn.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            match.ServerHandleParticipantLeft(player.StableId);
        }

        [Server]
        private void HandleGomokuParticipantLeft(Guid roomId, NetworkConnectionToClient conn)
        {
            if (_registry != null && _registry.TryGetRoom(roomId, out var room))
                HandleGomokuParticipantLeft(room, conn);
        }

        /// <summary>
        /// Server-side admission for command-style messages (chat, interaction, game
        /// commands). Rate limits are server behavior only and stay out of the config
        /// signature; they live here so every entry point shares one policy (M3-S.5).
        /// </summary>
        public bool ServerTryConsumeRate(int connectionId, RateLimitKind kind)
        {
            return _rateLimiter == null || _rateLimiter.TryAcquire(connectionId, kind, NetworkTime.localTime);
        }

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
        }

        [Server]
        private void RecycleIdleRooms()
        {
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

        private int GetRequiredMatchPlayers()
        {
            var configuredMinimum = config == null ? 1 : config.EffectiveMinPlayers;
            var template = config == null ? null : config.defaultRoomTemplate;
            return template != null && template.gomokuRules != null
                ? Math.Max(2, configuredMinimum)
                : configuredMinimum;
        }

        private int GetMaxSpectators()
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            return template == null ? config == null ? 20 : config.maxSpectators : template.maxSpectators;
        }

        private bool HasGomokuMatch()
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            return template != null && template.gomokuRules != null;
        }

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

        [Server]
        private void BroadcastClientState(ClientRoomOperation operation = ClientRoomOperation.List, Guid focusRoomId = default)
        {
            if (_registry == null) return;
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || !_registry.TryGetPlayer(conn.connectionId, out var player)) continue;
                RoomRegistry.Room room = null;
                var isInRoom = player.RoomId != LobbyRoom.Id && _registry.TryGetRoom(player.RoomId, out room);
                if (isInRoom)
                {
                    var currentOperation = player.RoomId == focusRoomId ? operation : ClientRoomOperation.UpdateRoom;
                    conn.Send(new ClientRoomMessage
                    {
                        clientRoomOperation = currentOperation,
                        roomId = room.Id,
                        phase = room.Phase,
                        error = string.Empty,
                        roomInfos = BuildRoomInfos(),
                        playerInfos = BuildPlayerInfos(room)
                    });
                }
                else
                {
                    conn.Send(new ClientRoomMessage
                    {
                        clientRoomOperation = operation == ClientRoomOperation.Error
                            ? ClientRoomOperation.Error
                            : ClientRoomOperation.List,
                        roomId = LobbyRoom.Id,
                        phase = RoomPhase.Lobby,
                        error = string.Empty,
                        roomInfos = BuildRoomInfos(),
                        playerInfos = Array.Empty<PlayerInfo>()
                    });
                }
            }
        }

        [Server]
        private void SendClientState(NetworkConnectionToClient conn)
        {
            if (_registry == null || conn == null || !_registry.TryGetPlayer(conn.connectionId, out var player)) return;
            if (player.RoomId != LobbyRoom.Id && _registry.TryGetRoom(player.RoomId, out var room))
            {
                conn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.UpdateRoom,
                    roomId = room.Id,
                    phase = room.Phase,
                    error = string.Empty,
                    roomInfos = BuildRoomInfos(),
                    playerInfos = BuildPlayerInfos(room)
                });
            }
            else
            {
                conn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.List,
                    roomId = LobbyRoom.Id,
                    phase = RoomPhase.Lobby,
                    error = string.Empty,
                    roomInfos = BuildRoomInfos(),
                    playerInfos = Array.Empty<PlayerInfo>()
                });
            }
        }

        [Server]
        private void SendError(NetworkConnectionToClient conn, string error, MultiplayerErrorCode errorCode = MultiplayerErrorCode.None)
        {
            if (conn == null) return;

            var roomId = LobbyRoom.Id;
            var phase = RoomPhase.Lobby;
            var playerInfos = Array.Empty<PlayerInfo>();

            if (_registry != null &&
                _registry.TryGetPlayer(conn.connectionId, out var player) &&
                player.RoomId != LobbyRoom.Id &&
                _registry.TryGetRoom(player.RoomId, out var room))
            {
                roomId = room.Id;
                phase = room.Phase;
                playerInfos = BuildPlayerInfos(room);
            }

            conn.Send(new ClientRoomMessage
            {
                clientRoomOperation = ClientRoomOperation.Error,
                roomId = roomId,
                phase = phase,
                error = error,
                errorCode = errorCode,
                roomInfos = BuildRoomInfos(),
                playerInfos = playerInfos
            });
            Debug.LogWarning($"Room operation rejected: {error} ({errorCode})", this);
        }

        private RoomInfo[] BuildRoomInfos()
        {
            if (_registry == null) return Array.Empty<RoomInfo>();
            return _registry.Rooms
                .OrderBy(room => room.Name)
                .Select(room => new RoomInfo
                {
                    roomId = room.Id,
                    roomName = room.Name,
                    playerCount = room.PlayerIds.Count,
                    maxPlayers = room.MaxPlayers,
                    phase = room.Phase,
                    spectatorCount = room.SpectatorCount,
                    maxSpectators = GetMaxSpectators()
                })
                .ToArray();
        }

        private PlayerInfo[] BuildPlayerInfos(RoomRegistry.Room room)
        {
            if (room == null || _registry == null) return Array.Empty<PlayerInfo>();
            return room.MemberIds
                .Where(id => _registry.TryGetPlayer(id, out _))
                .Select(id =>
                {
                    var player = _registry.Players.First(item => item.ConnectionId == id);
                    return new PlayerInfo
                    {
                        playerIndex = player.PlayerIndex,
                        displayName = player.DisplayName,
                        displayColor = GetPlayerColor(id),
                        ready = player.Ready,
                        isLeader = player.IsLeader,
                        isSpectator = player.IsSpectator,
                        roomId = room.Id
                    };
                })
                .ToArray();
        }

        private Color32 GetPlayerColor(int connectionId)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn) &&
                conn.identity != null &&
                conn.identity.TryGetComponent<NetworkPlayer>(out var player))
                return player.displayColor;
            return Color.white;
        }

        private void OnClientRoomMessage(ClientRoomMessage message)
        {
            _availableRooms.Clear();
            if (message.roomInfos != null) _availableRooms.AddRange(message.roomInfos);
            _currentPlayers.Clear();
            if (message.playerInfos != null) _currentPlayers.AddRange(message.playerInfos);
            _localRoomId = message.roomId == Guid.Empty ? LobbyRoom.Id : message.roomId;
            _localPhase = message.phase;
            _lastError = message.error ?? string.Empty;
            _lastErrorCode = message.errorCode;
            _localRoomName = _availableRooms
                .Where(room => room.roomId == _localRoomId)
                .Select(room => room.roomName)
                .FirstOrDefault() ?? string.Empty;
            if (_localRoomId == LobbyRoom.Id) ClientJoinPendingRoom();
            RoomStateChanged?.Invoke();
        }

        private void ClearClientRoomState()
        {
            _availableRooms.Clear();
            _currentPlayers.Clear();
            _localRoomId = LobbyRoom.Id;
            _localPhase = RoomPhase.Lobby;
            _localRoomName = string.Empty;
            _lastError = string.Empty;
            _lastErrorCode = MultiplayerErrorCode.None;
            _pendingRoomId = Guid.Empty;
            RoomStateChanged?.Invoke();
        }

        private static string SanitizeRoomName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Socket Room";
            value = value.Trim();
            return value.Length <= 32 ? value : value.Substring(0, 32);
        }
    }
}
