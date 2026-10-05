using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class GeoPositionConverterTests
    {
        [Fact]
        public void DegreesPerWorldUnit_MatchesComment()
        {
            Assert.Equal(1.0 / 111.3198888888889, GeoPositionConverter.DegreesPerWorldUnit, 12);
        }

        [Fact]
        public void ToLatLong_Zero_IsZero()
        {
            float lat, lon;

            GeoPositionConverter.ToLatLong(0f, 0f, out lat, out lon);

            Assert.Equal(0f, lat);
            Assert.Equal(0f, lon);
        }

        [Fact]
        public void ToLatLong_SwapsAxesLikeGame()
        {
            float lat, lon;

            GeoPositionConverter.ToLatLong(1000f, 2000f, out lat, out lon);

            Assert.Equal((float)(2000.0 * GeoPositionConverter.DegreesPerWorldUnit), lat);
            Assert.Equal((float)(1000.0 * GeoPositionConverter.DegreesPerWorldUnit), lon);
        }
    }
}
