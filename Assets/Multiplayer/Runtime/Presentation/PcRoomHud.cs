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
            if (session == null) session = FindFirstObjectByType<SessionOperations>();
            if (room == null) room = FindFirstObjectByType<RoomOperations>();
        }

        private void OnGUI()
        {
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

            var manager = NetworkManager.singleton as SocketNetworkManager;
            var local = NetworkClient.localPlayer == null ? null : NetworkClient.localPlayer.GetComponent<NetworkPlayer>();
            if (manager != null && (NetworkClient.active || NetworkServer.active))
            {
                GUILayout.Space(8f);
                GUILayout.Label($"Room: {manager.RoomName}  {manager.ConnectedPlayerCount}/{manager.Config?.maxPlayers ?? manager.maxConnections}");
                GUILayout.Label($"Mode: {manager.CurrentPhase}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(local != null && local.IsReady ? "Unready" : "Ready")) room?.ToggleReady();
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
