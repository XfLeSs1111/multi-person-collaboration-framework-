using System;

namespace Socket.Multiplayer
{
    public enum MatchParticipantRole : byte
    {
        Player,
        Spectator,
        Referee
    }

    public sealed class MatchParticipant
    {
        public string StableId { get; }
        public string DisplayName { get; }
        public int SeatIndex { get; }
        public MatchParticipantRole Role { get; }
        public bool IsConnected { get; private set; }

        public MatchParticipant(string stableId, string displayName, int seatIndex, MatchParticipantRole role)
        {
            if (string.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("Stable id is required.", nameof(stableId));
            StableId = stableId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? stableId : displayName;
            SeatIndex = seatIndex;
            Role = role;
            IsConnected = true;
        }

        public void SetConnected(bool value) => IsConnected = value;
    }
}
