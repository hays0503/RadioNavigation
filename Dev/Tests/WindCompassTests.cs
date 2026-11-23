using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class WindCompassTests
    {
        [Theory]
        [InlineData(1f, 0f, 270f)]     // air moves east, comes from west
        [InlineData(0f, 1f, 180f)]     // air moves north, comes from south
        [InlineData(-1f, 0f, 90f)]     // air moves west, comes from east
        [InlineData(0f, -1f, 0f)]      // air moves south, comes from north
        [InlineData(-1f, 1f, 135f)]    // south-west flow, comes from south-east
        public void BearingPointsAtTheSource(float east, float north, float expected)
        {
            Assert.Equal(expected, WindCompass.BearingFrom(east, north), 3);
        }

        [Theory]
        [InlineData(0f, "С")]
        [InlineData(22.4f, "С")]
        [InlineData(22.5f, "СВ")]
        [InlineData(90f, "В")]
        [InlineData(180f, "Ю")]
        [InlineData(225f, "ЮЗ")]
        [InlineData(270f, "З")]
        [InlineData(337.4f, "СЗ")]
        [InlineData(337.5f, "С")]
        [InlineData(359.9f, "С")]
        public void EightPointSectorsAreCentredOnTheirBearing(float bearing, string expected)
        {
            Assert.Equal(expected, WindCompass.PointName(bearing, true));
        }

        [Theory]
        [InlineData(0f, 0f, "")]
        [InlineData(0.1f, 0.1f, "")]
        [InlineData(float.NaN, 1f, "")]
        [InlineData(0f, 1f, "180° Ю")]
        [InlineData(1f, 0f, "270° З")]
        [InlineData(-1f, 0f, "90° В")]
        public void SuffixIsEmptyForCalmAndNamesTheSource(float east, float north, string expected)
        {
            Assert.Equal(expected, WindCompass.Suffix(east, north, true));
        }

        [Fact]
        public void EnglishSuffixUsesTheSameSectors()
        {
            Assert.Equal("225° SW", WindCompass.Suffix(1f, 1f, false));
            Assert.Equal("180° S", WindCompass.Suffix(0f, 1f, false));
        }

        [Fact]
        public void DegreesAreRoundedWithoutProducingThreeSixty()
        {
            float bearing = WindCompass.BearingFrom(0.0001f, -1f);
            Assert.Equal(0, WindCompass.Point(bearing));
            Assert.Equal("0° С", WindCompass.Suffix(0.0001f, -1f, true));
        }

        [Theory]
        [InlineData(-4f, true)]
        [InlineData(0f, true)]
        [InlineData(0.5f, true)]
        [InlineData(0.6f, false)]
        [InlineData(7f, false)]
        [InlineData(float.NaN, false)]
        public void OnlyTheSurfaceShowsTheDirection(float depth, bool expected)
        {
            Assert.Equal(expected, WindCompass.IsSurfaced(depth));
        }

        [Theory]
        [InlineData("8 уз", true)]
        [InlineData("15 км/ч", true)]
        [InlineData("8 knots", false)]
        [InlineData("", true)]
        public void PointNamesFollowTheLocalisedLabel(string text, bool russian)
        {
            Assert.Equal(russian, WindCompass.PrefersRussian(text));
        }
    }
}
