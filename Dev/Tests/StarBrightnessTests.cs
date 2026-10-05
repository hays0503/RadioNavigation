using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P3 photometry: Pogson flux law with a square-root alpha compression and
    /// a log-linear size curve, plus the single navigation-star boost.
    /// </summary>
    public sealed class StarBrightnessTests
    {
        [Fact]
        public void Flux_FiveMagnitudes_IsHundredFold()
        {
            double ratio = StarBrightness.Flux(1.0) / StarBrightness.Flux(6.0);
            Assert.InRange(ratio, 99.0, 101.0);
        }

        [Fact]
        public void Alpha_MatchesFluxTable()
        {
            // alpha(m) = 2.512^(-(m+1.5)/2)
            Assert.InRange(StarBrightness.Alpha(-1.5f), 0.995f, 1.000f);
            Assert.InRange(StarBrightness.Alpha(-1f), 0.789f, 0.799f);
            Assert.InRange(StarBrightness.Alpha(0f), 0.496f, 0.506f);
            Assert.InRange(StarBrightness.Alpha(1f), 0.311f, 0.321f);
            Assert.InRange(StarBrightness.Alpha(2f), 0.195f, 0.205f);
            Assert.InRange(StarBrightness.Alpha(3f), 0.121f, 0.131f);
            Assert.InRange(StarBrightness.Alpha(4f), 0.074f, 0.084f);
            Assert.InRange(StarBrightness.Alpha(5f), 0.045f, 0.055f);
            Assert.InRange(StarBrightness.Alpha(6f), 0.027f, 0.037f);
        }

        [Fact]
        public void Alpha_EachMagnitudeStep_Is0631()
        {
            for (int m = -1; m <= 5; m++)
            {
                float ratio = StarBrightness.Alpha(m + 1f) / StarBrightness.Alpha(m);
                Assert.InRange(ratio, 0.626f, 0.636f);
            }
        }

        [Fact]
        public void Size_MatchesCurve()
        {
            Assert.InRange(StarBrightness.SizePx(-1.5f), 19.9f, 20.1f);
            Assert.InRange(StarBrightness.SizePx(0f), 17.3f, 17.5f);
            Assert.InRange(StarBrightness.SizePx(3f), 11.4f, 11.6f);
            Assert.InRange(StarBrightness.SizePx(6f), 1.9f, 2.1f);
        }

        [Fact]
        public void Size_IsMonotonicDecreasing()
        {
            float previous = StarBrightness.SizePx(-2f);
            for (float m = -1.5f; m <= 6.5f; m += 0.5f)
            {
                float size = StarBrightness.SizePx(m);
                Assert.True(size <= previous + 1e-5f, "size must not grow with magnitude");
                previous = size;
            }
        }

        [Fact]
        public void Alpha_ClampsAtBrightEnd()
        {
            Assert.Equal(1f, StarBrightness.Alpha(-5f));
            Assert.Equal(20f, StarBrightness.SizePx(-9f));
        }

        [Fact]
        public void BrightnessFor_NavBoost_IsAboutThreeQuarterMag()
        {
            StarBrightness.BrightnessFor(2f, false, out float plainSize, out float plainAlpha);
            StarBrightness.BrightnessFor(2f, true, out float navSize, out float navAlpha);

            Assert.InRange(navSize / plainSize, 1.29f, 1.31f);
            Assert.InRange(navAlpha / plainAlpha, 1.19f, 1.21f);

            // Flux gain = area × alpha = 1.3² × 1.2 ≈ 2.03 → +0.77 mag.
            double gain = (navSize / plainSize) * (navSize / plainSize) * (navAlpha / plainAlpha);
            double magGain = 2.5 * Math.Log10(gain);
            Assert.InRange(magGain, 0.70, 0.85);
        }

        [Fact]
        public void BrightnessFor_NavAlpha_StaysClamped()
        {
            StarBrightness.BrightnessFor(-1.5f, true, out _, out float alpha);
            Assert.Equal(1f, alpha);
        }
    }
}
