using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class NetworkRoomState : NetworkBehaviour
    {
        [SyncVar] private string roomIdText;
        [SyncVar] private string roomName;
        [SyncVar] private int playerCount;
        [SyncVar] private int maxPlayers;
        [SyncVar] private RoomPhase phase = RoomPhase.Lobby;

        public string RoomName => roomName;
        public int PlayerCount => playerCount;
        public int MaxPlayers => maxPlayers;
        public RoomPhase Phase => phase;
        public Guid RoomId => Guid.TryParse(roomIdText, out var value) ? value : Guid.Empty;

        [Server]
        public void ServerRefresh(RoomRegistry.Room room)
        {
            if (room == null) return;
            roomIdText = room.Id.ToString();
            roomName = room.Name;
            playerCount = room.PlayerIds.Count;
            maxPlayers = room.MaxPlayers;
            phase = room.Phase;
        }

        [Server]
        public void ServerAssignRoom(Guid roomId)
        {
            roomIdText = roomId.ToString();
            GetComponent<NetworkMatch>().matchId = roomId;
        }
    }
}
