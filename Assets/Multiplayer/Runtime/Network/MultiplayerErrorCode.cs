namespace Socket.Multiplayer
{
    /// <summary>
    /// Stable server rejection codes (M3-S.6). The wire protocol is append-only:
    /// never renumber or reuse a value — clients are expected to switch on these
    /// numbers for localized messaging instead of parsing the debug text.
    /// The debug string in <c>ClientRoomMessage.error</c> stays for logs only.
    /// </summary>
    public enum MultiplayerErrorCode : ushort
    {
        None = 0,

        // 1xx — session and authentication
        NotRegistered = 100,
        ProtocolMismatch = 101,
        ConfigMismatch = 102,
        NameTaken = 103,

        // 2xx — room operations
        AlreadyInRoom = 200,
        RoomNotFound = 201,
        RoomFull = 202,
        RoomStarted = 203,
        RoomNotStarted = 204,
        RoomLimitReached = 205,
        NotLeader = 206,
        NotEnoughPlayers = 207,
        NotAllReady = 208,
        AlreadyInLobby = 209,
        SpectatorLimitReached = 210,
        SpectatorNotAllowed = 211,
        SpectatorCannotReady = 212,
        ReadyOnlyInLobby = 213,
        NotInRoom = 214,
        StartFailed = 215,

        // 3xx/4xx — reserved for match-kernel and interaction rejections

        // 9xx — traffic control
        RateLimited = 900,
        UnknownOperation = 999
    }
}
