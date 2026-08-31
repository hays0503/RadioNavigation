using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class AstroMathTests
    {
        [Fact]
        public void ToJulianDay_J2000_Is2451545()
        {
            var jd = AstroMath.ToJulianDay(new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            Assert.InRange(jd, 2451544.99, 2451545.01);
        }

        [Fact]
        public void GmstDeg_J2000_Is280_4606()
        {
            double gmst = AstroMath.GmstDeg(2451545.0);
            Assert.InRange(gmst, 280.45, 280.47);
        }

        [Fact]
        public void RaDecToEnu_Polaris_At50N_AltAbout50_AzNorth()
        {
            // Полярная RA~37.95° Dec~89.26° (J2000) → на 50N alt≈50, az≈0
            double ra = 37.95 * Math.PI / 180.0;
            double dec = 89.26 * Math.PI / 180.0;
            double lst = 37.95; // H=0 для culmination
            AstroMath.RaDecToEnu(ra, dec, lst, 50.0, out var alt, out var az, out _, out _, out _);
            Assert.InRange(alt * 180 / Math.PI, 50.0, 51.5);
            Assert.InRange(az * 180 / Math.PI, 0, 10); // север ±10
        }

        [Fact]
        public void RaDecToEnu_Canopus_At50N_AlwaysBelowHorizon()
        {
            // Канопус Dec -52.7° на 50N никогда не виден: max alt = 90-50-52.7 = -12.7°
            double ra = 95.0 * Math.PI / 180.0;
            double dec = -52.7 * Math.PI / 180.0;
            // Проверяем culmination (H=0) — максимальный alt
            double lst = 95.0;
            AstroMath.RaDecToEnu(ra, dec, lst, 50.0, out var alt, out _, out _, out _, out _);
            Assert.True(alt < 0, $"Canopus alt {alt*180/Math.PI} should be <0 at 50N");
        }

        [Fact]
        public void SunRaDec_J2000_MatchesMeeusReference()
        {
            // Meeus гл.25, JD 2451545.0: λ≈280.376° → RA≈281.27°, Dec≈−23.03°
            AstroMath.SunRaDec(2451545.0, out var raRad, out var decRad);
            double raDeg = raRad * 180.0 / Math.PI;
            double decDeg = decRad * 180.0 / Math.PI;
            Assert.InRange(raDeg, 281.17, 281.37);
            Assert.InRange(decDeg, -23.08, -22.98);
        }

        [Fact]
        public void SunRaDec_LateAugust2026_WithinAlmanacBounds()
        {
            // 2026-08-26 00:00 UT = JD 2461278.5. Кросс-чек независимой точкой:
            // формула даёт Dec=+0.02° на равноденствие 2026-09-23 00:00 UT
            // (реальное наступление 00:05 UT). RA≈154.75°, Dec≈+10.48°.
            AstroMath.SunRaDec(2461278.5, out var raRad, out var decRad);
            double raDeg = raRad * 180.0 / Math.PI;
            double decDeg = decRad * 180.0 / Math.PI;
            Assert.InRange(raDeg, 154.2, 155.3);
            Assert.InRange(decDeg, 10.2, 10.7);
        }

        [Fact]
        public void SunRaDec_DailyMotion_AboutOneDegree()
        {
            // Среднесуточный сдвиг RA: ~0.94°/день в конце августа,
            // ~1.07°/день у перигелия (январь); диапазон с запасом.
            AstroMath.SunRaDec(2451545.0, out var ra0, out _);
            AstroMath.SunRaDec(2451546.0, out var ra1, out _);
            double delta = (ra1 - ra0) * 180.0 / Math.PI;
            if (delta < 0) delta += 360.0;
            Assert.InRange(delta, 0.90, 1.15);

            AstroMath.SunRaDec(2461278.5, out var ra2, out _);
            AstroMath.SunRaDec(2461279.5, out var ra3, out _);
            double delta2 = (ra3 - ra2) * 180.0 / Math.PI;
            if (delta2 < 0) delta2 += 360.0;
            Assert.InRange(delta2, 0.90, 1.15);
        }

        [Fact]
        public void PlanetRaDec_AllSupportedPlanets_ReturnFiniteCoordinates()
        {
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));

            for (int planetId = 0; planetId < 4; planetId++)
            {
                Assert.True(AstroMath.PlanetRaDec(planetId, jd, out var ra, out var dec));
                Assert.InRange(ra, 0, 2 * Math.PI);
                Assert.InRange(dec, -Math.PI / 2, Math.PI / 2);
                Assert.False(double.IsNaN(ra) || double.IsInfinity(ra));
                Assert.False(double.IsNaN(dec) || double.IsInfinity(dec));
            }
        }

        [Fact]
        public void PlanetRaDec_RejectsUnknownPlanet()
        {
            Assert.False(AstroMath.PlanetRaDec(-1, 2451545.0, out _, out _));
            Assert.False(AstroMath.PlanetRaDec(4, 2451545.0, out _, out _));
        }

        [Fact]
        public void PlanetRaDec_PlanetsHaveNonZeroDeclination()
        {
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            int nonZero = 0;

            for (int planetId = 0; planetId < 4; planetId++)
            {
                AstroMath.PlanetRaDec(planetId, jd, out _, out var dec);
                if (Math.Abs(dec) > 0.001) nonZero++;
            }

            Assert.True(nonZero >= 3, "Orbital inclination and obliquity must affect declination");
        }

        [Fact]
        public void MoonRaDec_ReturnsFiniteCoordinates()
        {
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(AstroMath.MoonRaDec(jd, out var ra, out var dec));
            Assert.InRange(ra, 0, 2 * Math.PI);
            Assert.InRange(dec, -Math.PI / 2, Math.PI / 2);
            Assert.False(double.IsNaN(ra) || double.IsInfinity(ra));
            Assert.False(double.IsNaN(dec) || double.IsInfinity(dec));
        }

        [Fact]
        public void MoonIllumination_NewAndFullMoonAreOpposite()
        {
            double newMoon = AstroMath.MoonIllumination(2451550.25972);
            double fullMoon = AstroMath.MoonIllumination(2451550.25972 + 29.530588853 / 2);
            Assert.InRange(newMoon, 0, 0.01);
            Assert.InRange(fullMoon, 0.99, 1.0);
        }
    }
}
