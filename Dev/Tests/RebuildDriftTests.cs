using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 rebuild-drift math: wrapped LST difference and throttle.</summary>
    public sealed class RebuildDriftTests
    {
        [Fact]
        public void AngularDifference_WrapsAcrossNorth()
        {
            Assert.Equal(0.2, RebuildDrift.AngularDifferenceDeg(0.1, 359.9), 9);
            Assert.Equal(-0.2, RebuildDrift.AngularDifferenceDeg(359.9, 0.1), 9);
            Assert.Equal(10.0, RebuildDrift.AngularDifferenceDeg(10.0, 0.0), 9);
            Assert.Equal(180.0, RebuildDrift.AngularDifferenceDeg(180.0, 0.0), 9);
        }

        [Fact]
        public void LstWrap_TinyDrift_DoesNotRebuild()
        {
            // 359.97 → 0.03 is a 0.06° drift, not 359.9°: no rebuild.
            Assert.False(RebuildDrift.LstLatDrifted(0.03, 359.97, 50.0f, 50.0f));
        }

        [Fact]
        public void LstDrift_BeyondThreshold_Rebuilds()
        {
            Assert.True(RebuildDrift.LstLatDrifted(0.15, 359.95, 50.0f, 50.0f));
            Assert.True(RebuildDrift.LstLatDrifted(10.2, 10.0, 50.0f, 50.0f));
        }

        [Fact]
        public void LatitudeDrift_BeyondThreshold_Rebuilds()
        {
            Assert.False(RebuildDrift.LstLatDrifted(10.0, 10.0, 50.005f, 50.0f));
            Assert.True(RebuildDrift.LstLatDrifted(10.0, 10.0, 50.02f, 50.0f));
        }

        [Fact]
        public void RebuildInterval_ThrottlesBelowHalfASecond()
        {
            Assert.False(RebuildDrift.RebuildIntervalElapsed(0f, 0.49f));
            Assert.True(RebuildDrift.RebuildIntervalElapsed(0f, 0.5f));
            Assert.True(RebuildDrift.RebuildIntervalElapsed(0f, 10f));
            // The first rebuild is never starved (last = 0, clock large).
            Assert.True(RebuildDrift.RebuildIntervalElapsed(0f, 1f));
        }
    }
}
