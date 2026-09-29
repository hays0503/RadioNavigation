using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure optical PSF fields for the star sprite (CELESTIAL_REALISM_PLAN P3).
    /// Unity-free so the shape is pinned offline; StarsMesh turns the field
    /// into a Texture2D (star material blends SrcAlpha/One, so the sprite must
    /// stay straight white — premultiplying would double-dim it).
    /// </summary>
    public static class StarSpriteMath
    {
        public const int SpriteSize = 64;
        /// <summary>Reference single-Gaussian sigma kept for flux comparisons.</summary>
        public const float SpriteSigma = 0.35f;

        /// <summary>Sharp stellar core sigma in normalized quad coordinates.</summary>
        public const float OpticalCoreSigma = 0.26f;

        /// <summary>Low-level optical scatter halo sigma.</summary>
        public const float OpticalHaloSigma = 0.45f;

        /// <summary>Fraction of the peak PSF contributed by the scatter halo.</summary>
        public const float OpticalHaloWeight = 0.40f;

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

        /// <summary>
        /// Builds a two-scale stellar PSF: a sharper core plus a restrained
        /// scatter halo. The weights sum to one, so the peak stays at one and
        /// the color/alpha carried by <see cref="StarBrightness"/> remains the
        /// brightness control. The defaults keep integrated sprite alpha within
        /// about 3% of the previous sigma-0.35 Gaussian while moving energy from
        /// the broad middle into a crisp core and a faint outer wing.
        /// </summary>
        public static float[] BuildOpticalPsfField(int size)
        {
            return BuildOpticalPsfField(size, OpticalCoreSigma, OpticalHaloSigma,
                OpticalHaloWeight);
        }

        /// <summary>Parameterised form used by the unit tests and future tuning.</summary>
        public static float[] BuildOpticalPsfField(int size, float coreSigma,
            float haloSigma, float haloWeight)
        {
            if (size < 2)
                throw new ArgumentOutOfRangeException("size");
            if (!IsFinitePositive(coreSigma))
                throw new ArgumentOutOfRangeException("coreSigma");
            if (!IsFinitePositive(haloSigma))
                throw new ArgumentOutOfRangeException("haloSigma");
            if (float.IsNaN(haloWeight) || float.IsInfinity(haloWeight)
                || haloWeight < 0f || haloWeight > 1f)
                throw new ArgumentOutOfRangeException("haloWeight");

            float[] field = new float[size * size];
            float half = size * 0.5f;
            float coreInv = 1f / (2f * coreSigma * coreSigma);
            float haloInv = 1f / (2f * haloSigma * haloSigma);
            float coreWeight = 1f - haloWeight;

            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float r2 = dx * dx + dy * dy;
                    float core = (float)Math.Exp(-r2 * coreInv);
                    float halo = (float)Math.Exp(-r2 * haloInv);
                    float alpha = core * coreWeight + halo * haloWeight;
                    field[y * size + x] = alpha < Cutoff ? 0f : alpha;
                }
            }
            return field;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
