using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Characterization tests for the pure map-marker sizing math extracted
    /// from LighthouseMapMarker (test-first, before the refactor):
    /// UnitsPerPixel, IconPixelSizeForElevation, MarkerScale.
    /// </summary>
    public sealed class LighthouseMarkerSizingTests
    {
        [Fact]
        public void UnitsPerPixel_At3000_Fov60_1440p_IsAbout2_4()
        {
            double upp = LighthouseMarkerSizing.UnitsPerPixel(3000f, 60f, 1440f);
            Assert.InRange(upp, 2.40, 2.41);
        }

        [Fact]
        public void UnitsPerPixel_ZeroElevation_IsZero()
        {
            Assert.Equal(0.0, LighthouseMarkerSizing.UnitsPerPixel(0f, 60f, 1440f));
        }

        [Fact]
        public void UnitsPerPixel_ZeroScreenHeight_IsZero()
        {
            Assert.Equal(0.0, LighthouseMarkerSizing.UnitsPerPixel(3000f, 60f, 0f));
        }

        [Fact]
        public void IconPixelSize_BelowMinElevation_Is50()
        {
            Assert.Equal(50f, LighthouseMarkerSizing.IconPixelSizeForElevation(1000f));
            Assert.Equal(50f, LighthouseMarkerSizing.IconPixelSizeForElevation(500f));
        }

        [Fact]
        public void IconPixelSize_AtMaxElevation_Is25()
        {
            Assert.Equal(25f, LighthouseMarkerSizing.IconPixelSizeForElevation(1400000f));
            Assert.Equal(25f, LighthouseMarkerSizing.IconPixelSizeForElevation(2000000f));
        }

        [Fact]
        public void IconPixelSize_At3000_Near50()
        {
            Assert.InRange(LighthouseMarkerSizing.IconPixelSizeForElevation(3000f), 49.9f, 50.0f);
        }

        [Fact]
        public void IconPixelSize_MonotonicDecreasing()
        {
            float prev = float.MaxValue;
            foreach (float elevation in new[] { 500f, 1000f, 3000f, 100000f, 700000f, 1400000f, 2000000f })
            {
                float px = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);
                Assert.True(px <= prev, "not decreasing at elevation " + elevation);
                prev = px;
            }
        }

        [Fact]
        public void MarkerScale_ZeroLossy_UsesFloor()
        {
            double upp = LighthouseMarkerSizing.UnitsPerPixel(3000f, 60f, 1440f);
            double scale = LighthouseMarkerSizing.MarkerScale(25f, upp, 0f);
            Assert.True(scale > 0.0);
            Assert.False(double.IsNaN(scale));
            Assert.False(double.IsInfinity(scale));
        }

        [Fact]
        public void MarkerScale_RoundTrip_ReturnsIconPx_AtCloseZoom()
        {
            float elevation = 1000f;
            double upp = LighthouseMarkerSizing.UnitsPerPixel(elevation, 60f, 1440f);
            float px = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);
            double scale = LighthouseMarkerSizing.MarkerScale(px, upp, 1500f);
            double back = (scale * LighthouseMarkerSizing.IconSizeCanvasUnits * 1500f) / upp;
            Assert.InRange(back, 49.9, 50.1);
        }

        [Fact]
        public void MarkerScale_RoundTrip_ReturnsIconPx_AtFarZoom()
        {
            float elevation = 1400000f;
            double upp = LighthouseMarkerSizing.UnitsPerPixel(elevation, 60f, 1440f);
            float px = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);
            double scale = LighthouseMarkerSizing.MarkerScale(px, upp, 1500f);
            double back = (scale * LighthouseMarkerSizing.IconSizeCanvasUnits * 1500f) / upp;
            Assert.InRange(back, 24.9, 25.1);
        }
    }
}
