using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class SocketRoomPlayer : NetworkRoomPlayer
    {
        [SyncVar] public string displayName;
        [SyncVar] public Color32 displayColor = Color.white;
        [SyncVar] private bool leader;

        public string DisplayName => displayName;
        public Color32 DisplayColor => displayColor;
        public bool IsLeader => leader;

        public override void OnStartServer()
        {
            base.OnStartServer();
            displayName = connectionToClient != null && connectionToClient.authenticationData is string authenticatedName
                ? authenticatedName
                : $"Player {netId}";
            displayColor = Color.HSVToRGB((netId * 0.17f) % 1f, 0.7f, 0.95f);
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.ServerEnsureLeader(this);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            CmdSetDisplayName(ClientSessionOptions.PlayerName);
        }

        [Command]
        public void CmdSetDisplayName(string value)
        {
            value = SanitizeName(value);
            if (string.IsNullOrEmpty(value)) return;
            displayName = value;
            if (connectionToClient != null) connectionToClient.authenticationData = value;
        }

        [Command]
        public void CmdRequestStartGame()
        {
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.TryStartGame(connectionToClient);
        }

        [Command]
        public void CmdRequestReturnToLobby()
        {
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.ServerReturnToLobby(connectionToClient);
        }

        [Server]
        public void ServerSetLeader(bool value) => leader = value;

        // R-key Ready toggle for smoke tests. Only the local lobby player has this command;
        // in the game scene the room player is DontDestroyOnLoad but no longer the local player.
        private void Update()
        {
            if (!isLocalPlayer || !isClient) return;
            if (Input.GetKeyDown(KeyCode.R)) CmdChangeReadyState(!readyToBegin);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= 24 ? value : value.Substring(0, 24);
        }
    }
}
