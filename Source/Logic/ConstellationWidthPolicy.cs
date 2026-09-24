using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// View state that determines the constellation line width: whether the
    /// sextant is open, its zoom divisor and the game's UI scale.
    /// </summary>
    public struct ConstellationViewKey
    {
        public bool Open;
        public float FovDivisor;
        public float UiScale;
    }

    /// <summary>
    /// Ocular-aware constellation line width policy (2026-09-22). The mesh is
    /// one world object rendered by both the main camera and the sextant PIP,
    /// and the PIP zooms (its FOV = mainFov / divisor). The width must follow
    /// the view the player is actually looking at:
    ///  - sextant closed: main camera FOV + its pixel height (status quo);
    ///  - sextant open:   ocular FOV (mainFov / divisor) + the ocular's
    ///    on-screen height in pixels (600 units × uiScale, because the overlay
    ///    canvas scaleFactor equals the game UI scale).
    /// The PIP cameras are deliberately never touched: they can be absent and
    /// their pixel height is the render-target size, not the on-screen size.
    /// Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class ConstellationWidthPolicy
    {
        /// <summary>Legacy ocular size in canvas units (the pre-2026-11-04
        /// constant). The live size is a fraction of the unit height
        /// (SextantOcularSize) and is passed in by the caller.</summary>
        public const double OcularSizeUnits = 600.0;

        /// <summary>Comparison tolerance for the key (uiScale is re-read from
        /// userSettings and may differ by ulps; the divisor is exact).</summary>
        public const float Epsilon = 1e-3f;

        /// <summary>Captures the key, normalising invalid inputs to the defaults
        /// (divisor 1, scale 1) so the mesh never computes a degenerate width.</summary>
        public static ConstellationViewKey Capture(bool open, float fovDivisor, float uiScale)
        {
            var key = new ConstellationViewKey();
            key.Open = open;
            key.FovDivisor = fovDivisor > 0f && !float.IsNaN(fovDivisor) ? fovDivisor : 1f;
            key.UiScale = uiScale > 0f && !float.IsNaN(uiScale) ? uiScale : 1f;
            return key;
        }

        /// <summary>True when the width inputs changed (open edge, zoom step,
        /// UI scale) — the caller then rebuilds immediately, bypassing the
        /// drift throttle so closing the sextant restores the main view in the
        /// same frame.</summary>
        public static bool KeyChanged(ConstellationViewKey current, ConstellationViewKey previous)
        {
            if (current.Open != previous.Open)
                return true;
            if (Math.Abs(current.FovDivisor - previous.FovDivisor) > Epsilon)
                return true;
            return Math.Abs(current.UiScale - previous.UiScale) > Epsilon;
        }

        /// <summary>Rebuild decision: camera movement and the width key rebuild
        /// immediately; the time drift still obeys the existing throttle.</summary>
        public static bool NeedsRebuild(bool cameraMoved, bool keyChanged, bool timeDrift,
            bool throttleElapsed)
        {
            if (cameraMoved || keyChanged)
                return true;
            return timeDrift && throttleElapsed;
        }

        /// <summary>Width inputs for ConstellationLineMath.WorldWidth: the FOV
        /// (radians) and the pixel height of the view that matters. Open uses
        /// the ocular; closed uses the main camera.
        /// <paramref name="ocularSizeUnits"/> is the live ocular side
        /// (SextantOcularSize); the legacy 600 is used when it is invalid.</summary>
        public static void WidthInputs(bool open, double mainFovDeg, double fovDivisor,
            double uiScale, double mainPixelHeight, double ocularSizeUnits,
            out double fovRad, out double screenPixels)
        {
            double divisor = fovDivisor > 0.0 && !double.IsNaN(fovDivisor) ? fovDivisor : 1.0;
            double scale = uiScale > 0.0 && !double.IsNaN(uiScale) ? uiScale : 1.0;
            if (open)
            {
                fovRad = mainFovDeg * Math.PI / 180.0 / divisor;
                double ocular = ocularSizeUnits > 0.0 && !double.IsNaN(ocularSizeUnits)
                    ? ocularSizeUnits : OcularSizeUnits;
                screenPixels = ocular * scale;
                return;
            }

            fovRad = mainFovDeg * Math.PI / 180.0;
            screenPixels = mainPixelHeight;
        }

        /// <summary>Legacy overload (fixed 600-unit ocular).</summary>
        public static void WidthInputs(bool open, double mainFovDeg, double fovDivisor,
            double uiScale, double mainPixelHeight,
            out double fovRad, out double screenPixels)
        {
            WidthInputs(open, mainFovDeg, fovDivisor, uiScale, mainPixelHeight,
                OcularSizeUnits, out fovRad, out screenPixels);
        }
    }
}
