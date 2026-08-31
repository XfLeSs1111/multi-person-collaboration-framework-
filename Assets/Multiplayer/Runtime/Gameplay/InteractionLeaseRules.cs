namespace Socket.Multiplayer
{
    public readonly struct InteractionLeaseResult
    {
        public readonly bool Accepted;
        public readonly string Reason;

        public InteractionLeaseResult(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason;
        }
    }

    public static class InteractionLeaseRules
    {
        public static InteractionLeaseResult TryAcquire(
            bool active,
            uint currentHolder,
            uint requester,
            float distance,
            float maxDistance)
        {
            if (!active) return new InteractionLeaseResult(false, "inactive");
            if (requester == 0) return new InteractionLeaseResult(false, "invalid_requester");
            if (currentHolder != 0 && currentHolder != requester)
                return new InteractionLeaseResult(false, "already_held");
            if (distance > maxDistance) return new InteractionLeaseResult(false, "out_of_range");
            return new InteractionLeaseResult(true, "accepted");
        }

        public static bool CanRelease(uint currentHolder, uint requester)
        {
            return currentHolder != 0 && currentHolder == requester;
        }
    }
}
