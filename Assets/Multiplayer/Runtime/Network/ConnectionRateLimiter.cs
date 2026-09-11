using System.Collections.Generic;

namespace Socket.Multiplayer
{
    /// <summary>Server command entry points that are rate limited per connection.</summary>
    public enum RateLimitKind
    {
        RoomOperation,
        Chat,
        Interact,
        GameCommand
    }

    /// <summary>
    /// Per-connection token buckets for the server's command entry points (M3-S.5).
    /// Capacity is twice the configured rate so short UI bursts (toggling ready,
    /// switching rooms) pass, while sustained spam is rejected. Pure C# so the
    /// policy is covered by EditMode tests without starting a server.
    /// </summary>
    public sealed class ConnectionRateLimiter
    {
        private readonly Dictionary<long, TokenBucket> buckets = new Dictionary<long, TokenBucket>();
        private readonly float roomPerSecond;
        private readonly float chatPerSecond;
        private readonly float interactPerSecond;
        private readonly float gamePerSecond;

        public ConnectionRateLimiter(float roomPerSecond, float chatPerSecond, float interactPerSecond, float gamePerSecond)
        {
            this.roomPerSecond = Clamp(roomPerSecond);
            this.chatPerSecond = Clamp(chatPerSecond);
            this.interactPerSecond = Clamp(interactPerSecond);
            this.gamePerSecond = Clamp(gamePerSecond);
        }

        public bool TryAcquire(int connectionId, RateLimitKind kind, double now)
        {
            var key = Key(connectionId, kind);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                var rate = RateFor(kind);
                bucket = new TokenBucket(rate * 2d, rate, now);
                buckets.Add(key, bucket);
            }
            return bucket.TryConsume(now);
        }

        /// <summary>
        /// Drops every bucket of a connection. Must be called on disconnect:
        /// Mirror reuses connection ids, and a fresh client must not inherit
        /// the previous client's exhaustion.
        /// </summary>
        public void Forget(int connectionId)
        {
            var stale = new List<long>();
            foreach (var pair in buckets)
                if ((int)(pair.Key >> 8) == connectionId)
                    stale.Add(pair.Key);
            foreach (var key in stale) buckets.Remove(key);
        }

        private float RateFor(RateLimitKind kind)
        {
            switch (kind)
            {
                case RateLimitKind.RoomOperation: return roomPerSecond;
                case RateLimitKind.Chat: return chatPerSecond;
                case RateLimitKind.Interact: return interactPerSecond;
                default: return gamePerSecond;
            }
        }

        private static long Key(int connectionId, RateLimitKind kind) =>
            ((long)connectionId << 8) | (byte)kind;

        private static float Clamp(float value) => value < 0.5f ? 0.5f : value;
    }
}
