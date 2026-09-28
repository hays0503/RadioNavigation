using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Converts a screen-pixel size into world units on the sky shell.
    ///
    /// StarBrightness reports star sizes in SCREEN pixels; anything drawn on a
    /// shell at distance R must be scaled through the projection or it becomes
    /// sub-pixel and invisible: at R = 3825 m with a 65 deg field of view the
    /// old "pixels * 0.1" quads were ~0.05 px wide (measured live 2026-09-28).
    /// Unity-free so the mapping is testable.
    /// </summary>
    public static class StarQuadScale
    {
        /// <summary>
        /// World units per screen pixel at <paramref name="distanceM"/> for a
        /// camera with a vertical field of view and a viewport height in pixels:
        /// 2 * R * tan(fov/2) / viewportHeight.
        /// </summary>
        public static float WorldUnitsPerPixel(double distanceM, double verticalFovDeg,
            int viewportHeightPixels)
        {
            if (double.IsNaN(distanceM) || double.IsInfinity(distanceM) || distanceM <= 0d)
                throw new ArgumentOutOfRangeException("distanceM");
            if (double.IsNaN(verticalFovDeg) || double.IsInfinity(verticalFovDeg)
                || verticalFovDeg <= 0d || verticalFovDeg >= 179d)
                throw new ArgumentOutOfRangeException("verticalFovDeg");
            if (viewportHeightPixels <= 0)
                throw new ArgumentOutOfRangeException("viewportHeightPixels");

            double halfFov = verticalFovDeg * 0.5d * Math.PI / 180d;
            double worldHeight = 2d * distanceM * Math.Tan(halfFov);
            return (float)(worldHeight / viewportHeightPixels);
        }
    }
}
