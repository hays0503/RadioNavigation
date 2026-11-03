using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CelestialWorldReadbackTests
    {
        private sealed class Weather { public object Sky; }
        private sealed class Sky { public object World { get; set; } }
        private sealed class World { public float Latitude = 55; public double Longitude = 4; public int UTC = 0; }

        [Fact]
        public void ReflectionReadsNativeCoordinatesWithoutChangingThem()
        {
            var world = new World(); var sky = new Sky { World = world }; var weather = new Weather { Sky = sky };
            double latitude, longitude, utc;
            Assert.True(CelestialWorldReadback.TryRead(weather, out latitude, out longitude, out utc));
            Assert.Equal(55, latitude); Assert.Equal(4, longitude); Assert.Equal(0, utc);
            Assert.Same(world, sky.World); Assert.Equal(55, world.Latitude); Assert.Equal(4, world.Longitude);
        }

        [Fact]
        public void MissingOrRenamedMembersDoNotInventZeroCoordinates()
        {
            double latitude, longitude, utc;
            Assert.False(CelestialWorldReadback.TryRead(null, out latitude, out longitude, out utc));
            Assert.True(double.IsNaN(latitude));
            Assert.False(CelestialWorldReadback.TryRead(new Weather { Sky = new Sky() }, out latitude, out longitude, out utc));
            Assert.False(CelestialWorldReadback.TryRead(new Weather { Sky = new Sky { World = new object() } }, out latitude, out longitude, out utc));
        }

        [Fact]
        public void NonfiniteCoordinatesAreNotReportedReady()
        {
            double latitude, longitude, utc;
            var weather = new Weather { Sky = new Sky { World = new World { Latitude = float.NaN } } };
            Assert.False(CelestialWorldReadback.TryRead(weather, out latitude, out longitude, out utc));
        }
    }
}
