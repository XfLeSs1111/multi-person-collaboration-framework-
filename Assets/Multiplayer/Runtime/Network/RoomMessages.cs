using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    // Room protocol for the single-scene multi-room model (M2). Modeled on Mirror's
    // Examples/MultipleMatches MatchMessages, extended with P1 room features:
    // room name, phase, leader, and return-to-lobby.

    /// <summary>Request sent client -> server.</summary>
    public struct ServerRoomMessage : NetworkMessage
    {
        public ServerRoomOperation serverRoomOperation;
        public Guid roomId;
        public string roomName; // only meaningful for Create
        public bool ready; // only meaningful for Ready
    }

    /// <summary>Response sent server -> client.</summary>
    public struct ClientRoomMessage : NetworkMessage
    {
        public ClientRoomOperation clientRoomOperation;
        public Guid roomId;
        public RoomPhase phase;
        public string error;
        // Structured counterpart of <see cref="error"/> (M3-S.6). The string is kept
        // for logs; clients should switch on the code for user-facing messages.
        public MultiplayerErrorCode errorCode;
        public RoomInfo[] roomInfos;
        public PlayerInfo[] playerInfos;
        // Finished-match history of the room this connection sits in (config-bounded).
        public MatchRecordInfo[] matchRecords;
    }

    [Serializable]
    public struct RoomInfo
    {
        public Guid roomId;
        public string roomName;
        public int playerCount;
        public int maxPlayers;
        public RoomPhase phase;
        public int spectatorCount;
        public int maxSpectators;
        public bool isFull => playerCount >= maxPlayers;
    }

    [Serializable]
    public struct PlayerInfo
    {
        public int playerIndex;
        public string displayName;
        public Color32 displayColor;
        public bool ready;
        public bool isLeader;
        public bool isSpectator;
        public Guid roomId;
    }

    public enum ServerRoomOperation : byte
    {
        None,
        Create,
        Cancel,
        Join,
        Leave,
        Ready,
        Start,
        JoinAsSpectator,
        ReturnToLobby
    }

    public enum ClientRoomOperation : byte
    {
        None,
        List,
        Created,
        Cancelled,
        Joined,
        Departed,
        UpdateRoom,
        Started,
        ReturnedToLobby,
        Error
    }
}
