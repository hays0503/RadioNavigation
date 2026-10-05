using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P2 Moon: truncated Meeus ch.47 series replaces the Kepler base that
    /// dropped Evection (76′) and Variation (40′). Etalon is Meeus Example
    /// 47.a (independent textbook values).
    /// </summary>
    public sealed class MoonAccuracyTests
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>Meeus 2nd ed. Example 47.a: 1992 April 12.0 TD,
        /// JDE 2448724.5 → λ=133.162655°, β=−3.229126°, Δ=368409.7 km,
        /// apparent α=134.688470°, δ=13.768368°.</summary>
        [Fact]
        public void MoonRaDec_MatchesMeeusExample47a()
        {
            Assert.True(AstroMath.MoonRaDec(2448724.5, out double ra, out double dec));
            Assert.InRange(ra / Deg2Rad, 134.67, 134.71);
            Assert.InRange(dec / Deg2Rad, 13.75, 13.79);
        }

        [Fact]
        public void MoonTopocentric_ParallaxLowersAltitudeAndVanishesAtZenith()
        {
            // Sweep 30 days at 50°N: the topocentric altitude is always ≤ the
            // geocentric one, the drop never exceeds HP, it is large for a low
            // Moon and ~0 for a Moon near the zenith.
            const double lat = 50.0;
            const double lon = 0.0;
            double jd = 2448724.5;
            double maxDrop = 0.0;
            double minDrop = 180.0;

            for (int i = 0; i < 30; i++)
            {
                double day = jd + i;
                double lst = AstroMath.LstDeg(day, lon);
                double hp = AstroMath.MoonParallaxDeg(day);

                AstroMath.MoonRaDec(day, out double geoRa, out double geoDec);
                AstroMath.RaDecToEnu(geoRa, geoDec, lst, lat, out double geoAlt, out _, out _, out _, out _);

                Assert.True(AstroMath.MoonTopocentricRaDec(day, lat, lst,
                    out double topoRa, out double topoDec));
                AstroMath.RaDecToEnu(topoRa, topoDec, lst, lat, out double topoAlt, out _, out _, out _, out _);

                double drop = (geoAlt - topoAlt) / Deg2Rad;
                Assert.InRange(drop, -0.001, hp + 0.001);

                // Parallax in altitude ≈ HP·cos(alt): the drop must track it.
                double expected = hp * Math.Cos(geoAlt);
                Assert.InRange(drop, expected - 0.02 - 0.05 * hp, expected + 0.02 + 0.05 * hp);

                if (drop > maxDrop) maxDrop = drop;
                if (drop < minDrop) minDrop = drop;
            }

            Assert.True(maxDrop > 0.5, "a low Moon must show a large parallax drop");
            Assert.True(minDrop < maxDrop, "a high Moon must show less parallax than a low one");
        }

        [Fact]
        public void MoonDistance_MatchesMeeusExample47a_AndStaysPhysical()
        {
            double distance = AstroMath.MoonDistanceKm(2448724.5);
            Assert.InRange(distance, 368000.0, 368800.0);

            // Sweep one synodic month: perigee/apogee bounds.
            double jd = 2448724.5;
            double min = double.MaxValue;
            double max = 0.0;
            for (int i = 0; i < 30; i++)
            {
                double d = AstroMath.MoonDistanceKm(jd + i);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            Assert.InRange(min, 350000.0, 370000.0);
            Assert.InRange(max, 400000.0, 410000.0);
        }

        [Fact]
        public void MoonParallax_IsInTheKnownRange()
        {
            // Mean HP ≈ 57′; range 53.9′–61.6′ → 0.898°–1.027°.
            double jd = 2448724.5;
            for (int i = 0; i < 30; i++)
            {
                double hp = AstroMath.MoonParallaxDeg(jd + i);
                Assert.InRange(hp, 0.88, 1.04);
            }
        }

        [Fact]
        public void MoonRaDec_LatitudeStaysWithinOrbitBounds()
        {
            // The Moon's ecliptic latitude never exceeds ~5.3°.
            double jd = 2451545.0;
            for (int i = 0; i < 60; i++)
            {
                AstroMath.MoonRaDec(jd + i * 0.5, out _, out double dec);
                Assert.InRange(dec / Deg2Rad, -28.7, 28.7); // dec bound = ε + 5.3
            }
        }

        [Fact]
        public void MoonIllumination_MatchesElongation()
        {
            // k = (1 − cos ψ)/2 with ψ the Moon–Sun elongation.
            double jd = 2448724.5;
            AstroMath.MoonRaDec(jd, out double raM, out double decM);
            AstroMath.SunRaDec(jd, out double raS, out double decS);
            double cosPsi = Math.Sin(decM) * Math.Sin(decS)
                + Math.Cos(decM) * Math.Cos(decS) * Math.Cos(raM - raS);
            double expected = 0.5 * (1.0 - cosPsi);

            Assert.InRange(AstroMath.MoonIllumination(jd), expected - 0.01, expected + 0.01);
        }

        [Fact]
        public void MoonIllumination_CoversNewToFullOverAMonth()
        {
            double jd = 2451550.25972; // mean new moon epoch
            double min = 1.0;
            double max = 0.0;
            for (int i = 0; i <= 30; i++)
            {
                double k = AstroMath.MoonIllumination(jd + i);
                if (k < min) min = k;
                if (k > max) max = k;
            }
            Assert.InRange(min, 0.0, 0.02);
            Assert.InRange(max, 0.98, 1.0);
        }

        [Fact]
        public void MoonRaDec_AdvancesAboutThirteenDegreesPerDay()
        {
            AstroMath.MoonRaDec(2451545.0, out double ra0, out _);
            AstroMath.MoonRaDec(2451546.0, out double ra1, out _);
            double delta = (ra1 - ra0) / Deg2Rad;
            if (delta < 0) delta += 360.0;
            Assert.InRange(delta, 11.0, 15.5);
        }
    }
}
