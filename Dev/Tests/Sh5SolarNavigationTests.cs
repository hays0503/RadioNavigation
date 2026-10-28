using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>Printed row/cell goldens plus actual optical adapter → worksheet
    /// → running fix → codec. All clocks/water inputs are offline doubles.
    /// Synthetic round trips are NOT independent proof of a visible sea horizon.</summary>
    public sealed class Sh5SolarNavigationTests
    {
        private static readonly string root = Directory.GetCurrentDirectory();
        private static readonly string data = Path.Combine(root, "Source", "Data");

        public Sh5SolarNavigationTests()
        {
            Assert.True(Sh5SunAlmanac.Initialize(data), Sh5SunAlmanac.LastError);
        }

        [Theory]
        [InlineData("1939-01-01T00:00:00Z", 179, 13.4, -22, 27.3)]
        [InlineData("1940-02-29T04:00:00Z", 236, 49.6, -8, 0.3)]
        [InlineData("1944-02-29T20:00:00Z", 116, 51.2, -7, 44.7)]
        [InlineData("1941-07-01T00:00:00Z", 179, 7.1, 22, 31.8)]
        [InlineData("1942-04-01T16:00:00Z", 58, 60.0, 4, 27.4)]
        [InlineData("1939-09-02T08:00:00Z", 300, 1.1, 8, 11.1)]
        [InlineData("1945-12-31T20:00:00Z", 119, 12.5, -22, 26.8)]
        public void Sh5_PrintedYearColumnsLeapDaysAndSixtyMinuteCarry(string time,
            double gDeg, double gMin, double dDeg, double dMin)
        {
            double gha, dec;
            Assert.True(Sh5SunAlmanac.TryPlace(Utc(time), out gha, out dec));
            Assert.Equal(gDeg + gMin / 60, gha, 11);
            Assert.Equal(dDeg + Math.Sign(dDeg) * dMin / 60, dec, 11);
        }

        [Fact]
        public void Sh5_PaperFourHourDAndGhaIncrementsAreAppliedWithoutPortOrSkyShift()
        {
            double old = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                SunEphemerisCalibration.SetDelta(37);
                double gha, dec;
                Assert.True(Sh5SunAlmanac.TryPlace(Utc("1939-09-02T10:03:07Z"), out gha, out dec));
                Assert.Equal(330.7975, gha, 10); // p.57 300°01.1′ + p.73 15°/h
                Assert.Equal(8.154220833333333, dec, 11); // d = −3.6′ per FOUR hours
                double hc, az;
                Sh5SolarNavigation.Place(gha, dec, 55, 5.75, out hc, out az);
                Assert.Equal(39.57297848946271, hc, 10);
                Assert.Equal(149.26269689948955, az, 10);
                Assert.Equal(37, SunEphemerisCalibration.DeltaMinutes);
            }
            finally { SunEphemerisCalibration.SetDelta(old); }
        }

        [Fact]
        public void Sh5_PackRequiresExactHashAndDoesNotFallBackAfterFailure()
        {
            string temporary = Path.Combine(root, "artifacts", "tests", "sh5-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            string file = Path.Combine(temporary, Sh5SunAlmanac.FileName);
            byte[] bytes = File.ReadAllBytes(Path.Combine(data, Sh5SunAlmanac.FileName));
            try
            {
                bytes[1000] ^= 1;
                File.WriteAllBytes(file, bytes);
                Assert.False(Sh5SunAlmanac.Initialize(temporary));
                Assert.False(Sh5SunAlmanac.Loaded);
                double gha, dec;
                Assert.False(Sh5SunAlmanac.TryPlace(new DateTime(1941, 1, 1), out gha, out dec));
                Assert.Contains("hash", Sh5SunAlmanac.LastError);
            }
            finally
            {
                File.Delete(file);
                Assert.True(Sh5SunAlmanac.Initialize(data));
            }
        }

        [Fact]
        public void Sh5_AllCampaignDatesAndLastSecondsAreCoveredWithoutExtrapolation()
        {
            DateTime first = new DateTime(1939, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime end = new DateTime(1946, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int count = 0;
            double gha, dec;
            for (DateTime utc = first; utc < end; utc = utc.AddHours(4))
            {
                Assert.True(Sh5SunAlmanac.TryPlace(utc, out gha, out dec));
                Assert.InRange(gha, 0, 359.999999);
                Assert.InRange(dec, -25, 25);
                count++;
            }
            Assert.Equal(15342, count);
            Assert.True(Sh5SunAlmanac.TryPlace(end.AddTicks(-1), out gha, out dec));
            Assert.False(Sh5SunAlmanac.TryPlace(first.AddTicks(-1), out gha, out dec));
            Assert.False(Sh5SunAlmanac.TryPlace(end, out gha, out dec));
            Assert.False(Sh5SunAlmanac.TryPlace(DateTime.SpecifyKind(first, DateTimeKind.Local), out gha, out dec));
        }

        [Theory]
        [InlineData(3, 0, 1.8)]
        [InlineData(3, 14, -5.2)]
        [InlineData(3 + 2.0/3, 4, 0.0)]
        [InlineData(5, 12, 0.0)]
        [InlineData(10 + 1.0/3, 1, 9.2)]
        [InlineData(12.5, 7, 7.1)]
        [InlineData(14, 9, 6.9)]
        [InlineData(20, 5, 9.5)]
        [InlineData(30, 7, 9.7)]
        [InlineData(60, 11, 9.6)]
        [InlineData(80, 14, 9.2)]
        [InlineData(90, 0, 16.0)]
        [InlineData(90, 14, 9.4)]
        public void Sh5_FviiiAdditionalPrintedCellsIncludeSignsAndFullBounds(double altitude, double eye, double expected)
        {
            double b, m;
            Assert.True(SolarHeightReduction.TryCorrection(altitude, eye, 4, out b, out m));
            Assert.Equal(expected, b, 11);
            Assert.Equal(0, m);
        }

        [Fact]
        public void Sh5_FviiiPreservesPrintedZeroAndTwelveRatherThanSmoothingThem()
        {
            // The first two transcriptions shared these mistakes. Raw crops of
            // F-Tafel PDF p.98 show -0.0 and 12.0, NOT nominal -0.2 and 11.9.
            // Honour the supplied table even when its progression is uneven.
            double b, m;
            Assert.True(SolarHeightReduction.TryCorrection(3 + 2.0 / 3, 4, 4, out b, out m));
            Assert.Equal(0, b);
            Assert.True(SolarHeightReduction.TryCorrection(60, 4, 4, out b, out m));
            Assert.Equal(12.0, b);
        }

        [Fact]
        public void Sh5_FviiiMatchesAll810PrintedCellsAndMonthlyTerms()
        {
            using (var fixture = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, "Tools", "solar-sight", "fviii-full-verified.json"))))
            {
                var altitudes = fixture.RootElement.GetProperty("altitudes_dm");
                var rows = fixture.RootElement.GetProperty("base_arcmin");
                Assert.Equal(54, rows.GetArrayLength());
                Assert.Equal(54, altitudes.GetArrayLength());
                for (int r = 0; r < altitudes.GetArrayLength(); r++)
                {
                    var cells = rows[r];
                    Assert.Equal(15, cells.GetArrayLength());
                    for (int c = 0; c < cells.GetArrayLength(); c++)
                    {
                        double b, m;
                        double h = altitudes[r][0].GetDouble() + altitudes[r][1].GetDouble() / 60;
                        Assert.True(SolarHeightReduction.TryCorrection(h, c, 4, out b, out m));
                        Assert.Equal(cells[c].GetDouble(), b, 11);
                    }
                }
                var months = fixture.RootElement.GetProperty("monthly_lower_limb_arcmin");
                Assert.Equal(12, months.GetArrayLength());
                for (int i = 0; i < 12; i++)
                {
                    double b, m;
                    Assert.True(SolarHeightReduction.TryCorrection(38, 5.5, i + 1, out b, out m));
                    Assert.Equal(months[i].GetDouble(), m, 11);
                }
            }
        }

        [Fact]
        public void Sh5_FviiiUsesIndexBeforeInterpolationAndMonthlyTermExactlyOnce()
        {
            double[] monthly = {0.3,0.2,0.1,0,-0.2,-0.2,-0.2,-0.2,-0.1,0.1,0.2,0.3};
            for (int month = 1; month <= 12; month++)
            {
                double ho, correction;
                Assert.True(SolarHeightReduction.TryReduce(37.995, 0.3, 5.5, month,
                    SunSightLimb.LowerLimb, out ho, out correction));
                Assert.Equal(10.7 + monthly[month - 1], correction, 11);
                Assert.Equal(38 + (10.7 + monthly[month - 1]) / 60, ho, 11);
            }
            double baseline, m;
            Assert.True(SolarHeightReduction.TryCorrection(37, 5.5, 9, out baseline, out m));
            Assert.Equal(10.65, baseline, 11);
            Assert.Equal(-0.1, m, 11);
        }

        [Theory]
        [InlineData(2.999, 0, 5.5, SunSightLimb.LowerLimb)]
        [InlineData(90.001, 0, 5.5, SunSightLimb.LowerLimb)]
        [InlineData(38, 5.01, 5.5, SunSightLimb.LowerLimb)]
        [InlineData(38, 0, -0.01, SunSightLimb.LowerLimb)]
        [InlineData(38, 0, 14.01, SunSightLimb.LowerLimb)]
        [InlineData(38, 0, 5.5, SunSightLimb.UpperLimb)]
        [InlineData(double.NaN, 0, 5.5, SunSightLimb.LowerLimb)]
        [InlineData(38, double.PositiveInfinity, 5.5, SunSightLimb.LowerLimb)]
        public void Sh5_InvalidReductionIsRefusedNotClamped(double hs, double index, double eye, SunSightLimb limb)
        {
            double ho, correction;
            Assert.False(SolarHeightReduction.TryReduce(hs, index, eye, 9, limb, out ho, out correction));
        }

        [Fact]
        public void Sh5_OpticalRelationRoundTripsAllMonthsHeightsAndSupportedAltitudes()
        {
            for (int month = 1; month <= 12; month++)
                for (double eye = 0; eye <= 14; eye += 0.5)
                    for (double hs = 3; hs <= 89.5; hs += 0.5)
                    {
                        double ho, correction, lower;
                        Assert.True(SolarHeightReduction.TryReduce(hs, 0, eye, month, SunSightLimb.LowerLimb,
                            out ho, out correction));
                        Assert.True(SolarHeightReduction.TryLowerLimb(ho, eye, month, out lower));
                        Assert.Equal(hs, lower, 10);
                    }
        }

        [Fact]
        public void Sh5_HorizonUsesGameShaderNotRealEarthOrCameraWorldHeight()
        {
            double curved, flat;
            Assert.True(SolarSeaHorizon.TryAltitude(5.5, 9000, true, out curved));
            Assert.Equal(-0.07529415393001057, curved, 12); // installed shader knot 8250 m
            Assert.True(SolarSeaHorizon.TryAltitude(5.5, 9000, false, out flat));
            Assert.Equal(Math.Atan(-5.5/9000) * 180 / Math.PI, flat, 12);
            Assert.True(Math.Abs(curved * 60 + 1.76 * Math.Sqrt(5.5)) > 0.3);
            Assert.False(SolarSeaHorizon.TryAltitude(double.NaN, 9000, true, out curved));
            Assert.False(SolarSeaHorizon.TryAltitude(15, 9000, true, out curved));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        public void Sh5_ActualOpticsUsesMeanWaterAndIgnoresLegacySkyCalibration(float curved)
        {
            var camera = Camera(105.5f, 100);
            var optics = new SextantSolarOptics();
            double old = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                UnityEngine.Shader.Curvature = curved;
                SunEphemerisCalibration.SetDelta(-6.47);
                DateTime time = Utc("1939-09-02T10:03:07Z");
                optics.Refresh(camera, true, time, 55, 5.75);
                Assert.True(optics.Ready);
                Assert.Equal(5.5, optics.EyeHeightM);
                var expected = optics.Direction;
                double diameter = optics.DiameterDeg;
                SunEphemerisCalibration.SetDelta(37);
                optics.Refresh(camera, true, time, 55, 5.75);
                Assert.Equal(expected, optics.Direction);
                Assert.Equal(diameter, optics.DiameterDeg);
                var disc = optics.Sighting(true);
                double ho, correction;
                Assert.True(SolarHeightReduction.TryReduce(disc.ExpectedHsDeg, 0, disc.EyeHeightM, 9,
                    disc.Limb, out ho, out correction));
                Assert.Equal(39.57297848946271, ho, 10);
                Assert.False(optics.Sighting(false).TableCompatible);
                optics.Refresh(camera, false, time, 55, 5.75);
                Assert.False(optics.Ready);
                Assert.True(optics.Sighting(true).TableRequested);
                Assert.False(optics.Sighting(true).TableCompatible);
            }
            finally { SunEphemerisCalibration.SetDelta(old); }
        }

        [Fact]
        public void Sh5_ActualCaptureFreezesRawIndexEyeClockAndModelThroughSave()
        {
            DateTime oldClock = GameTimeResolver.Now;
            float oldIndex = UnityEngine.PlayerPrefs.GetFloat(IndexErrorCorrection.PrefsKey, 0);
            double oldDelta = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                DateTime sourceUtc = Utc("1939-09-02T10:03:07Z");
                var camera = Camera(105.5f, 100);
                UnityEngine.Shader.Curvature = 1;
                var optics = new SextantSolarOptics();
                optics.Refresh(camera, true, sourceUtc, 55, 5.75);
                var disc = optics.Sighting(true);
                UnityEngine.PlayerPrefs.SetFloat(IndexErrorCorrection.PrefsKey, 0.4f);
                double index = IndexErrorCorrection.Load();
                double raw = disc.ExpectedHsDeg - index/60;
                GameTimeResolver.Now = sourceUtc.AddMinutes(10); // must not move the captured source's GMT
                var worksheet = new SextantWorksheet(new Position { Lat = 60, Lon = -15 });
                Assert.True(worksheet.CaptureObservation(CelestialBody.Sun(), raw, 0.15, 4, 1, disc));
                SextantObservation sight = worksheet.Calculator.Observation1;
                Assert.Equal(sourceUtc, sight.Utc);
                Assert.Equal(55, sight.DrLatDeg);
                Assert.Equal(5.75, sight.DrLonDeg);
                Assert.Equal(1, sight.SolarReductionVersion);
                Assert.Equal(raw, sight.RawHsDeg);
                Assert.Equal(raw + index/60, sight.IndexCorrectedHDeg);
                Assert.Equal(index, sight.IndexErrorArcMin);
                Assert.Equal(5.5, sight.SolarEyeHeightM);
                Assert.Equal(disc.HorizonAltitudeDeg, sight.SolarHorizonAltitudeDeg);
                Assert.Equal(0, sight.SunDiscCompensationArcMin);
                Assert.Equal(39.57297848946271, sight.HoDeg, 10);
                string saved = SextantSaveCodec.Encode(worksheet.CaptureState());
                SunEphemerisCalibration.SetDelta(40);
                UnityEngine.PlayerPrefs.SetFloat(IndexErrorCorrection.PrefsKey, -4);
                SextantSaveSnapshot restored; string error;
                Assert.True(SextantSaveCodec.TryDecode(saved, out restored, out error), error);
                foreach (var field in typeof(SextantObservation).GetFields())
                    Assert.Equal(field.GetValue(sight), field.GetValue(restored.Observation1));
                string card = FixCardComposer.Compose(restored.Observation1, null, null, null, 40);
                Assert.Contains("SH5 LL Hs", card);
                Assert.Contains("F VIII", card);
                Assert.Contains("Глаз 5.5", card);
                Assert.Contains("не применяется к SH5", card);
                Assert.True(Math.Abs(sight.InterceptNm) < 1e-7);
            }
            finally
            {
                GameTimeResolver.Now = oldClock;
                UnityEngine.PlayerPrefs.SetFloat(IndexErrorCorrection.PrefsKey, oldIndex);
                SunEphemerisCalibration.SetDelta(oldDelta);
            }
        }

        [Fact]
        public void Sh5_CaptureKeepsTheExactConfiguredPositionBeforeBuildingHc()
        {
            var optics = new SextantSolarOptics();
            const double latitude = 55.123456789, longitude = 5.789123456;
            optics.Refresh(Camera(5.5f, 0), true, GameTimeResolver.Now, latitude, longitude);
            var disc = optics.Sighting(true);
            var worksheet = new SextantWorksheet(new Position { Lat = 60, Lon = -15 });
            Assert.True(worksheet.CaptureObservation(CelestialBody.Sun(), disc.ExpectedHsDeg, 0, 1, 0, disc));
            var sight = worksheet.Calculator.Observation1;
            double hc, az;
            Assert.True(Sh5SolarNavigation.TryCentre(disc.ConfiguredUtc, latitude, longitude, out hc, out az));
            Assert.Equal(latitude, sight.DrLatDeg);
            Assert.Equal(longitude, sight.DrLonDeg);
            Assert.Equal(hc, sight.HcDeg);
            Assert.Equal(az, sight.AzDeg);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Sh5_NoLegacySolarSeriesCanAcceptANewTableSun(int count)
        {
            var legacy = new SextantSaveSnapshot
            {
                Observation1 = RunningFixCalculator.BuildObservation(GameTimeResolver.Now, 38, 55, 5.75)
            };
            if (count >= 2)
                legacy.Observation2 = RunningFixCalculator.BuildObservation(GameTimeResolver.Now.AddHours(1), 38, 55, 5.75);
            if (count >= 3)
                legacy.Observation3 = RunningFixCalculator.BuildObservation(GameTimeResolver.Now.AddHours(2), 38, 55, 5.75);
            var worksheet = new SextantWorksheet(new Position());
            worksheet.RestoreState(legacy);
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38, 0, 1, 0, Disc(GameTimeResolver.Now)));
            Assert.Same(legacy.Observation1, worksheet.Calculator.Observation1);
            Assert.Same(legacy.Observation2, worksheet.Calculator.Observation2);
            Assert.Same(legacy.Observation3, worksheet.Calculator.Observation3);
        }

        [Theory]
        [InlineData("SolarReductionVersion", 2)]
        [InlineData("SolarReductionVersion", 0)]
        [InlineData("BodyKind", (double)CelestialBodyKind.Moon)]
        [InlineData("SunLimb", (double)SunSightLimb.UpperLimb)]
        [InlineData("Utc", 1938)]
        [InlineData("Utc", 1946)]
        [InlineData("SolarEyeHeightM", -0.01)]
        [InlineData("SolarEyeHeightM", 14.01)]
        [InlineData("IndexCorrectedHDeg", 2.999)]
        [InlineData("IndexCorrectedHDeg", 90.001)]
        [InlineData("IndexErrorArcMin", 5.01)]
        [InlineData("RawHsDeg", 42)]
        [InlineData("HoDeg", 40)]
        [InlineData("SolarFviiiArcMin", 15)]
        [InlineData("SunDiscCompensationArcMin", 0.1)]
        [InlineData("SolarHorizonAltitudeDeg", 0.001)]
        [InlineData("SolarHorizonAltitudeDeg", -1.001)]
        [InlineData("ObservedSunAngularDiameterDeg", 0.499)]
        [InlineData("ObservedSunAngularDiameterDeg", 0.561)]
        [InlineData("RawHsDeg", double.NaN)]
        [InlineData("SolarEyeHeightM", double.PositiveInfinity)]
        [InlineData("SolarFviiiArcMin", double.NegativeInfinity)]
        public void Sh5_CorruptV3PayloadIsRejectedTransactionally(string fieldName, double invalid)
        {
            var sight = ValidSolarSight();
            var field = typeof(SextantObservation).GetField(fieldName);
            if (fieldName == "Utc")
                field.SetValue(sight, new DateTime((int)invalid, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            else if (field.FieldType.IsEnum)
                field.SetValue(sight, Enum.ToObject(field.FieldType, (int)invalid));
            else if (field.FieldType == typeof(int))
                field.SetValue(sight, (int)invalid);
            else
                field.SetValue(sight, invalid);
            RejectSolarBytes(UncheckedSolarV3(sight));
        }

        [Fact]
        public void Sh5_FrozenCorrectionCannotExceedBoundsEvenWithConsistentHo()
        {
            var sight = ValidSolarSight();
            sight.SolarFviiiArcMin = 18;
            sight.HoDeg = sight.IndexCorrectedHDeg + 18.0 / 60;
            RejectSolarBytes(UncheckedSolarV3(sight));
        }

        [Fact]
        public void Sh5_V3DecoderPreservesFrozenSightWithNoLiveAlmanacAvailable()
        {
            var sight = ValidSolarSight();
            string payload = UncheckedSolarV3(sight);
            try
            {
                Assert.False(Sh5SunAlmanac.Initialize(Path.Combine(root, "artifacts", "no-such-sun-pack")));
                SextantSaveSnapshot restored; string error;
                Assert.True(SextantSaveCodec.TryDecode(payload, out restored, out error), error);
                foreach (var field in typeof(SextantObservation).GetFields())
                    Assert.Equal(field.GetValue(sight), field.GetValue(restored.Observation1));
            }
            finally { Assert.True(Sh5SunAlmanac.Initialize(data)); }
        }

        private static SextantObservation ValidSolarSight()
        {
            DateTime utc = Utc("1939-09-02T10:03:07Z");
            SextantObservation sight;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, 38, 0.4, 55, 5.75, Disc(utc), out sight));
            return sight;
        }

        private static void RejectSolarBytes(string payload)
        {
            SextantSaveSnapshot restored; string error;
            Assert.False(SextantSaveCodec.TryDecode(payload, out restored, out error));
            Assert.Null(restored);
            Assert.False(string.IsNullOrEmpty(error));
        }

        /// <summary>Independent v3 primitive layout, intentionally no Encode
        /// self-validation. These malformed wire bytes exercise the decoder,
        /// not just the encoder's refusal to write an invalid object.</summary>
        private static string UncheckedSolarV3(SextantObservation o)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8))
            {
                w.Write(0x524E4153); w.Write(3); w.Write(true);
                w.Write(o.Utc.Ticks); w.Write(o.HoDeg); w.Write(o.DrLatDeg); w.Write(o.DrLonDeg);
                w.Write((int)o.BodyKind);
                var name = System.Text.Encoding.UTF8.GetBytes(o.BodyName);
                w.Write(name.Length); w.Write(name); w.Write(o.BodyHip);
                w.Write(o.BodyRaDeg); w.Write(o.BodyDecDeg);
                w.Write(o.GhaAriesDeg); w.Write(o.ShaBodyDeg); w.Write(o.GhaBodyDeg);
                w.Write(o.LhaDeg); w.Write(o.DecDeg); w.Write(o.HcDeg); w.Write(o.AzDeg); w.Write(o.InterceptNm);
                w.Write(o.Quadrant); w.Write(o.QuadrantWest);
                w.Write(o.HoSigmaArcMin); w.Write(o.HoSampleCount); w.Write(o.HoRejectedCount); w.Write(o.IndexErrorArcMin);
                w.Write(o.ObservedSunAngularDiameterDeg); w.Write((int)o.SunLimb); w.Write(o.SunDiscCompensationArcMin);
                w.Write(o.SolarReductionVersion); w.Write(o.RawHsDeg); w.Write(o.IndexCorrectedHDeg);
                w.Write(o.SolarEyeHeightM); w.Write(o.SolarFviiiArcMin); w.Write(o.SolarHorizonAltitudeDeg);
                w.Write(false); w.Write(false); // slots 2/3
                w.Write(-1); w.Write(0.0); // card/calibration
                w.Write(false); w.Write(0.0); w.Write(0.0); w.Write(double.NaN); w.Write(0.0); w.Write(false);
                w.Write(0f); w.Write(0f); w.Write(0); // course
                w.Write(false); w.Write(0.0); w.Write(0.0); w.Write(-1); w.Write(false); w.Write(0); // markers
                for (int i = 0; i < 6; i++) w.Write(0.0);
                w.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        [Fact]
        public void Sh5_RunningSolarFixRefinesFrozenGhaNotTheLiveCalibratedRa()
        {
            double old = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                var calculator = new RunningFixCalculator();
                DateTime time = Utc("1941-06-21T08:00:00Z");
                calculator.Record(Observation(time, 53, -15, 52.8, -14.8));
                calculator.Record(Observation(time.AddHours(4), 53, -15, 52.8, -14.8));
                SunEphemerisCalibration.SetDelta(60);
                double fixLat, fixLon;
                Assert.True(calculator.CalculateFix(0, 0, 4, out fixLat, out fixLon));
                Assert.True(RunningFixCalculator.DistanceNm(53, -15, fixLat, fixLon) < 0.02);
                double hc, az;
                RunningFixCalculator.RecomputeHcAz(calculator.Observation1, 53, -15, out hc, out az);
                Assert.Equal(calculator.Observation1.HoDeg, hc, 10);
            }
            finally { SunEphemerisCalibration.SetDelta(old); }
        }

        [Fact]
        public void Sh5_UnreadyOrUpperLimbCannotRecordOrMixIntoAnUnfinishedLegacySolarSeries()
        {
            var worksheet = new SextantWorksheet(new Position());
            worksheet.RestoreState(new SextantSaveSnapshot
            {
                Observation1 = RunningFixCalculator.BuildObservation(GameTimeResolver.Now, 38, 55, 5.75)
            });
            var frozen = worksheet.Calculator.Observation1;
            var disc = Disc(GameTimeResolver.Now);
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38, 0, 1, 0, disc));
            Assert.Same(frozen, worksheet.Calculator.Observation1);
            Assert.Null(worksheet.Calculator.Observation2);
            worksheet.ResetSeries();
            disc.TableCompatible = false;
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38, 0, 1, 0, disc));
            Assert.Null(worksheet.Calculator.Observation1);
            disc.TableCompatible = true;
            disc.Limb = SunSightLimb.UpperLimb;
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38, 0, 1, 0, disc));
            Assert.Null(worksheet.Calculator.Observation1);
        }

        private static DateTime Utc(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        private static UnityEngine.Camera Camera(float eyeY, float seaY)
        {
            var water = new PlayWay.Water.Water();
            water.transform.position = new UnityEngine.Vector3(0, seaY, 0);
            var camera = new UnityEngine.Camera();
            camera.transform.position = new UnityEngine.Vector3(0, eyeY, 0);
            camera.WaterComponent = new PlayWay.Water.WaterCamera { MainWater = water, WaterLevel = seaY + 2 };
            return camera;
        }

        private static SunDiscSighting Disc(DateTime utc)
        {
            return new SunDiscSighting { TableRequested = true, TableCompatible = true,
                ConfiguredUtc = utc, ObservedDiameterDeg = 0.53, Limb = SunSightLimb.LowerLimb,
                EyeHeightM = 5.5, HorizonAltitudeDeg = -0.11827238574173227,
                ObserverLatDeg = 55, ObserverLonDeg = 5.75 };
        }

        private static SextantObservation Observation(DateTime utc, double lat, double lon, double apLat, double apLon)
        {
            double hc, az, hs;
            Assert.True(Sh5SolarNavigation.TryCentre(utc, lat, lon, out hc, out az));
            Assert.True(SolarHeightReduction.TryLowerLimb(hc, 5.5, utc.Month, out hs));
            SextantObservation sight;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, hs, 0, apLat, apLon, Disc(utc), out sight));
            return sight;
        }

        private sealed class Position : IPlayerPositionProvider
        {
            public float Lat = 55, Lon = 5.75f;
            public bool TryGetPosition(out float lat, out float lon) { lat = Lat; lon = Lon; return true; }
        }
    }
}
