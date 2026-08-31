using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class NetworkRoomState : NetworkBehaviour
    {
        [SyncVar] private string roomName;
        [SyncVar] private int playerCount;
        [SyncVar] private int maxPlayers;
        [SyncVar] private RoomPhase phase = RoomPhase.Lobby;

        public string RoomName => roomName;
        public int PlayerCount => playerCount;
        public int MaxPlayers => maxPlayers;
        public RoomPhase Phase => phase;

        [Server]
        public void ServerRefresh(SocketRoomManager manager)
        {
            if (manager == null) return;
            roomName = manager.RoomName;
            playerCount = manager.ConnectedPlayerCount;
            maxPlayers = manager.Config == null ? manager.maxConnections : manager.Config.maxPlayers;
            phase = manager.CurrentPhase;
        }
    }
}
