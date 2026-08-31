using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Running fix from two sun sights (F-Tafel "two observations with
    // Versetzung"): LOP1 -> transfer -> intersect LOP2 -> FIX. The primary
    // correctness criterion is the synthetic test: observations generated
    // from a known true position, algorithm sees only the displaced DR.
    public class SextantRunningFixTests
    {
        private static readonly DateTime SightTime =
            new DateTime(1941, 10, 14, 15, 21, 34, DateTimeKind.Utc);

        private const double TrueLat = 54.0;
        private const double TrueLon = 18.0;
        private const double DrLatOffsetDeg = 5.0 / 60.0;   // 5 NM north
        private const double DrLonOffsetDeg = 10.0 / 60.0;  // 10 NM east

        /// <summary>Sun Hc/Az at an arbitrary position and time (test-side
        /// ephemeris access, same AstroMath the mod uses).</summary>
        private static void SunHcAz(DateTime utc, double latDeg, double lonDeg,
            out double hcDeg, out double azDeg, out double decDeg,
            out double ghaAriesDeg, out double ghaSunDeg)
        {
            double jd = AstroMath.ToJulianDay(utc);
            ghaAriesDeg = RunningFixCalculator.Normalize360(AstroMath.GmstDeg(jd));
            double raRad, decRad;
            AstroMath.SunRaDec(jd, out raRad, out decRad);
            decDeg = decRad * 180.0 / Math.PI;
            ghaSunDeg = RunningFixCalculator.Normalize360(
                ghaAriesDeg - raRad * 180.0 / Math.PI);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, AstroMath.LstDeg(jd, lonDeg),
                latDeg, out altRad, out azRad, out e, out n, out u);
            hcDeg = altRad * 180.0 / Math.PI;
            azDeg = azRad * 180.0 / Math.PI;
        }

        private static SextantObservation ObservationFromTruth(DateTime utc,
            double truthLat, double truthLon, double drLat, double drLon)
        {
            double hc, az, dec, gamma, ghaSun;
            SunHcAz(utc, truthLat, truthLon, out hc, out az, out dec,
                out gamma, out ghaSun);
            // Perfect sight: Ho equals the true computed altitude.
            return RunningFixCalculator.BuildObservation(utc, hc, drLat, drLon);
        }

        // ---- ephemeris fields (F-Tafel structure) --------------------------

        [Fact]
        public void ShaSun_NotZero_AndRoundtrip()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            Assert.NotEqual(0.0, obs.ShaSunDeg, 6);
            double roundtrip = RunningFixCalculator.Normalize360(
                obs.GhaAriesDeg + obs.ShaSunDeg);
            Assert.Equal(obs.GhaSunDeg, roundtrip, 9);
        }

        [Fact]
        public void GhaSun_Equals_GhaAriesMinusRa()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            // Real relation: GHA = norm360(gamma - RA); SHA = norm360(GHA - gamma).
            double sha = RunningFixCalculator.Normalize360(
                obs.GhaSunDeg - obs.GhaAriesDeg);
            Assert.Equal(obs.ShaSunDeg, sha, 9);
        }

        [Fact]
        public void Lha_Equals_GhaSunPlusLambda()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            double lha = RunningFixCalculator.Normalize360(
                obs.GhaSunDeg + obs.DrLonDeg);
            Assert.Equal(lha, obs.LhaDeg, 9);
        }

        [Fact]
        public void Quadrant_Rule_WestFor12_OstFor34()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            if (obs.Quadrant <= 2)
                Assert.True(obs.QuadrantWest);
            else
                Assert.False(obs.QuadrantWest);
            Assert.InRange(obs.Quadrant, 1, 4);
        }

        // ---- LOP direction (mirror traps) -----------------------------------

        [Fact]
        public void Lop_Az0_PositiveIntercept_GoesNorth()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs.AzDeg = 0.0;
            obs.HoDeg = obs.HcDeg + 10.0 / 60.0;
            LineOfPosition lop = RunningFixCalculator.BuildLopAt(obs, 0.0, 0.0);
            Assert.Equal(10.0 / 60.0, lop.TangentLatDeg, 9);
            Assert.Equal(0.0, lop.TangentLonDeg, 9);
        }

        [Fact]
        public void Lop_Az90_PositiveIntercept_GoesEast()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs.HoDeg = obs.HcDeg + 10.0 / 60.0;
            obs.AzDeg = 90.0;
            LineOfPosition lop = RunningFixCalculator.BuildLopAt(obs, 0.0, 0.0);
            Assert.Equal(0.0, lop.TangentLatDeg, 9);
            Assert.Equal(10.0 / 60.0, lop.TangentLonDeg, 9);
        }

        [Fact]
        public void Lop_Az270_PositiveIntercept_GoesWest()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs.HoDeg = obs.HcDeg + 10.0 / 60.0;
            obs.AzDeg = 270.0;
            LineOfPosition lop = RunningFixCalculator.BuildLopAt(obs, 0.0, 0.0);
            Assert.Equal(0.0, lop.TangentLatDeg, 9);
            Assert.Equal(-10.0 / 60.0, lop.TangentLonDeg, 9);
        }

        // ---- longitude conversion uses cos(latitude) ------------------------

        [Fact]
        public void Lop_LongitudeConversion_UsesCosLatitude()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 60.0, 18.0);
            obs.HoDeg = obs.HcDeg + 10.0 / 60.0;
            obs.AzDeg = 90.0;
            LineOfPosition lop = RunningFixCalculator.BuildLopAt(obs, 60.0, 18.0);
            double expectedDeg = 10.0 / (60.0 * Math.Cos(60.0 * Math.PI / 180.0));
            Assert.Equal(18.0 + expectedDeg, lop.TangentLonDeg, 9);
        }

        // ---- Versetzung ------------------------------------------------------

        [Fact]
        public void TranslateLop_CourseEast_ShiftsEast()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs.HoDeg = obs.HcDeg + 10.0 / 60.0;
            obs.AzDeg = 0.0;
            LineOfPosition lop = RunningFixCalculator.BuildLop(obs);
            lop = RunningFixCalculator.TranslateLop(lop, 90.0, 10.0);
            // Shift reference latitude is the tangent point itself (10 NM N).
            double expectedLon = 10.0 / (60.0 * Math.Cos(10.0 / 60.0 * Math.PI / 180.0));
            Assert.Equal(10.0 / 60.0, lop.TangentLatDeg, 9);
            Assert.Equal(expectedLon, lop.TangentLonDeg, 9);
        }

        [Fact]
        public void TranslateLop_CourseNorth_ShiftsNorth()
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs.HoDeg = obs.HcDeg;
            obs.AzDeg = 90.0;
            LineOfPosition lop = RunningFixCalculator.BuildLop(obs);
            lop = RunningFixCalculator.TranslateLop(lop, 0.0, 10.0);
            Assert.Equal(10.0 / 60.0, lop.TangentLatDeg, 9);
            Assert.Equal(0.0, lop.TangentLonDeg, 9);
        }

        // ---- hand-computed intersection --------------------------------------

        [Fact]
        public void Intersect_HandCase_MatchesManualSolution()
        {
            // DR1 = (0,0), LOP1: Az=0, a=+10 -> tangent (0,10) NM, line E-W.
            // DR2 = 10 NM north, LOP2: Az=90, a=+5 -> tangent (5,10) NM, line N-S.
            // Fix = (5,10) NM -> lat 10/60 deg, lon 5/(60*cos) deg.
            // Pure geometry: synthetic azimuths are inconsistent with the real
            // ephemeris, so the refinement pass must not run here.
            SextantObservation obs1 = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs1.HoDeg = obs1.HcDeg + 10.0 / 60.0;
            obs1.AzDeg = 0.0;

            SextantObservation obs2 = RunningFixCalculator.BuildObservation(
                SightTime.AddMinutes(30), 30.0, 10.0 / 60.0, 0.0);
            obs2.HoDeg = obs2.HcDeg + 5.0 / 60.0;
            obs2.AzDeg = 90.0;

            double lat, lon;
            Assert.True(RunningFixCalculator.IntersectLops(
                RunningFixCalculator.BuildLop(obs1),
                RunningFixCalculator.BuildLop(obs2), out lat, out lon));
            Assert.Equal(10.0 / 60.0, lat, 6);
            Assert.Equal(5.0 / (60.0 * Math.Cos(10.0 / 60.0 * Math.PI / 180.0)),
                lon, 6);
        }

        // ---- synthetic running fix (primary criterion) ------------------------

        [Fact]
        public void SyntheticFix_StationaryTruth_ReturnsTruePosition()
        {
            // Truth at anchor; DR displaced 5 NM N / 10 NM E. Algorithm sees
            // only DR + sights; fix must recover the true position.
            DateTime t1 = SightTime;
            DateTime t2 = SightTime.AddMinutes(28);

            SextantObservation obs1 = ObservationFromTruth(t1,
                TrueLat, TrueLon, TrueLat + DrLatOffsetDeg, TrueLon + DrLonOffsetDeg);
            SextantObservation obs2 = ObservationFromTruth(t2,
                TrueLat, TrueLon, TrueLat + DrLatOffsetDeg, TrueLon + DrLonOffsetDeg);

            RunningFixCalculator calc = new RunningFixCalculator();
            calc.Record(obs1);
            calc.Record(obs2);
            double lat, lon;
            Assert.True(calc.CalculateFix(0.0, 0.0, out lat, out lon));

            double errNm = RunningFixCalculator.FixErrorNm(lat, lon, TrueLat, TrueLon);
            Assert.True(errNm < 0.05,
                string.Format("fix {0:F5},{1:F5} err {2:F4} NM", lat, lon, errNm));
        }

        [Fact]
        public void SyntheticFix_MovingTruth_UsesVersetzung()
        {
            // Truth steams 3.5 NM on course 45 over 30 min (7 kn); DR carries
            // the same offset at both sights; the calculator is told the
            // course/speed and must transfer LOP1 itself.
            double courseDeg = 45.0;
            double speedKn = 7.0;
            double dtHours = 0.5;
            double runNm = speedKn * dtHours;
            double dLat = runNm * Math.Cos(courseDeg * Math.PI / 180.0) / 60.0;
            double dLon = runNm * Math.Sin(courseDeg * Math.PI / 180.0) /
                (60.0 * Math.Cos(TrueLat * Math.PI / 180.0));

            DateTime t1 = SightTime;
            DateTime t2 = SightTime.AddHours(dtHours);

            SextantObservation obs1 = ObservationFromTruth(t1,
                TrueLat, TrueLon, TrueLat + DrLatOffsetDeg, TrueLon + DrLonOffsetDeg);
            SextantObservation obs2 = ObservationFromTruth(t2,
                TrueLat + dLat, TrueLon + dLon,
                TrueLat + dLat + DrLatOffsetDeg, TrueLon + dLon + DrLonOffsetDeg);

            RunningFixCalculator calc = new RunningFixCalculator();
            calc.Record(obs1);
            calc.Record(obs2);
            double lat, lon;
            Assert.True(calc.CalculateFix(courseDeg, speedKn, out lat, out lon));

            // Fix corresponds to the second sight's true position.
            double errNm = RunningFixCalculator.FixErrorNm(lat, lon,
                TrueLat + dLat, TrueLon + dLon);
            Assert.True(errNm < 0.05,
                string.Format("fix {0:F5},{1:F5} err {2:F4} NM", lat, lon, errNm));
        }

        // ---- degenerate cases and lifecycle -----------------------------------

        [Fact]
        public void ParallelLops_CannotSolve()
        {
            SextantObservation obs1 = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, 0.0, 0.0);
            obs1.AzDeg = 0.0;
            SextantObservation obs2 = RunningFixCalculator.BuildObservation(
                SightTime.AddMinutes(30), 30.0, 1.0, 0.0);
            obs2.AzDeg = 0.0;

            RunningFixCalculator calc = new RunningFixCalculator();
            calc.Record(obs1);
            calc.Record(obs2);
            double lat, lon;
            Assert.False(calc.CalculateFix(0.0, 0.0, out lat, out lon));
        }

        [Fact]
        public void Record_Sequence_FirstTwo_ThenReplace()
        {
            RunningFixCalculator calc = new RunningFixCalculator();
            Assert.False(calc.CanSolve);

            SextantObservation o1 = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            calc.Record(o1);
            Assert.False(calc.CanSolve);
            Assert.Same(o1, calc.Observation1);

            SextantObservation o2 = RunningFixCalculator.BuildObservation(
                SightTime.AddMinutes(30), 30.0, TrueLat, TrueLon);
            calc.Record(o2);
            Assert.True(calc.CanSolve);
            Assert.Same(o2, calc.Observation2);

            SextantObservation o3 = RunningFixCalculator.BuildObservation(
                SightTime.AddMinutes(60), 30.0, TrueLat, TrueLon);
            calc.Record(o3);
            Assert.Same(o3, calc.Observation2);
            Assert.Same(o1, calc.Observation1);
        }

        [Fact]
        public void Reset_ClearsSeries()
        {
            RunningFixCalculator calc = new RunningFixCalculator();
            calc.Record(RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon));
            calc.Record(RunningFixCalculator.BuildObservation(
                SightTime.AddMinutes(30), 30.0, TrueLat, TrueLon));
            Assert.True(calc.CanSolve);

            calc.Reset();
            Assert.False(calc.CanSolve);
            Assert.Null(calc.Observation1);
            Assert.Null(calc.Observation2);
        }

        [Fact]
        public void SecondIteration_DoesNotWorsenFix()
        {
            DateTime t1 = SightTime;
            DateTime t2 = SightTime.AddMinutes(28);
            SextantObservation obs1 = ObservationFromTruth(t1,
                TrueLat, TrueLon, TrueLat + DrLatOffsetDeg, TrueLon + DrLonOffsetDeg);
            SextantObservation obs2 = ObservationFromTruth(t2,
                TrueLat, TrueLon, TrueLat + DrLatOffsetDeg, TrueLon + DrLonOffsetDeg);

            RunningFixCalculator calc = new RunningFixCalculator();
            calc.Record(obs1);
            calc.Record(obs2);

            double lat1, lon1, lat2, lon2;
            Assert.True(calc.CalculateFix(0.0, 0.0, 1, out lat1, out lon1));
            Assert.True(calc.CalculateFix(0.0, 0.0, 2, out lat2, out lon2));

            double err1 = RunningFixCalculator.FixErrorNm(lat1, lon1, TrueLat, TrueLon);
            double err2 = RunningFixCalculator.FixErrorNm(lat2, lon2, TrueLat, TrueLon);
            Assert.True(err2 <= err1 + 1e-6,
                string.Format("iter1 {0:F5} NM, iter2 {1:F5} NM", err1, err2));
        }

        [Fact]
        public void FixErrorNm_Haversine_Sane()
        {
            // 1 degree of latitude = 60 NM.
            double err = RunningFixCalculator.FixErrorNm(54.0, 18.0, 55.0, 18.0);
            Assert.Equal(60.0, err, 1);
        }

        // ---- crossing angle (geometry quality) --------------------------------

        private static SextantObservation SightWithAzimuth(double azDeg)
        {
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                SightTime, 30.0, TrueLat, TrueLon);
            obs.AzDeg = azDeg;
            return obs;
        }

        [Fact]
        public void CrossingAngle_InGameCase_149Vs165_Is16()
        {
            double angle = RunningFixCalculator.CrossingAngleDeg(
                SightWithAzimuth(149.0), SightWithAzimuth(165.2));
            Assert.Equal(16.2, angle, 6);
        }

        [Fact]
        public void CrossingAngle_WrapsThrough180()
        {
            // 5° and 175°: lines are undirected, so the crossing is 10°.
            double angle = RunningFixCalculator.CrossingAngleDeg(
                SightWithAzimuth(5.0), SightWithAzimuth(175.0));
            Assert.Equal(10.0, angle, 6);
        }

        [Fact]
        public void CrossingAngle_Perpendicular_Is45()
        {
            double angle = RunningFixCalculator.CrossingAngleDeg(
                SightWithAzimuth(20.0), SightWithAzimuth(65.0));
            Assert.Equal(45.0, angle, 6);
        }
    }
}
