#if UNITY_EDITOR
using NUnit.Framework;

namespace Socket.Multiplayer.Tests
{
    public sealed class RateLimiterTests
    {
        [Test]
        public void TokenBucketAllowsBurstThenBlocksUntilRefill()
        {
            var bucket = new TokenBucket(4, 2, 0);
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsTrue(bucket.TryConsume(0));
            Assert.IsFalse(bucket.TryConsume(0));
            Assert.IsFalse(bucket.TryConsume(0.4));
            Assert.IsTrue(bucket.TryConsume(0.5)); // one token refilled at 2/s
        }

        [Test]
        public void TokenBucketNeverRefundsTimeGoingBackwards()
        {
            var bucket = new TokenBucket(1, 1, 10d);
            Assert.IsTrue(bucket.TryConsume(10d));
            Assert.IsFalse(bucket.TryConsume(9d));
            Assert.IsFalse(bucket.TryConsume(10.5d));
            Assert.IsTrue(bucket.TryConsume(11d));
        }

        [Test]
        public void RateLimiterSeparatesKindsAndConnections()
        {
            var limiter = new ConnectionRateLimiter(2f, 2f, 2f, 2f);
            for (var i = 0; i < 4; i++) Assert.IsTrue(limiter.TryAcquire(1, RateLimitKind.Chat, 0d));
            Assert.IsFalse(limiter.TryAcquire(1, RateLimitKind.Chat, 0d));

            Assert.IsTrue(limiter.TryAcquire(1, RateLimitKind.Interact, 0d));
            Assert.IsTrue(limiter.TryAcquire(2, RateLimitKind.Chat, 0d));
        }

        [Test]
        public void RateLimiterForgetClearsExhaustedBuckets()
        {
            var limiter = new ConnectionRateLimiter(2f, 2f, 2f, 2f);
            for (var i = 0; i < 4; i++) Assert.IsTrue(limiter.TryAcquire(7, RateLimitKind.RoomOperation, 0d));
            Assert.IsFalse(limiter.TryAcquire(7, RateLimitKind.RoomOperation, 0d));

            limiter.Forget(7);

            Assert.IsTrue(limiter.TryAcquire(7, RateLimitKind.RoomOperation, 0d));
        }

        [Test]
        public void RateLimiterRefillsOverTime()
        {
            var limiter = new ConnectionRateLimiter(4f, 4f, 4f, 4f);
            for (var i = 0; i < 8; i++) Assert.IsTrue(limiter.TryAcquire(3, RateLimitKind.GameCommand, 0d));
            Assert.IsFalse(limiter.TryAcquire(3, RateLimitKind.GameCommand, 0d));
            Assert.IsTrue(limiter.TryAcquire(3, RateLimitKind.GameCommand, 0.25d)); // 4/s -> one token per 0.25s
        }
    }
}
#endif
