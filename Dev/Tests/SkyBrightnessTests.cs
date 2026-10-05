using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P4 sky brightness: moonwash, twilight, haze, faint-first fade.</summary>
    public sealed class SkyBrightnessTests
    {
        [Fact]
        public void Limiting_NewMoonNight_IsZenithLimit()
        {
            Assert.InRange(SkyBrightness.LimitingMagnitude(108.0, 0.0, 45.0), 5.99, 6.01);
        }

        [Fact]
        public void Limiting_FullMoonOverhead_Is3_5()
        {
            // Author decision: mag 3.5 is barely visible in a full moon.
            Assert.InRange(SkyBrightness.LimitingMagnitude(108.0, 1.0, 90.0), 3.4, 3.6);
        }

        [Fact]
        public void Limiting_FullMoonBelowHorizon_DoesNotWashTheSky()
        {
            Assert.InRange(SkyBrightness.LimitingMagnitude(108.0, 1.0, 0.0), 5.99, 6.01);
            Assert.InRange(SkyBrightness.LimitingMagnitude(108.0, 1.0, -10.0), 5.99, 6.01);
        }

        [Fact]
        public void Limiting_Twilight_IsMonotonic()
        {
            double previous = SkyBrightness.LimitingMagnitude(85.0, 0.0, 0.0);
            for (double zenith = 86.0; zenith <= 110.0; zenith += 1.0)
            {
                double limiting = SkyBrightness.LimitingMagnitude(zenith, 0.0, 0.0);
                Assert.True(limiting >= previous - 1e-9,
                    "limiting magnitude must not fall as the Sun sets");
                previous = limiting;
            }
        }

        [Fact]
        public void Limiting_Daylight_CapsAtFiveAndHalfShift()
        {
            // Shift is capped at 5.5 mag, so even noon cannot go below −? above
            // 0.5 limiting (the mesh is hidden by daylight anyway).
            Assert.InRange(SkyBrightness.LimitingMagnitude(60.0, 0.0, 0.0), 0.4, 0.6);
        }

        [Fact]
        public void Visibility_BrightStarsUnaffected_FaintDieFirst()
        {
            const double limiting = 3.5;
            float sirius = SkyBrightness.VisibilityFactor(limiting, -1.46, 0.0);
            float mag3 = SkyBrightness.VisibilityFactor(limiting, 3.0, 0.0);
            float mag5 = SkyBrightness.VisibilityFactor(limiting, 5.5, 0.0);

            Assert.Equal(1f, sirius);
            Assert.InRange(mag3, 0.6f, 0.75f);
            Assert.Equal(0f, mag5);
        }

        [Fact]
        public void Haze_HorizonWorseThanZenith_AndFogMakesItWorse()
        {
            double horizonClear = SkyBrightness.HazeLoss(2.0, 0.0);
            double zenithClear = SkyBrightness.HazeLoss(60.0, 0.0);
            double horizonFog = SkyBrightness.HazeLoss(2.0, 1.0);

            Assert.True(horizonClear > zenithClear);
            Assert.True(horizonFog > horizonClear);
            Assert.True(zenithClear < 0.1);
        }

        [Fact]
        public void MoonShift_ScalesWithIlluminationAndAltitude()
        {
            Assert.Equal(0.0, SkyBrightness.MoonShift(1.0, 0.0));
            Assert.InRange(SkyBrightness.MoonShift(1.0, 90.0), 2.4, 2.6);
            Assert.True(SkyBrightness.MoonShift(0.5, 90.0)
                < SkyBrightness.MoonShift(1.0, 90.0));
        }

        [Fact]
        public void Smoothstep_IsClampedHermite()
        {
            Assert.Equal(0.0, SkyBrightness.Smoothstep(0.0, 1.0, -1.0));
            Assert.Equal(1.0, SkyBrightness.Smoothstep(0.0, 1.0, 2.0));
            Assert.InRange(SkyBrightness.Smoothstep(0.0, 1.0, 0.5), 0.49, 0.51);
        }
    }
}
