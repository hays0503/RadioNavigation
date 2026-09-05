using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SextantZoomTests
    {
        [Fact]
        public void Default_Is3x()
        {
            var zoom = new SextantZoom();

            Assert.Equal(3f, zoom.FovDivisor);
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

            Assert.True(zoom.ZoomOut());
            Assert.True(zoom.ZoomIn());

            Assert.Equal(SextantZoom.DefaultIndex, zoom.Index);
        }

        [Fact]
        public void PipFov_DividesMainFov()
        {
            var zoom = new SextantZoom();

            Assert.Equal(20f, zoom.PipFov(60f), 4);
        }

        [Fact]
        public void Steps_HasSixEntries()
        {
            Assert.Equal(6, SextantZoom.Steps.Length);
        }
    }
}
