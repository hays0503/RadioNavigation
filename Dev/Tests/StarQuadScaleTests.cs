using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the screen-pixel to world-units mapping used by the point-star
    /// layer. The old fixed factor produced sub-pixel quads and an invisible
    /// sky (live 2026-09-28).
    /// </summary>
    public sealed class StarQuadScaleTests
    {
        [Fact]
        public void SkyShell_MapsOnePixelToTheProjectedWorldSize()
        {
            // 3825 m shell, 65 deg vertical FOV, 1267 px viewport:
            // 2 * 3825 * tan(32.5 deg) / 1267 = 3.847 m per pixel.
            double units = StarQuadScale.WorldUnitsPerPixel(3825d, 65d, 1267);
            Assert.Equal(3.847d, units, 3);
        }

        [Fact]
        public void BrightStarQuad_IsVisibleInPixels()
        {
            // A 20 px star on that shell must be ~77 m wide, i.e. its projected
            // size is 20 px again (round trip).
            double units = StarQuadScale.WorldUnitsPerPixel(3825d, 65d, 1267);
            double quadMeters = 20d * units;
            double halfFov = 65d * 0.5d * Math.PI / 180d;
            double pixels = quadMeters / (2d * 3825d * Math.Tan(halfFov)) * 1267d;
            Assert.Equal(20d, pixels, 6);
            Assert.True(quadMeters > 70d, "A point quad on the shell must be tens of metres, not sub-metre.");
        }

        [Fact]
        public void Scale_GrowsWithDistanceAndShrinksWithViewport()
        {
            double near = StarQuadScale.WorldUnitsPerPixel(1000d, 65d, 1000);
            double far = StarQuadScale.WorldUnitsPerPixel(4000d, 65d, 1000);
            Assert.Equal(4d, far / near, 6);

            double wide = StarQuadScale.WorldUnitsPerPixel(3825d, 90d, 1000);
            double narrow = StarQuadScale.WorldUnitsPerPixel(3825d, 45d, 1000);
            Assert.True(wide > narrow);
        }

        [Fact]
        public void Scale_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarQuadScale.WorldUnitsPerPixel(0d, 65d, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarQuadScale.WorldUnitsPerPixel(1000d, 0d, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarQuadScale.WorldUnitsPerPixel(1000d, 65d, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarQuadScale.WorldUnitsPerPixel(double.NaN, 65d, 1000));
        }
    }
}
