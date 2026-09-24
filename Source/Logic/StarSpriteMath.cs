using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure gaussian PSF field for the star sprite (CELESTIAL_REALISM_PLAN P3).
    /// Unity-free so the shape is pinned offline; StarsMesh turns the field
    /// into a Texture2D (star material blends SrcAlpha/One, so the sprite must
    /// stay straight white — premultiplying would double-dim it).
    /// </summary>
    public static class StarSpriteMath
    {
        public const int SpriteSize = 64;
        public const float SpriteSigma = 0.35f;

        /// <summary>Below this the sprite is clamped to fully transparent
        /// (the gaussian tail would otherwise shimmer with mip-less bilinear).</summary>
        private const float Cutoff = 0.004f;

        /// <summary>Radial gaussian alpha field, row-major, size×size.
        /// r is normalised so the quad spans [−1,1] (corner r² = 2).</summary>
        public static float[] BuildAlphaField(int size, float sigma)
        {
            float[] field = new float[size * size];
            float half = size * 0.5f;
            float inv = 1f / (2f * sigma * sigma);
            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float r2 = dx * dx + dy * dy;
                    float a = (float)Math.Exp(-r2 * inv);
                    field[y * size + x] = a < Cutoff ? 0f : a;
                }
            }
            return field;
        }
    }
}
