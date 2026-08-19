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
        public void Nutation_Magnitude_Within20Arcsec()
        {
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            AstroMath.NutationDeg(jd, out var dPsi, out var dEps);
            Assert.InRange(Math.Abs(dPsi * 3600), 0, 20);
            Assert.InRange(Math.Abs(dEps * 3600), 0, 20);
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
        public void Precession_SameEpoch_IsIdentity()
        {
            double[] m = new double[9];
            double jd = 2451545.0;
            AstroMath.PrecessionMatrix(jd, jd, m);
            Assert.InRange(m[0], 0.99, 1.01);
            Assert.InRange(m[4], 0.99, 1.01);
            Assert.InRange(m[8], 0.99, 1.01);
        }
    }
}
