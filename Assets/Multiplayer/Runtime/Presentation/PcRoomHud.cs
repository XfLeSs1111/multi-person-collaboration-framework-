using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class PcRoomHud : MonoBehaviour
    {
        [SerializeField] private SessionOperations session;
        [SerializeField] private RoomOperations room;
        [SerializeField] private SocketRoomDiscovery discovery;

        private string _address = "localhost";
        private string _playerName = "Player";
        private string _roomName = "Socket Room";
        private string _chatInput = string.Empty;
        private Vector2 _scroll;
        private bool _browseLan;

        private void Awake()
        {
            if (FindObjectsByType<PcRoomHud>(FindObjectsSortMode.None).Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            if (session == null) session = FindFirstObjectByType<SessionOperations>();
            if (room == null) room = FindFirstObjectByType<RoomOperations>();
            if (discovery == null) discovery = FindFirstObjectByType<SocketRoomDiscovery>();
        }

        private void OnGUI()
        {
            var manager = NetworkManager.singleton as SocketRoomManager;
            // MultiplayerUi is the primary interface now; this panel is an opt-in
            // debug fallback toggled by MultiplayerConfig.useLegacyImGuiHud.
            if (GetComponent<MultiplayerUi>() != null && manager != null && manager.Config != null && !manager.Config.useLegacyImGuiHud)
                return;
            GUILayout.BeginArea(new Rect(16f, 16f, 460f, Screen.height - 32f), GUI.skin.window);
            GUILayout.Label("Socket PC Multiplayer");
            _playerName = GUILayout.TextField(_playerName, 24);
            _address = GUILayout.TextField(_address, 64);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) { ApplySessionFields(); StopBrowsingIfActive(); session?.StartHost(); }
            if (GUILayout.Button("Join")) { ApplySessionFields(); StopBrowsingIfActive(); session?.StartClient(); }
            if (GUILayout.Button("Stop")) session?.StopSession();
            GUILayout.EndHorizontal();

            if (discovery != null && !NetworkClient.active && !NetworkServer.active)
                DrawDiscovery();

            if (manager != null && (NetworkClient.active || NetworkServer.active))
            {
                GUILayout.Space(8f);
                DrawNetworkMetrics(manager);
                if (!string.IsNullOrEmpty(manager.LastError))
                    GUILayout.Label($"Error: {manager.LastError}");
                if (manager.LocalRoomId == LobbyRoom.Id)
                    DrawLobby(manager);
                else
                    DrawRoom(manager);
            }
            GUILayout.EndArea();
        }

        private static void DrawNetworkMetrics(SocketRoomManager manager)
        {
            var metrics = manager.Metrics;
            if (metrics == null) return;
            GUILayout.Label($"Network  RTT {metrics.RoundTripTimeMs:F0} ms  IN {FormatBytes(metrics.IncomingBytesPerSecond)}/s  OUT {FormatBytes(metrics.OutgoingBytesPerSecond)}/s");
        }

        private static string FormatBytes(float bytes)
        {
            if (bytes < 1024f) return $"{bytes:F0} B";
            if (bytes < 1024f * 1024f) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        private void DrawDiscovery()
        {
            GUILayout.Space(8f);
            if (_browseLan)
            {
                var servers = discovery.Servers;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"LAN servers ({servers.Count})");
                if (GUILayout.Button("Scan", GUILayout.Width(60f))) discovery.BroadcastDiscoveryRequest();
                GUILayout.EndHorizontal();
                foreach (var server in servers)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{server.roomName}  {server.playerCount}/{server.maxPlayers}", GUILayout.Width(280f));
                    if (GUILayout.Button("Join", GUILayout.Width(60f)))
                    {
                        StopBrowsingIfActive();
                        session?.JoinRoom(server);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button(_browseLan ? "Stop Browsing" : "Browse LAN"))
            {
                _browseLan = !_browseLan;
                if (_browseLan) discovery.StartBrowsing();
                else discovery.StopBrowsing();
            }
        }

        private void DrawLobby(SocketRoomManager manager)
        {
            GUILayout.Label("Lobby");
            _roomName = GUILayout.TextField(_roomName, 32);
            if (GUILayout.Button("Create Room")) room?.CreateRoom(_roomName);

            GUILayout.Space(6f);
            GUILayout.Label($"Available rooms ({manager.AvailableRooms.Count})");
                foreach (var roomInfo in manager.AvailableRooms)
                {
                    GUILayout.BeginHorizontal();
                GUILayout.Label($"{roomInfo.roomName}  {roomInfo.playerCount}/{roomInfo.maxPlayers}  观战 {roomInfo.spectatorCount}/{roomInfo.maxSpectators}  {roomInfo.phase}", GUILayout.Width(370f));
                if (roomInfo.phase == RoomPhase.InGame && GUILayout.Button("观战", GUILayout.Width(60f)))
                    room?.SpectateRoom(roomInfo.roomId);
                else if (!roomInfo.isFull && GUILayout.Button("加入", GUILayout.Width(60f)))
                    room?.JoinRoom(roomInfo.roomId);
                GUILayout.EndHorizontal();
                }
        }

        private void DrawRoom(SocketRoomManager manager)
        {
            var localPlayer = NetworkClient.localPlayer == null
                ? null
                : NetworkClient.localPlayer.GetComponent<NetworkPlayer>();
            var isLeader = localPlayer != null && localPlayer.IsLeader;
            var isReady = localPlayer != null && localPlayer.IsReady;
            var isSpectator = localPlayer != null && localPlayer.IsSpectator;

            GUILayout.Label($"Room: {manager.RoomName}");
            var playerCount = 0;
            var spectatorCount = 0;
            foreach (var player in manager.CurrentPlayers)
            {
                if (player.isSpectator) spectatorCount++;
                else playerCount++;
            }
            GUILayout.Label($"Players: {playerCount}/{FindRoomMax(manager)}  Spectators: {spectatorCount}");
            GUILayout.Label($"Phase: {manager.CurrentPhase}");
            GUILayout.BeginHorizontal();
            if (!isSpectator && GUILayout.Button(isReady ? "Unready" : "Ready")) room?.ToggleReady();
            if (!isSpectator && isLeader && manager.CurrentPhase == RoomPhase.Lobby && GUILayout.Button("Start")) room?.StartGame();
            if (isLeader && manager.CurrentPhase == RoomPhase.InGame && GUILayout.Button("Lobby")) room?.ReturnToLobby();
            GUILayout.EndHorizontal();

            if (GUILayout.Button(isLeader ? "Cancel Room" : "Leave Room"))
            {
                if (isLeader) room?.CancelRoom();
                else room?.LeaveRoom();
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(130f));
            foreach (var player in manager.CurrentPlayers)
                GUILayout.Label($"{(player.isLeader ? "[Leader] " : string.Empty)}{player.displayName}  {(player.ready ? "Ready" : "Not Ready")}");
            GUILayout.EndScrollView();

            DrawGomoku(manager, isSpectator);

            var chat = FindFirstObjectByType<NetworkRoomChat>();
            if (chat == null) return;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(120f));
            foreach (var message in chat.messages)
                GUILayout.Label($"{message.sender}: {message.body}");
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput, 200);
            if (GUILayout.Button("Send", GUILayout.Width(60f)))
            {
                chat.CmdSend(_chatInput);
                _chatInput = string.Empty;
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawGomoku(SocketRoomManager manager, bool isSpectator)
        {
            if (manager.CurrentPhase != RoomPhase.InGame) return;
            var match = FindGomokuMatch(manager.LocalRoomId);
            if (match == null || match.BoardSize <= 0) return;

            GUILayout.Label($"五子棋  {match.Turn} 手  {(match.Result == GomokuResult.None ? (match.CurrentSeat == 0 ? "黑棋回合" : "白棋回合") : $"结果：{match.Result}")}");
            var cellSize = Mathf.Clamp((Screen.width - 80f) / match.BoardSize, 22f, 34f);
            for (var row = 0; row < match.BoardSize; row++)
            {
                GUILayout.BeginHorizontal();
                for (var column = 0; column < match.BoardSize; column++)
                {
                    var index = row * match.BoardSize + column;
                    var cell = match.GetCell(index);
                    var label = cell == GomokuCell.Black ? "X" : cell == GomokuCell.White ? "O" : "+";
                    if (!isSpectator && match.Phase == MatchPhase.Active && cell == GomokuCell.Empty && GUILayout.Button(label, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                        match.CmdPlaceStone(index);
                    else
                        GUILayout.Label(label, GUILayout.Width(cellSize), GUILayout.Height(cellSize));
                }
                GUILayout.EndHorizontal();
            }
        }

        private static NetworkGomokuMatch FindGomokuMatch(System.Guid roomId)
        {
            foreach (var match in FindObjectsByType<NetworkGomokuMatch>(FindObjectsSortMode.None))
                if (match != null && match.MatchId == roomId)
                    return match;
            return null;
        }

        private static int FindRoomMax(SocketRoomManager manager)
        {
            foreach (var room in manager.AvailableRooms)
                if (room.roomId == manager.LocalRoomId)
                    return room.maxPlayers;
            return manager.Config == null ? manager.maxConnections : manager.Config.EffectiveMaxPlayers;
        }

        private void ApplySessionFields()
        {
            if (session == null) return;
            session.SetPlayerName(_playerName);
            session.SetServerAddress(_address);
        }

        private void StopBrowsingIfActive()
        {
            if (discovery == null || !discovery.IsBrowsing) return;
            _browseLan = false;
            discovery.StopBrowsing();
        }
    }
}
