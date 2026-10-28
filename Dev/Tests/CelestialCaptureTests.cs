using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>Execute the actual Worksheet → BuildObservation → codec chain.
    /// Only clock, world position, UI and map endpoints are fake. These tests
    /// are not Unity/GPU or horizon-reduction acceptance.</summary>
    public sealed class CelestialCaptureTests
    {
        private sealed class Position : IPlayerPositionProvider
        {
            public bool TryGetPosition(out float lat, out float lon)
            {
                lat = 55 + 10f / 60;
                lon = 5 + 40f / 60;
                return true;
            }
        }

        [Theory]
        [InlineData(CelestialBodyKind.Moon, 0.0)]
        [InlineData(CelestialBodyKind.Moon, 0.523)]
        [InlineData(CelestialBodyKind.Moon, 0.543)]
        [InlineData(CelestialBodyKind.Moon, 2.153125)]
        [InlineData(CelestialBodyKind.Star, 0.0)]
        [InlineData(CelestialBodyKind.Star, 0.523)]
        [InlineData(CelestialBodyKind.Star, 0.543)]
        [InlineData(CelestialBodyKind.Star, 2.153125)]
        public void CelestialCapture_AppliesIndexOnlyAndBodyGatesStoredProvenance(
            CelestialBodyKind kind, double diameter)
        {
            DateTime oldTime = GameTimeResolver.Now;
            double oldCalibration = SunEphemerisCalibration.DeltaMinutes;
            double oldIndex = IndexErrorCorrection.Load();
            try
            {
                GameTimeResolver.Now = new DateTime(1939, 9, 2,
                    kind == CelestialBodyKind.Moon ? 22 : 10, 3, 7, DateTimeKind.Utc);
                SunEphemerisCalibration.SetDelta(0);
                IndexErrorCorrection.Save(2.4);
                CelestialBody body = kind == CelestialBodyKind.Sun ? CelestialBody.Sun()
                    : kind == CelestialBodyKind.Moon ? CelestialBody.Moon()
                    : CelestialBody.Star(11767, "Polaris");
                var worksheet = new SextantWorksheet(new Position());
                var disc = new SunDiscSighting { ObservedDiameterDeg = diameter, Limb = SunSightLimb.UpperLimb };
                Assert.True(worksheet.CaptureObservation(body, 30.0, 0.25, 4, 1, disc));
                SextantObservation sight = worksheet.Calculator.Observation1;
                Assert.Equal(30.04, sight.HoDeg, 10);
                // PlayerPrefs stores a float: do not mistake its 1e-7' roundoff
                // for a measurement-chain defect.
                Assert.InRange(sight.IndexErrorArcMin, 2.399999, 2.400001);
                Assert.Equal(0.0, sight.SunDiscCompensationArcMin);
                Assert.Equal(kind == CelestialBodyKind.Sun ? diameter : 0.0,
                    sight.ObservedSunAngularDiameterDeg);
                Assert.Equal(kind == CelestialBodyKind.Sun ? SunSightLimb.UpperLimb : SunSightLimb.LowerLimb,
                    sight.SunLimb);
                string frozen = SextantSaveCodec.Encode(worksheet.CaptureState());
                GameTimeResolver.Now = GameTimeResolver.Now.AddDays(120);
                SunEphemerisCalibration.SetDelta(-6.47);
                IndexErrorCorrection.Save(-3.2);
                Assert.Equal(frozen, SextantSaveCodec.Encode(worksheet.CaptureState()));
                SextantSaveSnapshot restored;
                string error;
                Assert.True(SextantSaveCodec.TryDecode(frozen, out restored, out error), error);
                Assert.Equal(sight.HoDeg, restored.Observation1.HoDeg);
                Assert.Equal(sight.HcDeg, restored.Observation1.HcDeg);
                Assert.Equal(sight.InterceptNm, restored.Observation1.InterceptNm);
                Assert.Equal(sight.ObservedSunAngularDiameterDeg, restored.Observation1.ObservedSunAngularDiameterDeg);
            }
            finally
            {
                GameTimeResolver.Now = oldTime;
                SunEphemerisCalibration.SetDelta(oldCalibration);
                IndexErrorCorrection.Save(oldIndex);
            }
        }

        [Theory]
        [InlineData(CelestialBodyKind.Star, 0)]
        [InlineData(CelestialBodyKind.Star, int.MaxValue)]
        [InlineData((CelestialBodyKind)99, 0)]
        public void CelestialCapture_UnknownEphemerisCannotFallBackToSun(CelestialBodyKind kind, int hip)
        {
            var worksheet = new SextantWorksheet(new Position());
            Assert.True(worksheet.CaptureObservation(CelestialBody.Star(11767, "Polaris"), 30));
            string before = SextantSaveCodec.Encode(worksheet.CaptureState());
            var unknown = new CelestialBody { Kind = kind, Hip = hip, Name = "Unknown" };
            Assert.False(worksheet.CaptureObservation(unknown, 38));
            Assert.Equal(before, SextantSaveCodec.Encode(worksheet.CaptureState()));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.523)]
        [InlineData(0.543)]
        [InlineData(2.153125)]
        public void Sh5_NewSunCannotUseTheLegacyUnsourcedCaptureOverload(double diameter)
        {
            var worksheet = new SextantWorksheet(new Position());
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38));
            Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 38, 0, 1, 0,
                new SunDiscSighting { ObservedDiameterDeg = diameter, Limb = SunSightLimb.LowerLimb }));
            Assert.Null(worksheet.Calculator.Observation1);
        }
    }
}
