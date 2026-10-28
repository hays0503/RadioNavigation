using System;
using System.Globalization;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>Offline numerical integration, NOT acceptance of a visible
    /// game sight. The conventional physical forward model below uses neither
    /// TryLowerLimb nor TryApparentCentre. Printed-cell goldens cover the exact
    /// table; this approximate model separately checks signs, SD, index and fix
    /// conditioning, without cancelling a production error in its own inverse.</summary>
    public sealed class Sh5EndToEndSightTests
    {
        private const double EyeHeightM = 5.5;
        private const double FarClipM = 9000.0;
        private const double ForwardToleranceArcMin = 0.5;

        public Sh5EndToEndSightTests()
        {
            Assert.True(Sh5SunAlmanac.Initialize(Path.Combine(Directory.GetCurrentDirectory(),
                "Source", "Data")), Sh5SunAlmanac.LastError);
        }

        [Theory]
        // Synthetic offsets, not a measurement or a diagnosis of either live
        // run. A two-sight fix amplifies altitude bias according to its geometry.
        [InlineData(0.0)]
        [InlineData(0.13)]
        [InlineData(0.5)]
        [InlineData(1.9)]
        public void Sh5_IndependentForwardHeightsGiveABoundedStationaryFix(double horizonLiftArcMin)
        {
            double trueLat = 55.0, trueLon = 5.75;
            DateTime first = Utc("1939-09-02T08:30:00Z");
            var calculator = new RunningFixCalculator();
            for (int i = 0; i < 2; i++)
            {
                DateTime utc = first.AddHours(3 * i);
                calculator.Record(ObservedSight(utc, trueLat, trueLon, trueLat, trueLon,
                    horizonLiftArcMin, EyeHeightM, 0.0));
            }
            double fixLat, fixLon;
            Assert.True(calculator.CalculateFix(0, 0, 3, out fixLat, out fixLon));
            double error = RunningFixCalculator.DistanceNm(trueLat, trueLon, fixLat, fixLon);
            double angle = Math.Abs(BodyRowFormatter.Wrap180(calculator.Observation2.AzDeg
                - calculator.Observation1.AzDeg)) * Math.PI / 180.0;
            Assert.InRange(angle * 180.0 / Math.PI, 30, 150);
            double equalBiasError = horizonLiftArcMin / Math.Cos(angle / 2.0);
            double budget = ForwardToleranceArcMin
                / Math.Min(Math.Sin(angle / 2.0), Math.Cos(angle / 2.0)) + 0.05;
            Assert.True(Math.Abs(error - equalBiasError) <= budget,
                string.Format(CultureInfo.InvariantCulture,
                    "fix error {0:F3} NM; synthetic lift {1:F2}', conditioned prediction {2:F3} +/- {3:F3} NM",
                    error, horizonLiftArcMin, equalBiasError, budget));
        }

        [Fact]
        public void Sh5_IndependentForwardHoIsRelativeToTheHorizontalNotTheDippedSea()
        {
            DateTime utc = Utc("1939-09-02T10:03:07Z");
            double lat = 55.0, lon = 5.75;
            double centre = TrueCentre(utc, lat, lon);
            double drum = Drum(utc, lat, lon, EyeHeightM, 0, 0);
            SextantObservation sight;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, drum, 0.0, lat, lon,
                Sighting(utc, lat, lon, EyeHeightM), out sight));
            Assert.InRange(sight.InterceptNm, -ForwardToleranceArcMin, ForwardToleranceArcMin);
            double delta = (sight.HoDeg - centre) * 60;
            Assert.True(Math.Abs(delta) <= ForwardToleranceArcMin,
                string.Format(CultureInfo.InvariantCulture,
                    "Ho differs from true centre altitude by {0:F3}'", delta));
            // Adding the game mean-water depression a second time would be a
            // several-mile bias, even though the table already includes dip.
            Assert.True(Math.Abs((sight.HoDeg - (centre - sight.SolarHorizonAltitudeDeg)) * 60) > 4);
        }

        [Fact]
        public void Sh5_SyntheticHorizonLiftShortensTheHeightWithoutAFittedCorrection()
        {
            DateTime utc = Utc("1939-09-02T10:03:07Z");
            double lat = 55.0, lon = 5.75;
            SextantObservation calm, rough;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, Drum(utc, lat, lon, EyeHeightM, 0.13, 0.0),
                0.0, lat, lon, Sighting(utc, lat, lon, EyeHeightM), out calm));
            Assert.True(Sh5SolarNavigation.TryObservation(utc, Drum(utc, lat, lon, EyeHeightM, 1.9, 0.0),
                0.0, lat, lon, Sighting(utc, lat, lon, EyeHeightM), out rough));
            // The drum is quantised; F VIII is also reinterpolated at the new h.
            Assert.Equal(-1.77, (rough.RawHsDeg - calm.RawHsDeg) * 60, 0.1);
            Assert.Equal(-1.77, (rough.HoDeg - calm.HoDeg) * 60, 0.15);
            Assert.Equal(0, rough.SunDiscCompensationArcMin);
            Assert.Equal(0, calm.SunDiscCompensationArcMin);
        }

        [Theory]
        [InlineData(-4.8)]
        [InlineData(-0.4)]
        [InlineData(0.4)]
        [InlineData(4.8)]
        public void Sh5_IndependentForwardIndexCorrectionIsAppliedOnce(double indexArcMin)
        {
            DateTime utc = Utc("1939-09-02T10:03:07Z");
            double lat = 55.0, lon = 5.75;
            double centre = TrueCentre(utc, lat, lon);
            SextantObservation sight;
            Assert.True(Sh5SolarNavigation.TryObservation(utc,
                Drum(utc, lat, lon, EyeHeightM, 0.0, indexArcMin), indexArcMin, lat, lon,
                Sighting(utc, lat, lon, EyeHeightM), out sight));
            // Production Ho must be the body's true CENTRE altitude, with no
            // second semidiameter and no second dip/refraction.
            Assert.True(Math.Abs(sight.HoDeg - centre) <= ForwardToleranceArcMin / 60.0,
                string.Format(CultureInfo.InvariantCulture,
                    "Ho off the independent centre altitude by {0:F3} arcmin; rawHs={1:F6} h={2:F6} corr={3:F4} ho={4:F6} centre={5:F6} eye={6} month={7}",
                    (sight.HoDeg - centre) * 60, sight.RawHsDeg, sight.IndexCorrectedHDeg,
                    sight.SolarFviiiArcMin, sight.HoDeg, centre, EyeHeightM, utc.Month));
            Assert.Equal(sight.RawHsDeg + indexArcMin / 60.0, sight.IndexCorrectedHDeg, 12);
        }

        [Theory]
        [InlineData(-6.47)]
        [InlineData(6.47)]
        public void Sh5_TimeShiftIsNotAnAdmissibleAngularIndexCorrection(double wrongIndex)
        {
            DateTime utc = Utc("1939-09-02T10:03:07Z");
            SextantObservation sight;
            Assert.False(Sh5SolarNavigation.TryObservation(utc, 38, wrongIndex, 55, 5.75,
                Sighting(utc, 55, 5.75, EyeHeightM), out sight));
            Assert.Null(sight);
        }

        [Theory]
        [InlineData("1939-09-02T10:03:07Z", 38.0, 5.5, 0.4, 10.6)]
        [InlineData("1940-04-01T12:00:00Z", 3 + 2.0 / 3, 4.0, -0.2, 0.0)]
        [InlineData("1941-04-01T12:00:00Z", 60.0, 4.0, 0.3, 12.0)]
        public void Sh5_HandPrintedCorrectionRecoversTheCentreAtAnIndependentPosition(
            string time, double correctedHs, double eye, double index, double printedCorrection)
        {
            DateTime utc = Utc(time);
            double expectedHo = correctedHs + printedCorrection / 60.0;
            double lat, lon;
            PositionAtHeight(utc, expectedHo, out lat, out lon);
            SextantObservation sight;
            // Printed-cell values, not a production inverse or a fitted offset.
            Assert.True(Sh5SolarNavigation.TryObservation(utc, correctedHs - index / 60.0,
                index, lat, lon, Sighting(utc, lat, lon, eye), out sight));
            Assert.Equal(printedCorrection, sight.SolarFviiiArcMin, 10);
            Assert.Equal(expectedHo, sight.HoDeg, 10);
            Assert.Equal(expectedHo, sight.HcDeg, 10);
            Assert.InRange(sight.InterceptNm, -1e-8, 1e-8);
        }

        [Theory]
        [InlineData("1939-01-01T12:00:00Z", 3.5, 0.0)]
        [InlineData("1940-02-29T12:00:00Z", 10.0, 1.0)]
        [InlineData("1941-04-01T12:00:00Z", 30.0, 4.0)]
        [InlineData("1942-07-01T12:00:00Z", 45.0, 7.0)]
        [InlineData("1943-09-02T12:00:00Z", 60.0, 11.0)]
        [InlineData("1945-12-31T12:00:00Z", 80.0, 14.0)]
        public void Sh5_IndependentForwardReductionCoversLowHighSunDatesAndEyes(
            string time, double centre, double eye)
        {
            DateTime utc = Utc(time);
            double lat, lon;
            PositionAtHeight(utc, centre, out lat, out lon);
            var sight = ObservedSight(utc, lat, lon, lat, lon, 0, eye, 0.4);
            Assert.True(Math.Abs(sight.HoDeg - centre) * 60 <= ForwardToleranceArcMin,
                string.Format(CultureInfo.InvariantCulture,
                    "independent forward height residual {0:F3}' at {1}, altitude {2}, eye {3}",
                    (sight.HoDeg - centre) * 60, time, centre, eye));
            Assert.Equal(eye, sight.SolarEyeHeightM);
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(80.0, 8.0)]
        public void Sh5_IndependentForwardThreeSightFixTransfersToTheLastPosition(double course, double speed)
        {
            DateTime first = Utc("1939-09-02T08:30:00Z");
            var calculator = new RunningFixCalculator();
            double lat = 55.0, lon = 5.75;
            for (int i = 0; i < 3; i++)
            {
                MoveRhumb(55.0, 5.75, course, speed * 2 * i, out lat, out lon);
                // The AP is deliberately wrong: the fix cannot just echo truth.
                calculator.Record(ObservedSight(first.AddHours(2 * i), lat, lon,
                    lat + 0.02, lon - 0.03, 0, EyeHeightM, -0.4));
            }
            CockedHatResult fix;
            Assert.True(calculator.CalculateTriangleFix(course, speed, 4, out fix));
            Assert.True(fix.Ok);
            double error = RunningFixCalculator.DistanceNm(lat, lon, fix.FixLatDeg, fix.FixLonDeg);
            Assert.True(error <= 1.0, string.Format(CultureInfo.InvariantCulture,
                "three-sight forward fix is {0:F3} NM from the last true position", error));
            Assert.True(calculator.HasThird);
        }

        /// <summary>Standard sextant forward model: the player reads the
        /// apparent (refracted) lower limb above the VISIBLE horizon, which sits
        /// the nautical dip below the horizontal. Refraction lifts the image, so
        /// true = apparent - R. This is an independent approximate check of the
        /// printed F VIII signs, not its replacement or a game-render model.</summary>
        private static double DipArcMin(double eye)
        {
            return 1.76 * Math.Sqrt(eye);
        }

        private static double RefractionArcMin(double altitudeDeg)
        {
            // Saemundsson: TRUE altitude input. Do not mix Bennett's apparent
            // altitude coefficients (7.31, 4.4) with Saemundsson's 1.02 scale.
            return 1.02 / Math.Tan(Math.PI / 180.0 * (altitudeDeg + 10.3 / (altitudeDeg + 5.11)));
        }

        private static double Drum(DateTime utc, double lat, double lon, double eye,
            double horizonLiftArcMin, double indexArcMin)
        {
            double centre = TrueCentre(utc, lat, lon);
            double sd = Diameter(utc) / 2.0;
            double lower = centre - sd;
            double raw = lower + (RefractionArcMin(lower) + DipArcMin(eye)
                - horizonLiftArcMin - indexArcMin) / 60.0;
            // 0.1' drum grid, independently of SextantArc's implementation.
            return Math.Round(raw * 600.0, MidpointRounding.AwayFromZero) / 600.0;
        }

        private static SextantObservation ObservedSight(DateTime utc, double lat, double lon,
            double apLat, double apLon, double seaLiftArcMin, double eye, double indexArcMin)
        {
            double drum = Drum(utc, lat, lon, eye, seaLiftArcMin, indexArcMin);
            SextantObservation sight;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, drum, indexArcMin, apLat, apLon,
                Sighting(utc, lat, lon, eye), out sight));
            return sight;
        }

        private static SunDiscSighting Sighting(DateTime utc, double lat, double lon, double eye)
        {
            double horizon;
            Assert.True(SolarSeaHorizon.TryAltitude(eye, FarClipM, true, out horizon));
            return new SunDiscSighting
            {
                TableRequested = true, TableCompatible = true, ConfiguredUtc = utc,
                ObservedDiameterDeg = Diameter(utc), Limb = SunSightLimb.LowerLimb,
                EyeHeightM = eye, HorizonAltitudeDeg = horizon,
                ObserverLatDeg = lat, ObserverLonDeg = lon
            };
        }

        private static double Diameter(DateTime utc)
        {
            // TT without legacy SUNDRIFT/DeltaMinutes; no mutable calibration.
            return SunDiscScale.AngularDiameterDeg(AstroMath.ToJulianDay(utc) + 24.0 / 86400.0);
        }

        private static double TrueCentre(DateTime utc, double lat, double lon)
        {
            double gha, dec;
            Assert.True(Sh5SunAlmanac.TryPlace(utc, out gha, out dec));
            double radians = Math.PI / 180.0;
            return Math.Asin(Math.Sin(lat * radians) * Math.Sin(dec * radians)
                + Math.Cos(lat * radians) * Math.Cos(dec * radians)
                * Math.Cos((gha + lon) * radians)) / radians;
        }

        private static void PositionAtHeight(DateTime utc, double altitude, out double lat, out double lon)
        {
            double gha, dec;
            Assert.True(Sh5SunAlmanac.TryPlace(utc, out gha, out dec));
            lat = dec;
            double radians = Math.PI / 180.0;
            double cosine = (Math.Sin(altitude * radians) - Math.Pow(Math.Sin(dec * radians), 2))
                / Math.Pow(Math.Cos(dec * radians), 2);
            lon = BodyRowFormatter.Wrap180(Math.Acos(cosine) / radians - gha);
            Assert.Equal(altitude, TrueCentre(utc, lat, lon), 10);
        }

        private static void MoveRhumb(double startLat, double startLon, double course, double distanceNm,
            out double lat, out double lon)
        {
            // Independent constant-course spherical route, not TranslateLop.
            double radians = Math.PI / 180.0;
            double delta = distanceNm * radians / 60.0;
            double phi1 = startLat * radians;
            double phi2 = phi1 + delta * Math.Cos(course * radians);
            double deltaPsi = Math.Log(Math.Tan(Math.PI / 4 + phi2 / 2)
                / Math.Tan(Math.PI / 4 + phi1 / 2));
            double q = Math.Abs(deltaPsi) > 1e-12 ? (phi2 - phi1) / deltaPsi : Math.Cos(phi1);
            lat = phi2 / radians;
            lon = startLon + delta * Math.Sin(course * radians) / (q * radians);
        }

        private static DateTime Utc(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
    }
}
