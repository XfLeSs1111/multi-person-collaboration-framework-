using System;

namespace Socket.Multiplayer
{
    /// <summary>
    /// The shared waiting room every connected player belongs to before creating or
    /// joining a real room. Must be a non-empty Guid: MatchInterestManagement treats
    /// Guid.Empty as invisible to everyone, so a lobby player with an empty matchId
    /// would not even see their own object.
    /// Lives in its own file so pure-logic CI can compile RoomRegistry without the
    /// Unity-heavy RoomMessages.cs.
    /// </summary>
    public static class LobbyRoom
    {
        public static readonly Guid Id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    }
}
