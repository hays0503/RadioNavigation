using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Rate-gated temporal-history reset while the aim swings fast. The reset
    /// targets the VIEW (main) camera first — a 220 deg/s probe spin (PIP
    /// cameras only) renders sharp frames, so the smear follows the main
    /// camera and a PIP-only reset addresses nothing (verified live, then
    /// reverted). The PIP pair still gets the flag in case their own history
    /// ever matters.
    ///
    /// The threshold is pinned from below: probe motion stays under it, so
    /// probe-verified frames never change meaning.
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
        public void Threshold_SitsAboveProbeMotion()
        {
            Assert.True(SextantTemporalReset.ResetRateDegPerSec > 22.0,
                "threshold " + SextantTemporalReset.ResetRateDegPerSec +
                " must stay above probe motion so probe frames never reset");
        }

        [Fact]
        public void PrefsKey_IsNamed()
        {
            Assert.False(string.IsNullOrEmpty(SextantTemporalReset.PrefsKey));
        }
    }
}
