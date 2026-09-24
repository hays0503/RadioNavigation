using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// CelestialEphemeris routing (SEXTANT_PLAN v2.2, ADR-033 v2.4): the Sun
    /// AND Moon use the calibrated (SUNDRIFT) sky time, the stars use plain
    /// UTC; each body resolves through the right AstroMath/catalog source.
    /// </summary>
    public sealed class CelestialEphemerisTests
    {
        private static readonly DateTime T0 =
            new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void JdFor_SunAndMoonCalibrated_StarPlain()
        {
            Assert.Equal(SunEphemerisCalibration.CalibratedJd(T0),
                CelestialEphemeris.JdFor(CelestialBody.Sun(), T0), 9);
            Assert.Equal(SunEphemerisCalibration.CalibratedJd(T0),
                CelestialEphemeris.JdFor(CelestialBody.Moon(), T0), 9);
            Assert.Equal(AstroMath.ToJulianDay(T0),
                CelestialEphemeris.JdFor(CelestialBody.Star(91262, "Vega"), T0), 9);
        }

        [Fact]
        public void Compute_Sun_MatchesSunRaDec()
        {
            // The Sun is evaluated on TT (UT + ΔT) while its LST stays UT.
            AstroMath.SunRaDec(SunEphemerisCalibration.JdTt(T0),
                out double expectedRa, out double expectedDec);

            Assert.True(CelestialEphemeris.Compute(CelestialBody.Sun(), T0,
                out double ra, out double dec));
            Assert.Equal(expectedRa, ra, 12);
            Assert.Equal(expectedDec, dec, 12);
        }

        [Fact]
        public void Compute_Moon_MatchesMoonRaDec_AtCalibratedJd()
        {
            // The Moon shares the sky time shift: its ephemeris is evaluated
            // at the calibrated jd, not plain UTC.
            double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), T0);
            AstroMath.MoonRaDec(jd, out double expectedRa, out double expectedDec);

            Assert.True(CelestialEphemeris.Compute(CelestialBody.Moon(), T0,
                out double ra, out double dec));
            Assert.Equal(expectedRa, ra, 12);
            Assert.Equal(expectedDec, dec, 12);
        }

        [Fact]
        public void Compute_Star_MatchesPrecessedCatalogRow()
        {
            // The catalog's effective epoch is 1926.0; Compute must return the
            // row precessed to the sight date, never the raw row (P1b).
            const int hip = 91262; // Vega
            double jd = CelestialEphemeris.JdFor(CelestialBody.Star(hip, "Vega"), T0);
            Assert.True(CelestialEphemeris.Compute(CelestialBody.Star(hip, "Vega"), T0,
                out double ra, out double dec));
            Assert.True(CelestialEphemeris.TryGetStarRaDec(hip, jd,
                out double expectedRa, out double expectedDec));
            Assert.Equal(expectedRa, ra, 12);
            Assert.Equal(expectedDec, dec, 12);

            // And it must differ from the raw catalog row (pre-1926 fix it did not).
            Assert.True(CelestialEphemeris.TryGetStarRaDec(hip, out double rawRa, out _));
            Assert.True(Math.Abs(rawRa - ra) > 0.001, "Compute must precess the catalog row");
        }

        [Fact]
        public void Compute_NoneAndUnknownStar_ReturnFalse()
        {
            Assert.False(CelestialEphemeris.Compute(CelestialBody.None(), T0,
                out _, out _));
            Assert.False(CelestialEphemeris.Compute(CelestialBody.Star(-1, "?"), T0,
                out _, out _));
        }

        [Fact]
        public void UsesCalibration_SunAndMoon_NotStar()
        {
            Assert.True(CelestialEphemeris.UsesCalibration(CelestialBody.Sun()));
            Assert.True(CelestialEphemeris.UsesCalibration(CelestialBody.Moon()));
            Assert.False(CelestialEphemeris.UsesCalibration(CelestialBody.Star(1, "x")));
        }

        [Fact]
        public void IsSightableStar_RejectsNoHipSentinel()
        {
            // Hip=0 is the catalog's no-HIP sentinel (17 distinct stars);
            // a HIP-keyed lookup would return the first of them.
            Assert.False(CelestialEphemeris.IsSightableStar(0));
            Assert.False(CelestialEphemeris.IsSightableStar(-1));
            Assert.True(CelestialEphemeris.IsSightableStar(91262));
        }

        [Fact]
        public void Compute_StarHipZero_ReturnsFalse()
        {
            // Regression F1: Hip=0 must not resolve to the first sentinel row
            // (RaDeg 133.534 / DecDeg 48.332) for every no-HIP star.
            Assert.False(CelestialEphemeris.Compute(CelestialBody.Star(0, "HIP 0"), T0,
                out _, out _));
            Assert.False(CelestialEphemeris.TryGetStarRaDec(0, out _, out _));
        }
    }
}
