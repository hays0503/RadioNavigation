using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantSaveTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RoundTripEverySightSlotAndEveryFrozenObservationField(int count)
        {
            SextantSaveSnapshot state = Fixture(count);
            SextantSaveSnapshot restored = Decode(SextantSaveCodec.Encode(state));
            AssertObservation(state.Observation1, restored.Observation1);
            AssertObservation(state.Observation2, restored.Observation2);
            AssertObservation(state.Observation3, restored.Observation3);
            Assert.Equal(state.SectionText, restored.SectionText);
            Assert.Equal(state.CalibrationMinutes, restored.CalibrationMinutes);
            Assert.Equal(state.FixPublished, restored.FixPublished);
            Assert.Equal(state.FixLatDeg, restored.FixLatDeg);
            Assert.Equal(state.FixLonDeg, restored.FixLonDeg);
            Assert.Equal(state.FixErrorNm, restored.FixErrorNm);
            Assert.Equal(state.FixCrossingDeg, restored.FixCrossingDeg);
            Assert.Equal(state.FixSameBody, restored.FixSameBody);
            Assert.Equal(state.Markers.Label, restored.Markers.Label);
            Assert.Equal(state.Markers.Lat, restored.Markers.Lat);
            Assert.Equal(state.Markers.Lon, restored.Markers.Lon);
            Assert.Equal(state.Markers.VertexLat, restored.Markers.VertexLat);
            Assert.Equal(state.Markers.VertexLon, restored.Markers.VertexLon);
            Assert.Equal(state.Markers.HasTriangle, restored.Markers.HasTriangle);
            Assert.Equal(state.Markers.SuppressedMask, restored.Markers.SuppressedMask);
            Assert.Equal(state.Course.CourseDeg, restored.Course.CourseDeg);
            Assert.Equal(state.Course.SpeedKn, restored.Course.SpeedKn);
            Assert.Equal(state.Course.Samples, restored.Course.Samples);
        }

        [Fact]
        public void PayloadIsDetachedAndSwitchingSavesDoesNotMergeTheSeries()
        {
            SextantSaveSnapshot a = Fixture(3);
            string payloadA = SextantSaveCodec.Encode(a);
            string payloadB = SextantSaveCodec.Encode(Fixture(0));
            a.Observation1.HoDeg = 1;
            a.Markers.VertexLat[0] = 1;
            var calculator = new RunningFixCalculator();
            RestoreCalculator(calculator, Decode(payloadA));
            Assert.True(calculator.HasThird);
            Assert.NotEqual(1, calculator.Observation1.HoDeg);
            RestoreCalculator(calculator, Decode(payloadB));
            Assert.Null(calculator.Observation1);
            Assert.Null(calculator.Observation2);
            Assert.Null(calculator.Observation3);
            RestoreCalculator(calculator, Decode(payloadA));
            Assert.True(calculator.HasThird);
        }

        [Fact]
        public void CourseTrackContinuesExactlyAfterRoundTrip()
        {
            var original = new CourseSampler();
            DateTime utc = new DateTime(1941, 11, 5, 2, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i <= 6; i++)
                original.AddSample(utc.AddSeconds(i * 2), 48 + i * 0.0001, -15);
            var state = new SextantSaveSnapshot { Course = original.CaptureState() };
            var restored = new CourseSampler();
            restored.RestoreState(Decode(SextantSaveCodec.Encode(state)).Course);
            Assert.True(restored.IsFresh(utc.AddSeconds(12)));
            Assert.Equal(original.CourseDeg, restored.CourseDeg);
            Assert.Equal(original.SpeedKn, restored.SpeedKn);
            original.AddSample(utc.AddSeconds(14), 48.0007, -15);
            restored.AddSample(utc.AddSeconds(14), 48.0007, -15);
            Assert.Equal(original.CaptureState().Samples, restored.CaptureState().Samples);
            Assert.Equal(original.SpeedKn, restored.SpeedKn);
            restored.RestoreState(new CourseSamplerSnapshot());
            Assert.False(restored.IsFresh(utc));
            Assert.Equal(0, restored.SpeedKn);
        }

        [Fact]
        public void SnapshotRestorationKeepsFrozenSightsAndResultDespiteLiveCalibration()
        {
            SextantSaveSnapshot state = Fixture(3);
            double old = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                SunEphemerisCalibration.SetDelta(37);
                var restored = Decode(SextantSaveCodec.Encode(state));
                AssertObservation(state.Observation1, restored.Observation1);
                Assert.Equal(state.FixLatDeg, restored.FixLatDeg);
                Assert.Equal(state.FixLonDeg, restored.FixLonDeg);
                Assert.Equal(state.SectionText, restored.SectionText);
                Assert.Equal(-6.59, restored.CalibrationMinutes);
            }
            finally
            {
                SunEphemerisCalibration.SetDelta(old);
            }
        }

        [Fact]
        public void EncodingIsCultureIndependentAndKeepsNanErrorAndUnicode()
        {
            var state = Fixture(2);
            state.FixErrorNm = double.NaN;
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
                string ru = SextantSaveCodec.Encode(state);
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Assert.Equal(ru, SextantSaveCodec.Encode(state));
                Assert.True(double.IsNaN(Decode(ru).FixErrorNm));
                Assert.Contains("Геометрия", Decode(ru).SectionText);
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(15)]
        public void DismissalMaskKeepsEachMarkerHiddenAcrossLoad(int mask)
        {
            var state = Fixture(3);
            state.Markers.SuppressedMask = mask;
            FixMarkerSnapshot restored = Decode(SextantSaveCodec.Encode(state)).Markers;
            for (int i = 0; i < 4; i++)
                Assert.Equal((mask & (1 << i)) == 0, restored.IsVisible(i));
            restored.HasFix = false;
            Assert.False(restored.IsVisible(0));
            Assert.False(restored.IsVisible(3));
        }

        [Theory]
        [InlineData((string)null)]
        [InlineData("")]
        [InlineData("not base64")]
        [InlineData("AAAA")]
        public void BadPayloadIsRejectedWithoutPartialState(string payload)
        {
            AssertRejected(payload);
        }

        [Fact]
        public void FutureVersionTruncationTrailingBytesAndOversizeAreRejected()
        {
            byte[] valid = Convert.FromBase64String(SextantSaveCodec.Encode(Fixture(3)));
            byte[] future = (byte[])valid.Clone();
            Array.Copy(BitConverter.GetBytes(2), 0, future, 4, 4);
            AssertRejected(Convert.ToBase64String(future));
            for (int length = 1; length < valid.Length; length += 31)
                AssertRejected(Convert.ToBase64String(valid.Take(length).ToArray()));
            AssertRejected(Convert.ToBase64String(valid.Concat(new byte[] { 0 }).ToArray()));
            AssertRejected(new string('A', 100000));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void InvalidSemanticStateCannotBeWritten(int mutation)
        {
            var state = Fixture(3);
            switch (mutation)
            {
                case 0: state.Observation1.HoDeg = double.NaN; break;
                case 1: state.Observation2.DrLatDeg = 100; break;
                case 2: state.Observation3.BodyKind = CelestialBodyKind.None; break;
                case 3: state.Observation3.BodyHip = 0; break;
                case 4: state.Markers.SuppressedMask = 16; break;
                case 5: state.Observation1 = null; break;
                case 6: state.Course.SpeedKn = float.PositiveInfinity; break;
                case 7: state.SectionText = new string('x', 9000); break;
            }
            Assert.Throws<InvalidDataException>(() => SextantSaveCodec.Encode(state));
        }

        [Theory]
        [InlineData("")]
        [InlineData((string)null)]
        [InlineData("MCP Point")]
        [InlineData("RadioNav FIX old")]
        [InlineData("RadioNav LH DE-042")]
        [InlineData("RadioNav STN DDH")]
        public void LegacyOwnershipNeverInfersAnAstroPointFromEmptyOrSimilarNames(string name)
        {
            Assert.Equal(-1, FixMarkerSnapshot.IndexOfKey(name));
        }

        [Fact]
        public void OnlyFourExactKeysAreTheActiveSeries()
        {
            for (int i = 0; i < 4; i++)
                Assert.Equal(i, FixMarkerSnapshot.IndexOfKey(FixMarkerSnapshot.Key(i)));
            var legacy = new SextantSaveSnapshot
            {
                Markers = new FixMarkerSnapshot { HasFix = true, Lat = 48, Lon = -15, Label = "legacy" }
            };
            var loaded = Decode(SextantSaveCodec.Encode(legacy));
            Assert.True(loaded.Markers.HasFix);
            Assert.False(loaded.FixPublished);
            Assert.Null(loaded.Observation1); // Do not fabricate missing observations.
        }

        private static void RestoreCalculator(RunningFixCalculator calculator, SextantSaveSnapshot state)
        {
            calculator.Reset();
            calculator.Record(state.Observation1);
            calculator.Record(state.Observation2);
            calculator.Record(state.Observation3);
        }

        private static SextantSaveSnapshot Decode(string payload)
        {
            SextantSaveSnapshot state;
            string error;
            Assert.True(SextantSaveCodec.TryDecode(payload, out state, out error), error);
            return state;
        }

        private static void AssertRejected(string payload)
        {
            SextantSaveSnapshot state;
            string error;
            Assert.False(SextantSaveCodec.TryDecode(payload, out state, out error));
            Assert.Null(state);
            Assert.False(string.IsNullOrEmpty(error));
        }

        private static void AssertObservation(SextantObservation expected, SextantObservation actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }
            Assert.NotSame(expected, actual);
            // Exhaustive: adding a field without adding it to the codec must fail.
            foreach (FieldInfo field in typeof(SextantObservation).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.Equal(field.GetValue(expected), field.GetValue(actual));
            Assert.Equal(DateTimeKind.Utc, actual.Utc.Kind);
        }

        private static SextantSaveSnapshot Fixture(int count)
        {
            var state = new SextantSaveSnapshot
            {
                CalibrationMinutes = -6.59,
                SectionText = count >= 2 ? "Геометрия 60°\nFIX\nφ 48° λ -15°\n" : null,
                FixPublished = count >= 2, FixLatDeg = 48.25503, FixLonDeg = -15.7539482,
                FixErrorNm = 1.234, FixCrossingDeg = 60.125, FixSameBody = false,
                Markers = new FixMarkerSnapshot
                {
                    HasFix = count >= 2, HasTriangle = count >= 3,
                    Lat = 48.25503, Lon = -15.7539482, Label = "FIX 02:00:00\nMoon + Capella",
                    VertexLat = new[] { 48.1, 48.2, 48.3 }, VertexLon = new[] { -15.1, -15.2, -15.3 },
                    SuppressedMask = 4
                }
            };
            DateTime utc = new DateTime(1941, 11, 5, 2, 0, 0, DateTimeKind.Utc);
            if (count > 0) state.Observation1 = Sight(utc, 0, CelestialBody.Sun());
            if (count > 1) state.Observation2 = Sight(utc.AddMinutes(10), 1, CelestialBody.Moon());
            if (count > 2) state.Observation3 = Sight(utc.AddMinutes(11), 2, CelestialBody.Star(24608, "Capella"));
            var track = new CourseSampler();
            for (int i = 0; i < 6; i++)
                track.AddSample(utc.AddSeconds(i * 2), 48 + i * 0.0001, -15);
            state.Course = track.CaptureState();
            return state;
        }

        private static SextantObservation Sight(DateTime utc, int ordinal, CelestialBody body)
        {
            var o = RunningFixCalculator.BuildObservation(utc, 35 + ordinal, 48, -15, body);
            // Pin all fields to non-default distinguishable values so an omitted
            // write cannot pass merely because its initializer is also zero.
            o.HoSigmaArcMin = 0.25 + ordinal;
            o.HoSampleCount = 5 + ordinal;
            o.HoRejectedCount = 2 + ordinal;
            o.IndexErrorArcMin = -0.8 - ordinal;
            return o;
        }
    }
}
