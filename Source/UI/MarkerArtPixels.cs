using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure procedural art for map markers: the soft radial backdrop
    /// falloff (opaque white core fading to a transparent rim), computed as
    /// a Color32 array with no Unity objects so it runs offline in tests.
    /// Extracted from NavMapMarkerBase.GetBackdropSprite (SRP: pixel math
    /// is not view assembly). The base keeps a thin caching wrapper that
    /// uploads the pixels into the shared sprite (SourceCheck gate
    /// MarkerArtPixels_UsedByBackdropSprite pins the wiring).
    /// </summary>
    internal static class MarkerArtPixels
    {
        /// <summary>
        /// Renders size×size backdrop pixels, row-major. Falloff mirrors the
        /// original inline loop exactly: r >= 1 -> 0, else (1-r)^2.
        /// </summary>
        public static Color32[] RenderBackdropPixels(int size)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = r >= 1f ? 0f : (1f - r) * (1f - r);
                    byte a = (byte)Mathf.RoundToInt(falloff * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            return pixels;
        }
    }
}
