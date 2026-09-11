using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

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
    public sealed partial class SocketRoomManager : NetworkManager
    {
        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private NetworkRoomState roomStatePrefab;
        [SerializeField] private NetworkRoomChat roomChatPrefab;
        [SerializeField] private NetworkInteractable interactablePrefab;
        // Renamed from gomokuMatchPrefab so the manager only knows the framework adapter;
        // the former name is kept so existing scenes keep their prefab reference.
        [FormerlySerializedAs("gomokuMatchPrefab")]
        [SerializeField] private NetworkMatchAdapter matchPrefab;
        private SocketNetworkMetrics metrics;

        private RoomRegistry _registry;
        private readonly Dictionary<Guid, NetworkRoomState> _roomStates = new Dictionary<Guid, NetworkRoomState>();
        private readonly Dictionary<Guid, NetworkRoomChat> _roomChats = new Dictionary<Guid, NetworkRoomChat>();
        private readonly Dictionary<Guid, NetworkMatchAdapter> _roomMatches = new Dictionary<Guid, NetworkMatchAdapter>();
        private readonly Dictionary<Guid, List<NetworkInteractable>> _roomInteractables = new Dictionary<Guid, List<NetworkInteractable>>();
        // Bounded history per room; retention comes from the config centre (0 = off).
        private MatchRecordBook _matchRecords;

        private readonly List<RoomInfo> _availableRooms = new List<RoomInfo>();
        private readonly List<PlayerInfo> _currentPlayers = new List<PlayerInfo>();
        private readonly List<MatchRecordInfo> _roomMatchRecords = new List<MatchRecordInfo>();
        private Guid _localRoomId = LobbyRoom.Id;
        private RoomPhase _localPhase = RoomPhase.Lobby;
        private string _localRoomName = string.Empty;
        private string _lastError = string.Empty;
        private MultiplayerErrorCode _lastErrorCode = MultiplayerErrorCode.None;
        private Guid _pendingRoomId;
        private ConnectionRateLimiter _rateLimiter;
        /// <summary>限流拒绝回复的节流时间戳（每连接最多每秒回一次）。</summary>
        private readonly Dictionary<int, double> rateLimitReplies = new Dictionary<int, double>();
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
        /// <summary>Finished-match history of the local room (server-pushed, config-bounded).</summary>
        public IReadOnlyList<MatchRecordInfo> MatchRecords => _roomMatchRecords;
        public string LastError => _lastError;
        public MultiplayerErrorCode LastErrorCode => _lastErrorCode;
        public SocketNetworkMetrics Metrics => metrics == null ? metrics = GetComponent<SocketNetworkMetrics>() : metrics;
        public RoomInfo[] GetDiscoveryRooms() => BuildRoomInfos();

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
            // Mirror 的 StartClient() 会同步回调 OnStartClient() → ClearClientRoomState()，
            // 因此待加入房间必须在连接启动之后再写入，否则会被立即清空（发现列表加入静默失效）。
            StartClient();
            _pendingRoomId = room.roomId;
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
            rateLimitReplies.Remove(conn.connectionId);
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
                    _registry.RecordPendingSeat(stableId, state.RoomId, state.IsSpectator, now + window, state.Seat);
                    reserved = true;
                }

                _registry.RemovePlayer(conn.connectionId, now, out previousRoomId);
                if (previousRoomId != LobbyRoom.Id)
                {
                    if (_registry.TryGetRoom(previousRoomId, out var previousRoom))
                    {
                        HandleParticipantLeft(previousRoom, conn, MatchForfeitCause.Disconnect);
                        SyncPlayers(previousRoom);
                    }
                    else
                        DestroyRoomObjects(previousRoomId);
                }
            }

            if (authenticator is SocketAuthenticator socketAuthenticator && !string.IsNullOrWhiteSpace(stableId))
            {
                // 断开必须释放“已连接”占用：否则同名者（含本人）再也回不来。
                // 保留期改由 Reservation 保护，且放行条件是**一次性凭据**匹配，而不是名字相同。
                socketAuthenticator.ReleaseName(stableId);
                if (reserved) socketAuthenticator.ReserveName(stableId, now + window);
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
                _registry.TryRejoinRoom(connection.connectionId, seat.RoomId, seat.IsSpectator, out var rejoinedRoom, out _, seat.Seat))
            {
                _registry.TouchRoom(rejoinedRoom.Id, NetworkTime.localTime);
                SyncPlayer(connection);
                if (!seat.IsSpectator)
                    PlacePlayerInRoom(connection, rejoinedRoom);
                RefreshRoom(rejoinedRoom.Id);
                if (_roomMatches.TryGetValue(rejoinedRoom.Id, out var match) && match != null && match.Phase == MatchPhase.Waiting)
                {
                    if (seat.IsSpectator) AddSpectatorToMatch(rejoinedRoom, connection);
                    else AddPlayerToMatch(rejoinedRoom, connection);
                }
                BroadcastClientState(ClientRoomOperation.Joined, rejoinedRoom.Id);
                return;
            }

            player.ServerAssignRoom(LobbyRoom.Id, false, false);
            BroadcastClientState();
        }


        /// <summary>
        /// 限流拒绝回复：每连接最多每秒回一次，其余静默丢弃——防止“十字节请求换整张房间表”的
        /// 放大效应，也避免每次超限都打一条带堆栈的 Warning。
        /// </summary>
        [Server]
        private void SendRateLimited(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
            var now = NetworkTime.localTime;
            if (rateLimitReplies.TryGetValue(conn.connectionId, out var last) && now - last < 1d) return;
            rateLimitReplies[conn.connectionId] = now;
            SendError(conn, "Too many requests.", MultiplayerErrorCode.RateLimited);
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

        /// <summary>
        /// Tells one client that its match command was rejected. The kernel hands over a
        /// structured reason, which is mapped to the stable wire code here — no layer has
        /// to parse debug text. Only the sender hears it.
        /// </summary>
        [Server]
        public void ServerReportMatchRejection(NetworkConnectionToClient conn, MatchRejectReason reason, string debugText)
        {
            if (conn == null) return;
            SendError(conn, debugText, ToErrorCode(reason));
        }

        private static MultiplayerErrorCode ToErrorCode(MatchRejectReason reason)
        {
            switch (reason)
            {
                case MatchRejectReason.NotYourTurn: return MultiplayerErrorCode.NotYourTurn;
                case MatchRejectReason.InvalidCommand: return MultiplayerErrorCode.InvalidMatchCommand;
                case MatchRejectReason.NotActive: return MultiplayerErrorCode.MatchNotRunning;
                case MatchRejectReason.NotParticipant: return MultiplayerErrorCode.MatchNotRunning;
                case MatchRejectReason.NotConnected: return MultiplayerErrorCode.MatchNotRunning;
                default: return MultiplayerErrorCode.MatchNotRunning;
            }
        }

        /// <summary>
        /// Writes the finished match into the (config-bounded) record book. Called from the
        /// room's InGame → Lobby transition, which happens once per game.
        /// </summary>
        [Server]
        private void RecordFinishedMatch(Guid roomId, NetworkMatchAdapter match)
        {
            var limit = config == null ? 0 : config.matchRecordLimit;
            if (limit <= 0 || match == null) return;
            if (_matchRecords == null || _matchRecords.Limit != limit)
                _matchRecords = new MatchRecordBook(limit);
            _registry.TryGetRoom(roomId, out var room);
            _matchRecords.Record(roomId, match.ServerCreateRecord(room == null ? string.Empty : room.Name));
        }

        private MatchRecordInfo[] BuildMatchRecords(Guid roomId)
        {
            if (_matchRecords == null || !_matchRecords.Enabled || roomId == LobbyRoom.Id)
                return Array.Empty<MatchRecordInfo>();
            var records = _matchRecords.Get(roomId);
            return records.Count == 0 ? Array.Empty<MatchRecordInfo>() : new List<MatchRecordInfo>(records).ToArray();
        }


        private int GetRequiredMatchPlayers()
        {
            var configuredMinimum = config == null ? 1 : config.EffectiveMinPlayers;
            var template = config == null ? null : config.defaultRoomTemplate;
            return template != null && template.rules != null
                ? Math.Max(2, configuredMinimum)
                : configuredMinimum;
        }

        private int GetMaxSpectators()
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            return template == null ? config == null ? 20 : config.maxSpectators : template.maxSpectators;
        }

        private bool HasRoomMatch()
        {
            var template = config == null ? null : config.defaultRoomTemplate;
            return template != null && template.rules != null;
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
                        playerInfos = BuildPlayerInfos(room),
                        matchRecords = BuildMatchRecords(room.Id)
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
                    playerInfos = BuildPlayerInfos(room),
                    matchRecords = BuildMatchRecords(room.Id)
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
            // 一次字典查找代替 Players.First(...)：成员多时原实现是 O(n²)。
            return room.MemberIds
                .Select(id => _registry.TryGetPlayer(id, out var found) ? found : null)
                .Where(found => found != null)
                .Select(player =>
                {
                    return new PlayerInfo
                    {
                        playerIndex = player.PlayerIndex,
                        displayName = player.DisplayName,
                        displayColor = GetPlayerColor(player.ConnectionId),
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
            _roomMatchRecords.Clear();
            if (message.matchRecords != null) _roomMatchRecords.AddRange(message.matchRecords);
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
            _roomMatchRecords.Clear();
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
