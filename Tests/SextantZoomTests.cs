using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SextantZoomTests
    {
        [Fact]
        public void Default_Is1x_NoZoom()
        {
            var zoom = new SextantZoom();

            Assert.Equal(1f, zoom.FovDivisor);
            Assert.Equal(SextantZoom.DefaultIndex, zoom.Index);
        }

        [Fact]
        public void ZoomIn_AtMin_Stays()
        {
            var zoom = new SextantZoom();

            while (zoom.ZoomIn()) { }

            Assert.Equal(0, zoom.Index);
            Assert.False(zoom.ZoomIn());
            Assert.Equal(0, zoom.Index);
        }

        [Fact]
        public void ZoomOut_AtMax_Stays()
        {
            var zoom = new SextantZoom();

            while (zoom.ZoomOut()) { }

            Assert.Equal(SextantZoom.Steps.Length - 1, zoom.Index);
            Assert.False(zoom.ZoomOut());
        }

        [Fact]
        public void ZoomInOut_RoundTrips()
        {
            var zoom = new SextantZoom();

            // The default IS the no-zoom end: zooming out is a no-op there.
            Assert.False(zoom.ZoomOut());
            Assert.True(zoom.ZoomIn());
            Assert.True(zoom.ZoomOut());

            Assert.Equal(SextantZoom.DefaultIndex, zoom.Index);
        }

        [Fact]
        public void PipFov_DividesMainFov()
        {
            var zoom = new SextantZoom();

            // No zoom by default: the ocular FOV equals the main camera FOV.
            Assert.Equal(60f, zoom.PipFov(60f), 4);

            // Three steps in = 3x (steps: 6, 4.5, 3, ...).
            Assert.True(zoom.ZoomIn());
            Assert.True(zoom.ZoomIn());
            Assert.True(zoom.ZoomIn());
            Assert.Equal(3f, zoom.FovDivisor);
            Assert.Equal(20f, zoom.PipFov(60f), 4);
        }

        [Fact]
        public void Steps_HasSixEntries()
        {
            Assert.Equal(6, SextantZoom.Steps.Length);
        }
    }
}
