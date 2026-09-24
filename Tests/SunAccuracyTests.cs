using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P1 (CELESTIAL_REALISM_PLAN): the Sun is evaluated on TT with nutation,
    /// aberration and true obliquity. Etalons here are INDEPENDENT of the mod
    /// implementation: Meeus Example 22.a (nutation) and the published equinox
    /// /solstice instants. Stellarium snapshot table is pending (see P0).
    /// </summary>
    public sealed class SunAccuracyTests
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>Meeus 2nd ed., Example 22.a: 1987 April 10.0 TD,
        /// JDE = 2446895.5 → Δψ = −3.788″, Δε = +9.443″ (full theory).
        /// The mod's 4-term truncation must stay within 0.1″ of it.</summary>
        [Fact]
        public void NutationLowPrecision_MatchesMeeusExample22a()
        {
            const double jdTt = 2446895.5;
            double t = (jdTt - 2451545.0) / 36525.0;
            double l0 = AstroMath.NormalizeDegrees(280.46646 + 36000.76983 * t + 0.0003032 * t * t);

            AstroMath.NutationLowPrecision(t, l0 * Deg2Rad,
                out double dPsi, out double dEps);

            Assert.InRange(dPsi, -3.888, -3.688);
            Assert.InRange(dEps, 9.343, 9.543);
        }

        [Fact]
        public void NutationLowPrecision_StaysWithinMainTermBounds()
        {
            // Δψ главный член −17.20″sinΩ: за 18.6-летний цикл |Δψ| ≤ ~20″,
            // |Δε| ≤ ~10″. Проверяем свип по 20 годам с шагом 10 суток.
            double jd = 2451545.0;
            double maxPsi = 0.0;
            double maxEps = 0.0;
            for (int i = 0; i < 730; i++)
            {
                double t = (jd - 2451545.0) / 36525.0;
                double l0 = AstroMath.NormalizeDegrees(280.46646 + 36000.76983 * t + 0.0003032 * t * t);
                AstroMath.NutationLowPrecision(t, l0 * Deg2Rad,
                    out double dPsi, out double dEps);
                maxPsi = Math.Max(maxPsi, Math.Abs(dPsi));
                maxEps = Math.Max(maxEps, Math.Abs(dEps));
                jd += 10.0;
            }
            Assert.InRange(maxPsi, 16.0, 20.0);
            Assert.InRange(maxEps, 8.0, 10.5);
        }

        [Fact]
        public void SunRaDec_MarchEquinox2000_DeclinationNearZero()
        {
            // Published instant: 2000-03-20 07:35 UT. Apparent longitude ≈ 0
            // → declination ≈ 0 (tolerance absorbs the low-precision series).
            double jd = AstroMath.ToJulianDay(new DateTime(2000, 3, 20, 7, 35, 0, DateTimeKind.Utc))
                        + SunEphemerisCalibration.DeltaTSeconds / 86400.0;
            AstroMath.SunRaDec(jd, out double raRad, out double decRad);

            Assert.InRange(decRad / Deg2Rad, -0.02, 0.02);
            double raDeg = raRad / Deg2Rad;
            double raFromZero = Math.Min(raDeg, 360.0 - raDeg);
            Assert.InRange(raFromZero, 0.0, 0.05);
        }

        [Fact]
        public void SunRaDec_JuneSolstice1942_DeclinationNearObliquity()
        {
            // Solstice 1942-06-21 (evening UT): the Sun sits at its maximum
            // declination ≈ +23.44° for the whole day.
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 21, 12, 0, 0, DateTimeKind.Utc))
                        + SunEphemerisCalibration.DeltaTSeconds / 86400.0;
            AstroMath.SunRaDec(jd, out _, out double decRad);
            Assert.InRange(decRad / Deg2Rad, 23.40, 23.46);
        }

        [Fact]
        public void SunRaDec_DecemberSolstice1942_DeclinationNearMinusObliquity()
        {
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 12, 22, 0, 0, 0, DateTimeKind.Utc))
                        + SunEphemerisCalibration.DeltaTSeconds / 86400.0;
            AstroMath.SunRaDec(jd, out _, out double decRad);
            Assert.InRange(decRad / Deg2Rad, -23.46, -23.40);
        }

        [Fact]
        public void SunRaDec_TtSplit_MovesRaBySolarMotionOverDeltaT()
        {
            // ΔT = 24 s of the Sun's mean motion (≈0.9856°/day) → ≈0.00027°
            // in longitude. A TT/UT mix-up worth 6′ would show as ~0.1°.
            double jdUt = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            AstroMath.SunRaDec(jdUt, out double raUt, out _);
            AstroMath.SunRaDec(jdUt + SunEphemerisCalibration.DeltaTSeconds / 86400.0,
                out double raTt, out _);

            double delta = Math.Abs(raTt - raUt) / Deg2Rad;
            if (delta > 180.0)
                delta = 360.0 - delta;
            Assert.InRange(delta, 0.0001, 0.0005);
        }

        [Fact]
        public void SunRaDec_NutationAndAberration_ShiftLongitudeByTensOfArcsec()
        {
            // Apparent vs geometric longitude: nutation (|Δψ| ≤ 20″) plus the
            // 20.5″ aberration. Total must stay in the 10–40″ band; a broken
            // sign or a missing term would leave it near zero or double it.
            double jd = AstroMath.ToJulianDay(new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            double t = (jd - 2451545.0) / 36525.0;
            double l0 = AstroMath.NormalizeDegrees(280.46646 + 36000.76983 * t + 0.0003032 * t * t);
            double m = AstroMath.NormalizeDegrees(357.52911 + 35999.05029 * t - 0.0001537 * t * t);
            double mRad = m * Deg2Rad;
            double c = (1.914602 - 0.004817 * t) * Math.Sin(mRad)
                     + 0.019993 * Math.Sin(2.0 * mRad)
                     + 0.000289 * Math.Sin(3.0 * mRad);
            double lambdaTrue = l0 + c;

            AstroMath.SunRaDec(jd + SunEphemerisCalibration.DeltaTSeconds / 86400.0,
                out double raRad, out double decRad);
            double epsTrue = 23.4392911 - 0.0130042 * t;
            double lambdaApp = Math.Atan2(
                Math.Sin(raRad) * Math.Cos(epsTrue * Deg2Rad) + Math.Tan(decRad) * Math.Sin(epsTrue * Deg2Rad),
                Math.Cos(raRad)) / Deg2Rad;
            double shiftArcsec = (lambdaApp - lambdaTrue) * 3600.0;
            Assert.InRange(shiftArcsec, -40.0, -10.0);
        }
    }
}
