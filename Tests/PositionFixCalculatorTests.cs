using System;
using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class PositionFixCalculatorTests
    {
        private static PositionFixCalculator.Measurement M(float lat, float lon, float brng) =>
            new PositionFixCalculator.Measurement { StationIndex = 0, Latitude = lat, Longitude = lon, Bearing = brng, Weight = 1f };

        [Fact]
        public void Calculate_NullMeasurements_ReturnsInvalid()
        {
            var r = PositionFixCalculator.Calculate(null);
            Assert.False(r.Valid);
        }

        [Fact]
        public void Calculate_EmptyMeasurements_ReturnsInvalid()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>());
            Assert.False(r.Valid);
        }

        [Fact]
        public void Calculate_SingleMeasurement_ReturnsInvalid()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement> { M(50f, 10f, 45f) });
            Assert.False(r.Valid);
        }

        [Fact]
        public void Calculate_TwoMeasurementsAtSamePoint_ReturnsThatPoint()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement> { M(0f, 0f, 45f), M(0f, 0f, 135f) });
            Assert.True(r.Valid);
            Assert.Equal(0f, r.Latitude, 3);
            Assert.Equal(0f, r.Longitude, 3);
            Assert.True(r.ErrorKm >= 0f);
        }

        [Fact]
        public void Calculate_NormalCase_CoordinatesInValidRanges()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>
            {
                M(50f, 0f, 90f),
                M(52f, 4f, 180f)
            });
            Assert.True(r.Valid);
            Assert.InRange(r.Latitude, -90f, 90f);
            Assert.InRange(r.Longitude, -180f, 180f);
            Assert.False(float.IsNaN(r.Latitude));
            Assert.False(float.IsNaN(r.Longitude));
        }

        [Fact]
        public void Calculate_InconsistentBearings_ErrorKmPositive()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>
            {
                M(48f, 8f, 34.52f),
                M(52f, 9f, 164.14f),
                M(49f, 12f, 310.35f),
                M(51f, 11f, 214.85f)
            });
            Assert.True(r.Valid);
            Assert.True(r.ErrorKm > 0.5f, "ErrorKm=" + r.ErrorKm);
            Assert.False(float.IsNaN(r.ErrorKm));
        }

        [Fact]
        public void Calculate_PerfectIntersection_ErrorKmNearZero()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>
            {
                M(0f, 0f, 45f),
                M(0f, 0f, 135f),
                M(0f, 0f, 225f)
            });
            Assert.True(r.Valid);
            Assert.True(r.ErrorKm < 0.5f, "ErrorKm=" + r.ErrorKm);
        }

        [Fact]
        public void Calculate_DoesNotMutateInputMeasurements()
        {
            var measurements = new List<PositionFixCalculator.Measurement>
            {
                M(50f, 0f, 90f),
                M(52f, 4f, 180f),
                M(48f, -2f, 0f)
            };
            var snapshot = new List<PositionFixCalculator.Measurement>(measurements);
            _ = PositionFixCalculator.Calculate(measurements);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Assert.Equal(snapshot[i].Weight, measurements[i].Weight);
                Assert.Equal(snapshot[i].Bearing, measurements[i].Bearing);
            }
        }

        // F128: stations straddling the antimeridian (179.5E / 179.5W)
        // must intersect near lon=180, not at a mirrored false point.
        [Fact]
        public void Calculate_AntimeridianStations_IntersectsNearDateline()
        {
            var r = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>
            {
                M(60f, 179.5f, 45f),
                M(60f, -179.5f, 315f)
            });
            Assert.True(r.Valid);
            Assert.False(float.IsNaN(r.Latitude));
            Assert.False(float.IsNaN(r.Longitude));
            Assert.InRange(r.Longitude, -180f, 180f);
            Assert.True(Math.Abs(Math.Abs(r.Longitude) - 180f) < 1f, "lon=" + r.Longitude);
            Assert.InRange(r.Latitude, 59.5f, 61.5f);
        }

        [Fact]
        public void Calculate_StressCases_NeverNaN()
        {
            var cases = new List<List<PositionFixCalculator.Measurement>>
            {
                new List<PositionFixCalculator.Measurement> { M(89f, 179f, 0f), M(89f, -179f, 90f) },
                new List<PositionFixCalculator.Measurement> { M(0.001f, 0.001f, 10f), M(0.002f, 0.002f, 190f) },
                new List<PositionFixCalculator.Measurement> { M(-89f, -179.9f, 270f), M(-89f, 179.9f, 90f) },
                new List<PositionFixCalculator.Measurement> { M(45f, 10f, 350f), M(46f, 11f, 170f), M(44f, 9f, 260f) }
            };
            foreach (var measurements in cases)
            {
                var r = PositionFixCalculator.Calculate(measurements);
                if (!r.Valid)
                    continue;
                Assert.False(float.IsNaN(r.Latitude));
                Assert.False(float.IsNaN(r.Longitude));
                Assert.False(float.IsNaN(r.ErrorKm));
            }
        }

        [Fact]
        public void Calculate_AntipodalMeasurements_DoesNotThrow()
        {
            _ = PositionFixCalculator.Calculate(new List<PositionFixCalculator.Measurement>
            {
                M(0f, 0f, 0f),
                M(0f, 180f, 0f)
            });
        }

        [Fact]
        public void GetDistanceKm_SamePoint_Zero()
        {
            Assert.Equal(0f, PositionFixCalculator.GetDistanceKm(0f, 0f, 0f, 0f), 4);
        }

        [Fact]
        public void GetDistanceKm_OneDegreeLatitude_About111Km()
        {
            float d = PositionFixCalculator.GetDistanceKm(50f, 0f, 51f, 0f);
            Assert.InRange(d, 110f, 113f);
        }
    }
}
