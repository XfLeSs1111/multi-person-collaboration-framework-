using System;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Server-side admission policy for client input frames.
    /// Sequence ordering rejects delayed packets; the small burst allowance
    /// absorbs transport jitter without allowing unbounded command spam.
    /// </summary>
    public sealed class InputAdmission
    {
        private readonly double minimumInterval;
        private bool hasAcceptedFrame;
        private uint lastSequence;
        private double lastAcceptedTime;

        public InputAdmission(float expectedRate)
        {
            expectedRate = Math.Max(1f, expectedRate);
            minimumInterval = 0.5d / expectedRate;
            lastAcceptedTime = double.NegativeInfinity;
        }

        public bool TryAccept(uint sequence, double now)
        {
            if (hasAcceptedFrame && sequence <= lastSequence) return false;
            if (hasAcceptedFrame && now - lastAcceptedTime < minimumInterval) return false;

            hasAcceptedFrame = true;
            lastSequence = sequence;
            lastAcceptedTime = now;
            return true;
        }
    }
}
