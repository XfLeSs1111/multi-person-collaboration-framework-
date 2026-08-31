using System.Linq;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [AddComponentMenu("Socket/Network Room Manager")]
    public sealed class SocketRoomManager : NetworkRoomManager
    {
        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private NetworkRoomState roomStatePrefab;
        [SerializeField] private NetworkRoomChat roomChatPrefab;

        private NetworkRoomState _roomState;
        private NetworkRoomChat _roomChat;

        public MultiplayerConfig Config => config;
        public string RoomName => config == null ? "Socket Room" : config.roomName;
        public int ConnectedPlayerCount => roomSlots.Count;
        public RoomPhase CurrentPhase => Utils.IsSceneActive(GameplayScene) ? RoomPhase.InGame : RoomPhase.Lobby;

        public override void Awake()
        {
            base.Awake();
            // PcRoomHud renders the room controls; Mirror's built-in IMGUI room panel is disabled.
            showRoomGUI = false;

            if (config == null)
            {
                Debug.LogWarning("SocketRoomManager has no MultiplayerConfig assigned.", this);
                return;
            }

            maxConnections = config.maxPlayers;
            minPlayers = config.minPlayers;
            sendRate = config.sendRate;
            networkAddress = config.defaultAddress;
            offlineScene = config.offlineScene;
            onlineScene = config.lobbyScene;
            RoomScene = config.lobbyScene;
            GameplayScene = config.gameplayScene;
            ApplyPort(config.port);
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

        // ---- Leader election -------------------------------------------------

        [Server]
        public void ServerEnsureLeader(SocketRoomPlayer joiningPlayer)
        {
            if (joiningPlayer == null) return;
            foreach (var player in roomSlots)
            {
                if (player == null) continue;
                if (player is SocketRoomPlayer roomPlayer && roomPlayer.IsLeader)
                    return;
            }
            joiningPlayer.ServerSetLeader(true);
        }

        [Server]
        private void ServerTransferLeader(SocketRoomPlayer leavingPlayer)
        {
            if (leavingPlayer == null || !leavingPlayer.IsLeader) return;
            leavingPlayer.ServerSetLeader(false);
            foreach (var player in roomSlots)
            {
                if (player == null || player == leavingPlayer) continue;
                if (player is SocketRoomPlayer roomPlayer)
                {
                    roomPlayer.ServerSetLeader(true);
                    return;
                }
            }
        }

        // ---- Room flow -------------------------------------------------------

        [Server]
        public void TryStartGame(NetworkConnectionToClient requester)
        {
            if (requester != null && !Utils.IsSceneActive(RoomScene)) return;
            if (requester != null)
            {
                var leader = FindRoomPlayer(requester);
                if (leader == null || !leader.IsLeader) return;
            }
            var required = config == null ? 1 : Mathf.Max(1, config.minPlayers);
            if (roomSlots.Count < required) return;
            if (roomSlots.Any(p => p == null || !p.readyToBegin)) return;
            ServerChangeScene(GameplayScene);
        }

        [Server]
        public void ServerReturnToLobby(NetworkConnectionToClient requester)
        {
            if (requester == null || !Utils.IsSceneActive(GameplayScene)) return;
            var leader = FindRoomPlayer(requester);
            if (leader == null || !leader.IsLeader) return;
            ServerChangeScene(RoomScene);
        }

        [Server]
        private SocketRoomPlayer FindRoomPlayer(NetworkConnectionToClient conn)
        {
            if (conn == null) return null;
            if (conn.identity != null && conn.identity.TryGetComponent<SocketRoomPlayer>(out var identityRoomPlayer))
                return identityRoomPlayer;
            foreach (var owned in conn.owned)
                if (owned != null && owned.TryGetComponent<SocketRoomPlayer>(out var ownedRoomPlayer))
                    return ownedRoomPlayer;
            return null;
        }

        // ---- Room state / chat ----------------------------------------------

        [Server]
        public override void OnRoomStartServer()
        {
            base.OnRoomStartServer();
            EnsureRoomState();
            EnsureRoomChat();
            RefreshRoomState();
        }

        public override void OnRoomStopServer()
        {
            base.OnRoomStopServer();
            _roomState = null;
            _roomChat = null;
        }

        public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
        {
            // NOTE: the base OnServerDisconnect already removed the leaving player from
            // roomSlots before this hook runs, so resolve the room player from the
            // connection's owned objects (KeepAuthority keeps the room player owned).
            var roomPlayer = FindRoomPlayer(conn);
            if (roomPlayer != null) ServerTransferLeader(roomPlayer);
            base.OnRoomServerDisconnect(conn);
            RefreshRoomState();
        }

        public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
        {
            var start = GetStartPosition();
            if (start != null) return Instantiate(playerPrefab, start.position, start.rotation);
            var index = roomSlots.Count;
            var spacing = config == null ? 2f : config.spawnSpacing;
            return Instantiate(playerPrefab, new Vector3(index * spacing, 0f, 0f), Quaternion.identity);
        }

        public override bool OnRoomServerSceneLoadedForPlayer(NetworkConnectionToClient conn, GameObject roomPlayer, GameObject gamePlayer)
        {
            if (roomPlayer == null || gamePlayer == null) return false;
            var rp = roomPlayer.GetComponent<SocketRoomPlayer>();
            var gp = gamePlayer.GetComponent<NetworkPlayer>();
            if (rp != null && gp != null) gp.SetIdentity(rp.DisplayName, rp.DisplayColor, rp.IsLeader);
            return true;
        }

        public override void OnRoomServerSceneChanged(string sceneName)
        {
            base.OnRoomServerSceneChanged(sceneName);
            EnsureRoomState();
            EnsureRoomChat();
            if (sceneName == RoomScene)
            {
                // Returning to the lobby: release any interaction lease held by the game players.
                foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                    interactable.ServerReset();
            }
            RefreshRoomState();
        }

        // Auto-start is driven from ReadyStatusChanged (fires on EVERY ready-state change)
        // rather than the base's allPlayersReady false->true flip, which calls back only
        // once: any single declined TryStartGame would permanently deadlock auto-start.
        public override void ReadyStatusChanged()
        {
            // Maintain the base's allPlayersReady bookkeeping. Its flip then invokes the
            // suppressed OnRoomServerPlayersReady below (no-op), so no double-start.
            base.ReadyStatusChanged();
            if (config != null && config.autoStartWhenAllReady && Utils.IsSceneActive(RoomScene))
                TryStartGame(null);
        }

        public override void OnRoomServerPlayersReady()
        {
            // Suppress the base default (ServerChangeScene(GameplayScene)) and the flip-only
            // path; auto-start is self-counted in ReadyStatusChanged instead.
        }

        // ---- State/chat lifecycle helpers ------------------------------------

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
                Debug.LogError("SocketRoomManager: roomStatePrefab is not assigned.", this);
                return;
            }
            var stateObject = Instantiate(roomStatePrefab.gameObject);
            DontDestroyOnLoad(stateObject);
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
                Debug.LogError("SocketRoomManager: roomChatPrefab is not assigned.", this);
                return;
            }
            var chatObject = Instantiate(roomChatPrefab.gameObject);
            DontDestroyOnLoad(chatObject);
            _roomChat = chatObject.GetComponent<NetworkRoomChat>();
            NetworkServer.Spawn(chatObject);
        }
    }
}
