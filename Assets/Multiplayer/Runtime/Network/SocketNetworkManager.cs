using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class SocketNetworkManager : NetworkManager
    {
        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private NetworkPlayer playerPrefabOverride;
        [SerializeField] private NetworkRoomState roomStatePrefab;
        [SerializeField] private NetworkRoomChat roomChatPrefab;

        private bool _gameStarted;
        private RoomPhase _currentPhase = RoomPhase.Lobby;
        private NetworkRoomState _roomState;
        private NetworkRoomChat _roomChat;

        public MultiplayerConfig Config => config;
        public RoomPhase CurrentPhase => _currentPhase;
        public int ConnectedPlayerCount => numPlayers;
        public string RoomName => config == null ? "Socket Room" : config.roomName;

        public void ConfigureGeneratedAssets(
            MultiplayerConfig newConfig,
            NetworkPlayer newPlayerPrefab,
            NetworkRoomState newRoomStatePrefab,
            NetworkRoomChat newRoomChatPrefab)
        {
            config = newConfig;
            playerPrefabOverride = newPlayerPrefab;
            roomStatePrefab = newRoomStatePrefab;
            roomChatPrefab = newRoomChatPrefab;
        }

        public override void Awake()
        {
            base.Awake();
            if (config == null)
                Debug.LogWarning("SocketNetworkManager has no MultiplayerConfig assigned.");

            if (playerPrefabOverride != null)
                playerPrefab = playerPrefabOverride.gameObject;

            if (config != null)
            {
                maxConnections = config.maxPlayers;
                sendRate = config.sendRate;
                networkAddress = config.defaultAddress;
                offlineScene = config.offlineScene;
                onlineScene = config.lobbyScene;
                ApplyPort(config.port);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureRoomState();
            EnsureRoomChat();
            RefreshRoomState();
        }

        public override void OnStopServer()
        {
            _roomState = null;
            _roomChat = null;
            base.OnStopServer();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            if (numPlayers >= maxConnections)
            {
                conn.Disconnect();
                return;
            }

            if (_gameStarted && (config == null || !config.allowLateJoiners))
            {
                conn.Disconnect();
                return;
            }

            base.OnServerConnect(conn);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (_gameStarted && (config == null || !config.allowLateJoiners))
            {
                conn.Disconnect();
                return;
            }

            var start = GetStartPosition();
            var fallbackIndex = numPlayers;
            var instance = start == null
                ? Instantiate(playerPrefab)
                : Instantiate(playerPrefab, start.position, start.rotation);
            if (start == null && config != null)
                instance.transform.position = new Vector3(fallbackIndex * config.spawnSpacing, 0f, 0f);
            NetworkServer.AddPlayerForConnection(conn, instance);
            ServerEnsureLeader(instance.GetComponent<NetworkPlayer>());
            RefreshRoomState();
        }

        [Server]
        public void ServerEnsureLeader(NetworkPlayer joiningPlayer)
        {
            if (joiningPlayer == null) return;
            foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
                if (player.IsLeader) return;
            joiningPlayer.ServerSetLeader(true);
        }

        [Server]
        private void ServerTransferLeader(NetworkPlayer leavingPlayer)
        {
            if (leavingPlayer == null || !leavingPlayer.IsLeader) return;
            leavingPlayer.ServerSetLeader(false);
            foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (player != leavingPlayer)
                {
                    player.ServerSetLeader(true);
                    return;
                }
            }
        }

        public void ApplyPort(ushort value)
        {
            var transport = GetComponent<kcp2k.KcpTransport>();
            if (transport != null)
            {
                transport.Port = value;
                return;
            }

            Debug.LogWarning("KcpTransport was not found; configure the active Mirror transport port manually.");
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

        public bool AreAllPlayersReady()
        {
            if (!NetworkServer.active || config == null || numPlayers < config.minPlayers) return false;
            foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
                if (!player.IsReady) return false;
            return true;
        }

        [Server]
        public void ServerStartGame(NetworkPlayer requester)
        {
            if (_gameStarted || requester == null || !requester.IsLeader || !AreAllPlayersReady()) return;
            var scene = config == null ? string.Empty : config.gameplayScene;
            if (string.IsNullOrWhiteSpace(scene)) return;
            _gameStarted = true;
            SetServerPhase(RoomPhase.Starting);
            ServerChangeScene(scene);
        }

        [Server]
        public void ServerReturnToLobby()
        {
            var scene = config == null ? string.Empty : config.lobbyScene;
            if (string.IsNullOrWhiteSpace(scene)) return;
            _gameStarted = false;
            SetServerPhase(RoomPhase.ReturningToLobby);
            ServerChangeScene(scene);
        }

        [Server]
        private void SetServerPhase(RoomPhase value)
        {
            _currentPhase = value;
            foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
                player.ServerSetPhase(value);
            RefreshRoomState();
        }

        [Server]
        private void RefreshRoomState()
        {
            EnsureRoomState();
            if (_roomState != null) _roomState.ServerRefresh(this);
        }

        [Server]
        private void EnsureRoomState()
        {
            if (_roomState == null) _roomState = FindFirstObjectByType<NetworkRoomState>();
            if (_roomState != null) return;

            if (roomStatePrefab == null)
            {
                Debug.LogError("NetworkRoomState prefab is not assigned.");
                return;
            }
            var stateObject = Instantiate(roomStatePrefab.gameObject);
            _roomState = stateObject.GetComponent<NetworkRoomState>();
            NetworkServer.Spawn(stateObject);
        }

        [Server]
        private void EnsureRoomChat()
        {
            if (_roomChat == null) _roomChat = FindFirstObjectByType<NetworkRoomChat>();
            if (_roomChat != null) return;

            if (roomChatPrefab == null)
            {
                Debug.LogError("NetworkRoomChat prefab is not assigned.");
                return;
            }
            var chatObject = Instantiate(roomChatPrefab.gameObject);
            _roomChat = chatObject.GetComponent<NetworkRoomChat>();
            NetworkServer.Spawn(chatObject);
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            EnsureRoomState();
            EnsureRoomChat();
            var lobby = config != null && sceneName == config.lobbyScene;
            if (lobby)
            {
                _gameStarted = false;
                _currentPhase = RoomPhase.Lobby;
                foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
                {
                    player.ServerSetReady(false);
                    player.ServerSetPhase(RoomPhase.Lobby);
                }
                foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                    interactable.ServerReset();
                RefreshRoomState();
            }
            else if (config != null && sceneName == config.gameplayScene)
            {
                _gameStarted = true;
                SetServerPhase(RoomPhase.InGame);
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            var player = conn == null || conn.identity == null ? null : conn.identity.GetComponent<NetworkPlayer>();
            if (player != null) ServerTransferLeader(player);
            base.OnServerDisconnect(conn);
            RefreshRoomState();
        }
    }
}
