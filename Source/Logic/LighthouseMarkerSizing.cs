using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure sizing math for the lighthouse map marker (ADR-027), extracted
    /// from LighthouseMapMarker.Update so it can be unit-tested. Uses
    /// System.Math only — no UnityEngine dependencies (the mod's test
    /// project stubs cover only a fraction of Mathf).
    ///
    /// The icon renders at a zoom-dependent pixel size: 1× base = 25 px at
    /// the farthest zoom, scaling linearly up to 50×50 px at the closest
    /// zoom. The marker scale is derived from the camera FOV, elevation and
    /// screen height so the icon renders exactly that many pixels.
    /// </summary>
    public static class LighthouseMarkerSizing
    {
        public const float IconSizeCanvasUnits = 64f;
        public const float MinIconPixelSize = 25f;
        public const float MaxIconPixelSize = 50f;
        public const float MinElevationForSize = 1000f;
        public const float MaxElevationForSize = 1400000f;
        public const float DefaultFovDegrees = 60f;
        public const float MinParentLossyScale = 0.0001f;

        /// <summary>
        /// World units per screen pixel at the given camera elevation/FOV:
        /// vertical view height (2·elevation·tan(fov/2)) over screen height.
        /// </summary>
        public static double UnitsPerPixel(float elevation, float fovDegrees, float screenHeight)
        {
            if (elevation <= 0f || screenHeight <= 0f)
            {
                return 0.0;
            }
            double halfFov = fovDegrees * 0.5 * (Math.PI / 180.0);
            return (2.0 * elevation * Math.Tan(halfFov)) / screenHeight;
        }

        /// <summary>
        /// Icon pixel size for the current zoom: 50 px at elevation ≤
        /// MinElevationForSize, 25 px at elevation ≥ MaxElevationForSize,
        /// linear in between, clamped outside the range.
        /// </summary>
        public static float IconPixelSizeForElevation(float elevation)
        {
            float t = (elevation - MinElevationForSize) / (MaxElevationForSize - MinElevationForSize);
            t = Math.Max(0f, Math.Min(1f, t));
            return MaxIconPixelSize + (MinIconPixelSize - MaxIconPixelSize) * t;
        }

        /// <summary>
        /// Marker local scale for the given pixel target: iconPx world size
        /// over (icon canvas size × parent lossy scale). A non-positive
        /// parent lossy scale falls back to MinParentLossyScale to avoid
        /// division by zero.
        /// </summary>
        public static double MarkerScale(float iconPx, double unitsPerPixel, float parentLossyScale)
        {
            double lossy = parentLossyScale > 0f ? parentLossyScale : MinParentLossyScale;
            return (iconPx * unitsPerPixel) / (IconSizeCanvasUnits * lossy);
        }
    }
}
