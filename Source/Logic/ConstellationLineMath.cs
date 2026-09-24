using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure math for the constellation polylines (CELESTIAL_REALISM_PLAN P7):
    /// the horizon clip and the minimum on-screen line width. Unity-free so
    /// both are pinned offline (the mesh itself is Unity-bound).
    /// </summary>
    public static class ConstellationLineMath
    {
        /// <summary>
        /// Visible parameter range of a segment whose endpoints have the given
        /// altitudes: [t0, t1] ⊂ [0, 1] where the linearly interpolated
        /// altitude stays above the horizon. False when the whole segment is
        /// below it. Without this the mesh joined the surviving vertices and
        /// drew a chord across the sky.
        /// </summary>
        public static bool ClipToHorizon(double alt0Deg, double alt1Deg,
            out double t0, out double t1)
        {
            t0 = 0.0;
            t1 = 1.0;
            if (alt0Deg > 0.0 && alt1Deg > 0.0)
                return true;
            if (alt0Deg <= 0.0 && alt1Deg <= 0.0)
                return false;

            double crossing = alt0Deg / (alt0Deg - alt1Deg);
            if (crossing < 0.0) crossing = 0.0;
            if (crossing > 1.0) crossing = 1.0;
            if (alt0Deg > 0.0)
                t1 = crossing;
            else
                t0 = crossing;
            return true;
        }

        /// <summary>
        /// Line width in world units: the larger of the fixed angular width
        /// and the width that keeps the line at least minPixels on screen.
        /// The old code used the angular width alone, which at 2′ is ~0.26 px
        /// in the 512² ocular at ×1 — invisible.
        /// </summary>
        public static float WorldWidth(double angularWidthRad, double distance,
            double fovRad, double screenPixels, double minPixels)
        {
            double angularWorld = angularWidthRad * distance;
            if (screenPixels <= 1.0 || fovRad <= 0.0 || distance <= 0.0)
                return (float)angularWorld;
            double pixelsWorld = minPixels * 2.0 * distance * Math.Tan(fovRad * 0.5) / screenPixels;
            return (float)Math.Max(angularWorld, pixelsWorld);
        }

        /// <summary>Horizon fade for a line vertex: 0 at the horizon, 1 at
        /// fadeDeg above it (the old code cut the line dead at alt ≤ 0).</summary>
        public static float HorizonFade(double altDeg, double fadeDeg)
        {
            if (altDeg <= 0.0)
                return 0f;
            if (fadeDeg <= 0.0 || altDeg >= fadeDeg)
                return 1f;
            return (float)(altDeg / fadeDeg);
        }
    }
}
