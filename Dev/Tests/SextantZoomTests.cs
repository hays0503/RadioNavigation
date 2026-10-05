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

        [Fact]
        public void Label_ReadsTheCurrentStep()
        {
            var zoom = new SextantZoom();

            // Default is the no-zoom end.
            Assert.Equal("x1", zoom.Label);
            Assert.True(zoom.ZoomIn());
            Assert.Equal("x1.5", zoom.Label);
            Assert.True(zoom.ZoomIn());
            Assert.Equal("x2", zoom.Label);
            Assert.True(zoom.ZoomIn());
            Assert.Equal("x3", zoom.Label);
            Assert.True(zoom.ZoomIn());
            Assert.Equal("x4.5", zoom.Label);
            Assert.True(zoom.ZoomIn());
            Assert.Equal("x6", zoom.Label);
            // Clamped at the tight end: the label stays.
            Assert.False(zoom.ZoomIn());
            Assert.Equal("x6", zoom.Label);
        }

        [Fact]
        public void Label_UsesInvariantDecimalSeparator()
        {
            // The OS locale may be Russian ("4,5" would break the readout).
            var zoom = new SextantZoom();
            while (zoom.FovDivisor != 4.5f)
                Assert.True(zoom.ZoomIn());
            Assert.Equal("x4.5", zoom.Label);
        }
    }
}
