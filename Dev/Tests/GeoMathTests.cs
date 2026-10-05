using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class GeoMathTests
    {
        [Fact]
        public void ToRadians_Zero_Zero()
        {
            Assert.Equal(0.0, GeoMath.ToRadians(0f), 12);
        }

        [Fact]
        public void ToRadians_HalfTurn_Pi()
        {
            Assert.Equal(Math.PI, GeoMath.ToRadians(180f), 10);
        }

        [Fact]
        public void HaversineKm_SamePoint_Zero()
        {
            Assert.Equal(0f, GeoMath.HaversineKm(50f, 10f, 50f, 10f), 4);
        }

        [Fact]
        public void HaversineKm_NearIdenticalPoints_NoNan()
        {
            // F80: rounding must not push the Haversine sum above 1.0.
            float d = GeoMath.HaversineKm(0.001f, 0.001f, 0.0010001f, 0.0010001f);
            Assert.False(float.IsNaN(d));
            Assert.True(d >= 0f);
        }

        [Fact]
        public void HaversineKm_OneDegreeLatitude_About111Km()
        {
            float d = GeoMath.HaversineKm(50f, 0f, 51f, 0f);
            Assert.InRange(d, 110f, 113f);
        }

        [Fact]
        public void InitialBearingDeg_Northbound_Zero()
        {
            Assert.Equal(0f, GeoMath.InitialBearingDeg(50f, 10f, 51f, 10f), 2);
        }

        [Fact]
        public void InitialBearingDeg_Eastbound_Ninety()
        {
            Assert.InRange(GeoMath.InitialBearingDeg(50f, 10f, 50f, 11f), 89f, 91f);
        }

        [Fact]
        public void InitialBearingDeg_Southbound_OneEighty()
        {
            Assert.Equal(180f, GeoMath.InitialBearingDeg(51f, 10f, 50f, 10f), 2);
        }

        [Fact]
        public void InitialBearingDeg_Westbound_TwoSeventy()
        {
            Assert.InRange(GeoMath.InitialBearingDeg(50f, 11f, 50f, 10f), 269f, 271f);
        }

        [Fact]
        public void InitialBearingDeg_ResultNormalizedTo360()
        {
            // bearing -90 (westbound from the formula) must wrap to 270.
            float b = GeoMath.InitialBearingDeg(50f, 11f, 50f, 10f);
            Assert.InRange(b, 0f, 360f);
        }

        [Fact]
        public void MapBearingDeg_UsesFlatGameMapProjection()
        {
            float b = GeoMath.MapBearingDeg(58.625556f, 5.630278f, 54.09821f, 7.73621f);
            Assert.InRange(b, 154.5f, 155.5f);
        }
    }
}
