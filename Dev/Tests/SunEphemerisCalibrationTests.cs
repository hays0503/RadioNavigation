using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // SUNDRIFT calibration (SEXTANT_PLAN v1.10): the game sun equals the own
    // Meeus ephemeris shifted in time; the solver recovers that shift from a
    // target direction. Static DeltaMinutes is process-global — every test
    // restores it to zero.
    public class SunEphemerisCalibrationTests : IDisposable
    {
        private static readonly DateTime Utc =
            new DateTime(1941, 10, 14, 12, 44, 25, DateTimeKind.Utc);
        private const double Lat = 54.0;
        private const double Lon = 4.0;

        public void Dispose()
        {
            SunEphemerisCalibration.SetDelta(0.0);
        }

        private static double[] OwnDirAt(double deltaMinutes)
        {
            // Mirror the production TT/UT split (P1): LST on UT, coordinates
            // on TT — a target built the old way would bias the solved Δt.
            double jdUt = AstroMath.ToJulianDay(Utc) + deltaMinutes / 1440.0;
            double jdTt = jdUt + SunEphemerisCalibration.DeltaTSeconds / 86400.0;
            double raRad, decRad;
            AstroMath.SunRaDec(jdTt, out raRad, out decRad);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, AstroMath.LstDeg(jdUt, Lon), Lat,
                out altRad, out azRad, out e, out n, out u);
            double len = Math.Sqrt(e * e + n * n + u * u);
            return new[] { e / len, u / len, n / len };
        }

        [Fact]
        public void CalibratedJd_ZeroDelta_EqualsPlainJulianDay()
        {
            SunEphemerisCalibration.SetDelta(0.0);
            Assert.Equal(AstroMath.ToJulianDay(Utc),
                SunEphemerisCalibration.CalibratedJd(Utc), 12);
        }

        [Fact]
        public void CalibratedJd_ShiftsByMinutes()
        {
            SunEphemerisCalibration.SetDelta(6.5);
            Assert.Equal(AstroMath.ToJulianDay(Utc) + 6.5 / 1440.0,
                SunEphemerisCalibration.CalibratedJd(Utc), 12);
        }

        [Fact]
        public void SolveDelta_RecoversKnownShift()
        {
            // Target = own ephemeris at utc + 6.5 min: the solver must find
            // the shift back to within a tenth of a minute.
            double[] target = OwnDirAt(6.5);
            double delta, residual;
            bool ok = SunEphemerisCalibration.SolveDelta(Utc, Lat, Lon,
                target[0], target[1], target[2], out delta, out residual);
            Assert.True(ok, string.Format("residual {0:F4} deg", residual));
            Assert.InRange(delta, 6.4, 6.6);
            Assert.InRange(residual, 0.0, 0.05);
        }

        [Fact]
        public void SolveDelta_ZeroShift_ReturnsNearZero()
        {
            double[] target = OwnDirAt(0.0);
            double delta, residual;
            bool ok = SunEphemerisCalibration.SolveDelta(Utc, Lat, Lon,
                target[0], target[1], target[2], out delta, out residual);
            Assert.True(ok, string.Format("residual {0:F4} deg", residual));
            Assert.InRange(delta, -0.05, 0.05);
        }

        [Fact]
        public void SolveDelta_GarbageTarget_Rejects()
        {
            // A direction the sun never reaches from this position (straight
            // down) cannot be explained by a ±15 min time shift.
            double delta, residual;
            bool ok = SunEphemerisCalibration.SolveDelta(Utc, Lat, Lon,
                0.0, 1.0, 0.0, out delta, out residual);
            Assert.False(ok);
            Assert.True(residual > SunEphemerisCalibration.ResidualAcceptDeg);
        }

        [Fact]
        public void SolveDelta_ZeroTargetVector_Rejects()
        {
            double delta, residual;
            Assert.False(SunEphemerisCalibration.SolveDelta(Utc, Lat, Lon,
                0.0, 0.0, 0.0, out delta, out residual));
        }

        [Fact]
        public void SetDelta_Roundtrip()
        {
            SunEphemerisCalibration.SetDelta(-3.25);
            Assert.Equal(-3.25, SunEphemerisCalibration.DeltaMinutes, 9);
            SunEphemerisCalibration.SetDelta(0.0);
            Assert.Equal(0.0, SunEphemerisCalibration.DeltaMinutes, 9);
        }

        [Fact]
        public void Worksheet_WithCalibration_SelfConsistent()
        {
            // With a calibration applied, a sight of the calibrated sun has a
            // near-zero frozen intercept: the worksheet matches the sky the
            // player measures against. InterceptNm is frozen at build time,
            // so Ho must equal Hc at construction.
            SunEphemerisCalibration.SetDelta(6.5);
            SextantObservation probe = RunningFixCalculator.BuildObservation(
                Utc, 0.0, Lat, Lon);
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                Utc, probe.HcDeg, Lat, Lon);
            Assert.Equal(0.0, obs.InterceptNm, 6);
        }
    }
}
