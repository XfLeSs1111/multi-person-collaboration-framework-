using Mirror;
using Mirror.Authenticators;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class SessionOperations : MonoBehaviour
    {
        [SerializeField] private SocketRoomManager networkManager;
        [SerializeField] private UniqueNameAuthenticator nameAuthenticator;
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private string playerName = "Player";

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
            if (nameAuthenticator == null) nameAuthenticator = FindFirstObjectByType<UniqueNameAuthenticator>();
        }

        public void SetServerAddress(string value) => serverAddress = value;
        public void SetPlayerName(string value) => playerName = value;
        public string ServerAddress => serverAddress;
        public string PlayerName => playerName;

        public void StartHost()
        {
            ApplyIdentity();
            if (networkManager != null) networkManager.StartHost();
        }

        public void StartClient()
        {
            ApplyIdentity();
            if (networkManager != null) networkManager.StartClient(serverAddress);
        }

        public void StopSession()
        {
            if (NetworkServer.active && NetworkClient.active) networkManager.StopHost();
            else if (NetworkClient.active) networkManager.StopClient();
            else if (NetworkServer.active) networkManager.StopServer();
        }

        private void ApplyIdentity()
        {
            ClientSessionOptions.PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            if (nameAuthenticator != null)
                nameAuthenticator.playerName = ClientSessionOptions.PlayerName;
        }
    }
}
