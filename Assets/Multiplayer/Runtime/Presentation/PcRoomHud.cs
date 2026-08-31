using System.Text;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class PcRoomHud : MonoBehaviour
    {
        [SerializeField] private SessionOperations session;
        [SerializeField] private RoomOperations room;
        [SerializeField] private NetworkRoomChat chat;
        private string _address = "localhost";
        private string _playerName = "Player";
        private string _chatInput = string.Empty;
        private Vector2 _scroll;

        private void Awake()
        {
            // Guard against duplicate HUDs (e.g. stale copies left in a scene by an older generator).
            if (FindObjectsByType<PcRoomHud>(FindObjectsSortMode.None).Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            if (session == null) session = FindFirstObjectByType<SessionOperations>();
            if (room == null) room = FindFirstObjectByType<RoomOperations>();
        }

        private void OnGUI()
        {
            // Re-resolve every frame: the HUD is on the DontDestroyOnLoad manager object,
            // so its references survive scene changes (Unity's == treats a destroyed object
            // as null, which re-triggers the lookup). room is generated onto the manager
            // object, but this keeps the HUD working for manual setups that add it later.
            if (room == null) room = FindFirstObjectByType<RoomOperations>();
            if (chat == null) chat = FindFirstObjectByType<NetworkRoomChat>();
            GUILayout.BeginArea(new Rect(16f, 16f, 420f, Screen.height - 32f), GUI.skin.window);
            GUILayout.Label("Socket PC Multiplayer");
            _playerName = GUILayout.TextField(_playerName, 24);
            _address = GUILayout.TextField(_address, 64);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) { ApplySessionFields(); session?.StartHost(); }
            if (GUILayout.Button("Join")) { ApplySessionFields(); session?.StartClient(); }
            if (GUILayout.Button("Stop")) session?.StopSession();
            GUILayout.EndHorizontal();

            var manager = NetworkManager.singleton as SocketRoomManager;
            var roomPlayer = NetworkClient.localPlayer == null ? null : NetworkClient.localPlayer.GetComponent<SocketRoomPlayer>();
            var isReady = roomPlayer != null && roomPlayer.readyToBegin;
            if (manager != null && (NetworkClient.active || NetworkServer.active))
            {
                GUILayout.Space(8f);
                GUILayout.Label($"Room: {manager.RoomName}  {manager.ConnectedPlayerCount}/{manager.Config?.maxPlayers ?? manager.maxConnections}");
                GUILayout.Label($"Mode: {manager.CurrentPhase}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isReady ? "Unready" : "Ready")) room?.ToggleReady();
                if (GUILayout.Button("Start")) room?.StartGame();
                if (GUILayout.Button("Lobby")) room?.ReturnToLobby();
                GUILayout.EndHorizontal();
            }

            if (chat != null)
            {
                GUILayout.Space(8f);
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(180f));
                foreach (var message in chat.messages) GUILayout.Label($"{message.sender}: {message.body}");
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
            GUILayout.EndArea();
        }

        private void ApplySessionFields()
        {
            if (session == null) return;
            session.SetPlayerName(_playerName);
            session.SetServerAddress(_address);
        }
    }
}
