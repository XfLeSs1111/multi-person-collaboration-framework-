using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class RoomOperations : MonoBehaviour
    {
        [SerializeField] private SocketRoomManager networkManager;

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
        }

        public void CreateRoom(string roomName)
        {
            Send(ServerRoomOperation.Create, Guid.Empty, roomName);
        }

        public void CancelRoom()
        {
            Send(ServerRoomOperation.Cancel, Guid.Empty, string.Empty);
        }

        public void JoinRoom(Guid roomId)
        {
            Send(ServerRoomOperation.Join, roomId, string.Empty);
        }

        public void SpectateRoom(Guid roomId)
        {
            Send(ServerRoomOperation.JoinAsSpectator, roomId, string.Empty);
        }

        public void LeaveRoom()
        {
            Send(ServerRoomOperation.Leave, Guid.Empty, string.Empty);
        }

        public void ToggleReady()
        {
            var local = NetworkClient.localPlayer == null
                ? null
                : NetworkClient.localPlayer.GetComponent<NetworkPlayer>();
            Send(ServerRoomOperation.Ready, Guid.Empty, string.Empty, local == null || !local.IsReady);
        }

        public void SetReady(bool value)
        {
            Send(ServerRoomOperation.Ready, Guid.Empty, string.Empty, value);
        }

        public void StartGame()
        {
            Send(ServerRoomOperation.Start, Guid.Empty, string.Empty);
        }

        public void ReturnToLobby()
        {
            Send(ServerRoomOperation.ReturnToLobby, Guid.Empty, string.Empty);
        }

        private static void Send(ServerRoomOperation operation, Guid roomId, string roomName, bool ready = false)
        {
            if (!NetworkClient.active) return;
            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = operation,
                roomId = roomId,
                roomName = roomName,
                ready = ready
            });
        }
    }
}
