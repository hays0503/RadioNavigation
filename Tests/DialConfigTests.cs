using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class DialConfigTests
    {
        [Fact]
        public void Bands_FiveBands()
        {
            Assert.Equal(5, DialConfig.Bands.Length);
            Assert.Equal(new[] { "I", "II", "III", "IV", "V" }, System.Linq.Enumerable.Select(DialConfig.Bands, b => b.Id));
        }

        [Fact]
        public void AngleToFrequency_BelowAngleStart_ReturnsRangeMin()
        {
            Assert.Equal(150f, DialConfig.AngleToFrequency(-10f, 1));
            Assert.Equal(70f, DialConfig.AngleToFrequency(0f, 0));
        }

        [Fact]
        public void AngleToFrequency_AboveAngleEnd_ReturnsRangeMax()
        {
            Assert.Equal(340f, DialConfig.AngleToFrequency(999f, 1));
            Assert.Equal(1600f, DialConfig.AngleToFrequency(500f, 3));
        }

        [Fact]
        public void AngleToFrequency_ExactReferencePoint_ReturnsFrequency()
        {
            Assert.Equal(200f, DialConfig.AngleToFrequency(71.1f, 1), 2);
        }

        [Fact]
        public void AngleToFrequency_InterpolatesBetweenPoints()
        {
            float f = DialConfig.AngleToFrequency(45f, 1);
            Assert.Equal(173.79f, f, 1);
        }

        [Fact]
        public void AngleToFrequency_InvalidBandIndex_NaN()
        {
            Assert.True(float.IsNaN(DialConfig.AngleToFrequency(50f, 99)));
            Assert.True(float.IsNaN(DialConfig.AngleToFrequency(50f, -1)));
        }

        // Culture note: ToString("F1") is locale-dependent (ru-RU uses ','). Normalize to '.' for stability.
        // Game-side this is a latent issue (see F161 family).
        [Fact]
        public void FormatFrequency_MHzBand_OneDecimal()
        {
            Assert.Equal("100.0 MHz", DialConfig.FormatFrequency(100f, 0).Replace(',', '.'));
        }

        [Fact]
        public void FormatFrequency_KHzBand_RoundedInteger()
        {
            Assert.Equal("266 kHz", DialConfig.FormatFrequency(266f, 1));
            Assert.Equal("174 kHz", DialConfig.FormatFrequency(173.79f, 1));
        }

        [Fact]
        public void FormatFrequency_NaN_ReturnsDashes()
        {
            Assert.Equal("---", DialConfig.FormatFrequency(float.NaN, 1));
        }
    }
}
