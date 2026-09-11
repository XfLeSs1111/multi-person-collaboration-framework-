using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class SessionOperations : MonoBehaviour
    {
        [SerializeField] private SocketRoomManager networkManager;
        [SerializeField] private SocketAuthenticator nameAuthenticator;
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private string playerName = "Player";

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
            if (nameAuthenticator == null) nameAuthenticator = FindFirstObjectByType<SocketAuthenticator>();
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
            if (networkManager != null)
            {
                // Reset to the configured port: a previous LAN join may have left the
                // transport on the advertised room's non-default port.
                networkManager.ApplyPort(networkManager.Config == null ? (ushort)7777 : networkManager.Config.port);
                networkManager.StartClient(serverAddress);
            }
        }

        // Join a room picked from the discovery list; the advertised port is applied
        // before connecting so non-default-port hosts work.
        public void JoinRoom(SocketRoomDiscovery.RoomInfo room)
        {
            ApplyIdentity();
            if (networkManager != null) networkManager.StartClient(room);
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
