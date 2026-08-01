using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class PlayerPositionProviderTests
    {
        // F200: the game tracks the player ship in world units (1 unit equals
        // 1/111.3198888888889 degree, mirrored by WorldPosition.FromCoordinates).
        [Fact]
        public void ToLatLong_OneDegree_Converts()
        {
            GeoPositionConverter.ToLatLong(111.3198888888889f, 111.3198888888889f, out float lat, out float lon);
            Assert.Equal(1f, lat, 4);
            Assert.Equal(1f, lon, 4);
        }

        [Fact]
        public void ToLatLong_NegativeUnits_ConvertsToSouthernHemisphere()
        {
            GeoPositionConverter.ToLatLong(-111.3198888888889f, -55.65994444444445f, out float lat, out float lon);
            Assert.Equal(-0.5f, lat, 4);
            Assert.Equal(-1f, lon, 4);
        }

        [Fact]
        public void ToLatLong_Zero_IsZero()
        {
            GeoPositionConverter.ToLatLong(0f, 0f, out float lat, out float lon);
            Assert.Equal(0f, lat, 4);
            Assert.Equal(0f, lon, 4);
        }

        [Fact]
        public void ToLatLong_RoundTrip_FromCoordinates()
        {
            float latDeg = 54.5f;
            float lonDeg = 12.25f;
            GeoPositionConverter.ToLatLong(
                lonDeg * 111.3198888888889f, latDeg * 111.3198888888889f,
                out float lat, out float lon);
            Assert.Equal(latDeg, lat, 4);
            Assert.Equal(lonDeg, lon, 4);
        }
    }
}
