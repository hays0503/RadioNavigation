using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Rate-gated temporal-history reset for the PIP cameras. The reset keeps
    /// the ocular sharp on fast mouse turns (the pipeline's temporal history
    /// is what smears bright scene content across the rim); the main view is
    /// untouched.
    ///
    /// The threshold is the load-bearing number and it is pinned from below:
    /// 22 deg/s of probe spin renders a clean frame with no reset at all
    /// (verified live), so the threshold must sit above the fastest
    /// verified-clean rate. A threshold at or below it would change
    /// probe-verified frames and invalidate the probe as a control.
    /// </summary>
    public class SextantTemporalResetTests
    {
        [Theory]
        [InlineData(0d, false)]
        [InlineData(10d, false)]
        [InlineData(22d, false)]
        [InlineData(24.999, false)]
        [InlineData(25d, true)]
        [InlineData(70d, true)]
        [InlineData(900d, true)]
        public void ShouldReset_FiresOnlyAtAndAboveThreshold(double rate, bool expected)
        {
            Assert.Equal(expected, SextantTemporalReset.ShouldReset(rate, true));
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(25d)]
        [InlineData(900d)]
        public void ShouldReset_PrefOffNeverResets(double rate)
        {
            Assert.False(SextantTemporalReset.ShouldReset(rate, false));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        public void ShouldReset_BadInputNeverResets(double rate)
        {
            Assert.True(SextantTemporalReset.ShouldReset(rate, true) == false,
                "rate " + rate + " must never reset");
            Assert.False(SextantTemporalReset.ShouldReset(rate, false));
        }

        [Fact]
        public void Threshold_SitsAboveTheFastestVerifiedCleanRate()
        {
            // 22 deg/s of probe spin is verified clean without any reset; the
            // threshold must stay above it or probe frames change meaning.
            Assert.True(SextantTemporalReset.ResetRateDegPerSec > 22.0,
                "threshold " + SextantTemporalReset.ResetRateDegPerSec +
                " must stay above the verified-clean 22 deg/s probe rate");
        }

        [Fact]
        public void PrefsKey_IsNamed()
        {
            Assert.False(string.IsNullOrEmpty(SextantTemporalReset.PrefsKey));
        }
    }
}
