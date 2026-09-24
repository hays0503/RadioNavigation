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
        /// <summary>
        /// Procedural marker glyph: a filled core plus a concentric ring. Used
        /// as the icon fallback when the bundle predates the marker prefabs
        /// (live 2026-09-22: IconSextan/IconSonne/IconLighthouse all missing,
        /// which left every map marker blank).
        /// </summary>
        public static Color32[] RenderIconPixels(int size)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            const float coreR = 0.34f;
            const float ringInner = 0.58f;
            const float ringOuter = 0.82f;
            const float soft = 0.06f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float core = 1f - Smoothstep(r, coreR - soft, coreR + soft);
                    float ring = Smoothstep(r, ringInner - soft, ringInner + soft)
                        * (1f - Smoothstep(r, ringOuter - soft, ringOuter + soft));
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(core + ring) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            return pixels;
        }

        private static float Smoothstep(float value, float edge0, float edge1)
        {
            if (edge1 <= edge0)
                return value < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
