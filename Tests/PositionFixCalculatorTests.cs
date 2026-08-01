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
