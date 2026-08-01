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

        // F150: band ranges must not overlap — a frequency maps to one band.
        [Fact]
        public void Bands_DoNotOverlap()
        {
            for (int i = 0; i + 1 < DialConfig.Bands.Length; i++)
            {
                Assert.True(DialConfig.Bands[i].RangeMax <= DialConfig.Bands[i + 1].RangeMin,
                    $"band {i} max {DialConfig.Bands[i].RangeMax} > band {i + 1} min {DialConfig.Bands[i + 1].RangeMin}");
            }
        }

        // F151: interpolation between AngleStart and the first reference point.
        [Fact]
        public void AngleToFrequency_BetweenStartAndFirstPoint_Interpolates()
        {
            float f = DialConfig.AngleToFrequency(15f, 3);
            Assert.True(f > 720f && f < 740f, $"expected interpolation, got {f}");
        }
    }
}
