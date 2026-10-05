using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P6 cocked hat: three sights publish the triangle centre (author:
    /// "two sights give a rough fix, the third makes it better because there
    /// is more data"), with the spread graded for the card.
    /// </summary>
    public sealed class CockedHatTests
    {
        private const double TrueLat = 54.0;
        private const double TrueLon = 18.0;
        private const double DrLatOffset = 5.0 / 60.0;
        private const double DrLonOffset = 10.0 / 60.0;
        private const double Deg2Rad = Math.PI / 180.0;

        private static readonly DateTime T0 =
            new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);

        private static readonly CelestialBody Vega = CelestialBody.Star(91262, "Vega");
        private static readonly CelestialBody Arcturus = CelestialBody.Star(69673, "Arcturus");
        private static readonly CelestialBody Deneb = CelestialBody.Star(102098, "Deneb");

        private static void BodyHcAz(CelestialBody body, DateTime utc,
            double latDeg, double lonDeg, out double hcDeg)
        {
            double jd = CelestialEphemeris.JdFor(body, utc);
            Assert.True(CelestialEphemeris.ComputeForObserver(body, utc, latDeg, lonDeg,
                out double raRad, out double decRad));
            AstroMath.RaDecToEnu(raRad, decRad, AstroMath.LstDeg(jd, lonDeg), latDeg,
                out double altRad, out _, out _, out _, out _);
            hcDeg = altRad / Deg2Rad;
        }

        /// <summary>Sight taken from the ship's true position, recorded
        /// against a dead-reckoned position 5′N/10′E off it.</summary>
        private static SextantObservation FromTruth(CelestialBody body, DateTime utc,
            double truthLat, double truthLon)
        {
            BodyHcAz(body, utc, truthLat, truthLon, out double hc);
            return RunningFixCalculator.BuildObservation(utc, hc,
                truthLat + DrLatOffset, truthLon + DrLonOffset, body);
        }

        private static void TruthAt(DateTime utc, double courseDeg, double speedKn,
            out double lat, out double lon)
        {
            double hours = (utc - T0).TotalHours;
            double runNm = speedKn * hours;
            lat = TrueLat + runNm * Math.Cos(courseDeg * Deg2Rad) / 60.0;
            lon = TrueLon + runNm * Math.Sin(courseDeg * Deg2Rad)
                / (60.0 * Math.Cos(TrueLat * Deg2Rad));
        }

        [Fact]
        public void ThreeStars_Simultaneous_PublishTheCentreNearTruth()
        {
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Deneb, T0, TrueLat, TrueLon));

            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out CockedHatResult hat));
            Assert.True(hat.Ok);
            Assert.False(hat.UsedBestPairFallback);

            double error = RunningFixCalculator.DistanceNm(hat.FixLatDeg, hat.FixLonDeg,
                TrueLat, TrueLon);
            Assert.True(error < 0.05, "centre error " + error + " NM");
            Assert.True(hat.RadiusNm < 1.0, "radius " + hat.RadiusNm + " NM");
            Assert.True(hat.AreaNm2 > 0.0);
        }

        [Fact]
        public void ThreeSights_Moving_TransferToTheLastTime()
        {
            const double course = 45.0;
            const double speed = 7.0;
            var calc = new RunningFixCalculator();

            for (int i = 0; i < 3; i++)
            {
                DateTime utc = T0.AddMinutes(i * 10);
                double lat, lon;
                TruthAt(utc, course, speed, out lat, out lon);
                CelestialBody body = i == 0 ? Vega : (i == 1 ? Arcturus : Deneb);
                calc.Record(FromTruth(body, utc, lat, lon));
            }

            DateTime last = T0.AddMinutes(20);
            double truthLat, truthLon;
            TruthAt(last, course, speed, out truthLat, out truthLon);

            Assert.True(calc.CalculateTriangleFix(course, speed, 2, out CockedHatResult hat));
            double error = RunningFixCalculator.DistanceNm(hat.FixLatDeg, hat.FixLonDeg,
                truthLat, truthLon);
            Assert.True(error < 0.05, "moving centre error " + error + " NM");
        }

        [Fact]
        public void GrossError_OneSight_InflatesTheTriangle()
        {
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));

            SextantObservation bad = FromTruth(Deneb, T0, TrueLat, TrueLon);
            bad.HoDeg += 1.0; // a full degree of mis-aim
            calc.Record(bad);

            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out CockedHatResult hat));
            Assert.True(hat.Ok);
            Assert.True(hat.RadiusNm > 5.0, "gross error must inflate the hat, R=" + hat.RadiusNm);
            Assert.Equal("rough", RunningFixCalculator.TriangleGrade(hat.RadiusNm));
        }

        [Fact]
        public void DegenerateHat_FallsBackToTheBestPair()
        {
            // Two identical Vega sights are parallel (no hat vertex), so the
            // cocked hat cannot form; the refined Vega+Arcturus pair is published.
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));

            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out CockedHatResult hat));
            Assert.True(hat.Ok);
            Assert.True(hat.UsedBestPairFallback);
            double error = RunningFixCalculator.DistanceNm(hat.FixLatDeg, hat.FixLonDeg,
                TrueLat, TrueLon);
            Assert.True(error < 0.05, "fallback error " + error + " NM");
        }

        [Fact]
        public void Vertices_AreThePairwiseIntersections()
        {
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Deneb, T0, TrueLat, TrueLon));

            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out CockedHatResult hat));

            // The sides recomputed from the published vertices must match the
            // sides of the hat (A = B∩C, B = C∩A, C = A∩B).
            double sideA = RunningFixCalculator.DistanceNm(
                hat.VertexBLatDeg, hat.VertexBLonDeg, hat.VertexCLatDeg, hat.VertexCLonDeg);
            double sideB = RunningFixCalculator.DistanceNm(
                hat.VertexCLatDeg, hat.VertexCLonDeg, hat.VertexALatDeg, hat.VertexALonDeg);
            double sideC = RunningFixCalculator.DistanceNm(
                hat.VertexALatDeg, hat.VertexALonDeg, hat.VertexBLatDeg, hat.VertexBLonDeg);

            Assert.InRange(sideA, hat.SideANm - 0.01, hat.SideANm + 0.01);
            Assert.InRange(sideB, hat.SideBNm - 0.01, hat.SideBNm + 0.01);
            Assert.InRange(sideC, hat.SideCNm - 0.01, hat.SideCNm + 0.01);
        }

        [Fact]
        public void RoughHat_PublishesTheConsistentPair_AndKeepsTheMetrics()
        {
            // Live 2026-09-22: a third sight 11 NM off dragged the centroid
            // 13.6 NM from the boat. The rough hat must publish the pair
            // WITHOUT the outlier, keeping the triangle metrics for the card.
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));

            SextantObservation bad = FromTruth(Deneb, T0, TrueLat, TrueLon);
            bad.HoDeg += 0.5; // ~30 NM of mis-aim
            calc.Record(bad);

            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out CockedHatResult hat));
            Assert.True(hat.Ok);
            Assert.True(hat.UsedBestPairFallback, "a rough hat must publish the pair");
            Assert.True(hat.RadiusNm > RunningFixCalculator.MaxPublishRadiusNm,
                "the metrics stay for the card, R=" + hat.RadiusNm);

            double error = RunningFixCalculator.DistanceNm(hat.FixLatDeg, hat.FixLonDeg,
                TrueLat, TrueLon);
            Assert.True(error < 1.0, "pair fix error " + error + " NM");
        }

        [Fact]
        public void TriangleGrade_Thresholds()
        {
            Assert.Equal("good", RunningFixCalculator.TriangleGrade(0.5));
            Assert.Equal("good", RunningFixCalculator.TriangleGrade(3.0));
            Assert.Equal("fair", RunningFixCalculator.TriangleGrade(4.5));
            Assert.Equal("rough", RunningFixCalculator.TriangleGrade(9.0));
        }

        [Fact]
        public void NeedsThreeObservations()
        {
            var calc = new RunningFixCalculator();
            calc.Record(FromTruth(Vega, T0, TrueLat, TrueLon));
            calc.Record(FromTruth(Arcturus, T0, TrueLat, TrueLon));
            Assert.False(calc.CalculateTriangleFix(0.0, 0.0, 2, out _));

            calc.Record(FromTruth(Deneb, T0, TrueLat, TrueLon));
            Assert.True(calc.CalculateTriangleFix(0.0, 0.0, 2, out _));
        }
    }
}
