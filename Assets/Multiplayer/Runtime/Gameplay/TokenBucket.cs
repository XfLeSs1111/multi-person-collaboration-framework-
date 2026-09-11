using System;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Deterministic token bucket used by the server to cap per-connection request
    /// rates (M3-S.5). Pure C# — no UnityEngine/Mirror dependency — so it can be
    /// driven directly from EditMode tests and the future pure-logic CI project.
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly double capacity;
        private readonly double refillPerSecond;
        private double tokens;
        private double lastRefillTime;

        public TokenBucket(double capacity, double refillPerSecond, double now)
        {
            this.capacity = Math.Max(1d, capacity);
            this.refillPerSecond = Math.Max(0.05d, refillPerSecond);
            tokens = this.capacity;
            lastRefillTime = now;
        }

        /// <summary>Consumes one token when available. A clock going backwards never
        /// refills (or refunds) tokens — the baseline only moves forward.</summary>
        public bool TryConsume(double now)
        {
            if (now > lastRefillTime)
            {
                tokens = Math.Min(capacity, tokens + (now - lastRefillTime) * refillPerSecond);
                lastRefillTime = now;
            }
            if (tokens < 1d) return false;
            tokens -= 1d;
            return true;
        }
    }
}
