using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// FrameThrottle contract: retry allowed only once the interval has
    /// elapsed since the last attempt; a never-tried state always retries.
    /// </summary>
    public sealed class FrameThrottleTests
    {
        [Fact]
        public void ShouldRetry_SameFrame_False()
        {
            Assert.False(FrameThrottle.ShouldRetry(100, 100, 60));
        }

        [Fact]
        public void ShouldRetry_BeforeInterval_False()
        {
            Assert.False(FrameThrottle.ShouldRetry(100, 159, 60));
        }

        [Fact]
        public void ShouldRetry_AtInterval_True()
        {
            Assert.True(FrameThrottle.ShouldRetry(100, 160, 60));
        }

        [Fact]
        public void ShouldRetry_NeverTried_True()
        {
            Assert.True(FrameThrottle.ShouldRetry(-1000, 0, 60));
        }
    }
}
