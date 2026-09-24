using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SunEphemerisTests : IDisposable
    {
        public void Dispose()
        {
            SunEphemerisCalibration.SetDelta(0.0);
        }

        [Fact]
        public void Compute_ValidInput_OkNormalized()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

            SunEphemeris.SunFix fix = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.True(fix.Ok);
            double len = Math.Sqrt(fix.DirX * fix.DirX + fix.DirY * fix.DirY + fix.DirZ * fix.DirZ);
            Assert.Equal(1.0, len, 9);
        }

        [Fact]
        public void Compute_AltInRange()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

            SunEphemeris.SunFix fix = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.True(fix.AltDeg >= -90f && fix.AltDeg <= 90f);
        }

        [Fact]
        public void Compute_Deterministic()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

            SunEphemeris.SunFix a = SunEphemeris.Compute(54.0, 18.0, utc);
            SunEphemeris.SunFix b = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.Equal(a.DirX, b.DirX);
            Assert.Equal(a.AltDeg, b.AltDeg);
        }

        [Fact]
        public void Compute_MatchesDirectAstroMath()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

            double jdUt = SunEphemerisCalibration.JdUt(utc);
            double jdTt = SunEphemerisCalibration.JdTt(utc);
            double lstDeg = AstroMath.LstDeg(jdUt, 18.0);
            double raRad, decRad;
            AstroMath.SunRaDec(jdTt, out raRad, out decRad);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, 54.0, out altRad, out azRad, out e, out n, out u);
            double len = Math.Sqrt(e * e + n * n + u * u);

            SunEphemeris.SunFix fix = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.True(fix.Ok);
            Assert.Equal(e / len, fix.DirX, 9);
            Assert.Equal(u / len, fix.DirY, 9);
            Assert.Equal(n / len, fix.DirZ, 9);
        }

        [Fact]
        public void Compute_WinterMidnight_BelowHorizon()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            SunEphemeris.SunFix fix = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.True(fix.Ok);
            Assert.True(fix.AltDeg < 0f);
        }

        [Fact]
        public void Compute_SummerNoon_AboveHorizon()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            DateTime utc = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

            SunEphemeris.SunFix fix = SunEphemeris.Compute(54.0, 18.0, utc);

            Assert.True(fix.Ok);
            Assert.True(fix.AltDeg > 0f);
        }
    }
}
