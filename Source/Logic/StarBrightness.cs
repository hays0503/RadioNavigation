using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Star photometry (CELESTIAL_REALISM_PLAN P3): Pogson's law instead of the
    /// old linear magnitude lerp. alpha(m) = 2.512^(-(m+1.5)/2) is the flux
    /// ratio square-root-compressed (visually correct for additive blending),
    /// size(m) is log-linear with the same slope. Navigation stars get ONE
    /// boost (~+0.75 mag of flux) instead of the old stacked ×1.9 size/×1.5
    /// alpha, which flattened the naked-eye hierarchy. Pure math, no Unity.
    /// </summary>
    public static class StarBrightness
    {
        public const float MinSizePx = 2f;
        public const float MaxSizePx = 20f;
        public const float NavSizeFactor = 1.3f;
        public const float NavAlphaFactor = 1.2f;

        private const float DimMag = 6f;
        private const float BrightMag = -1.5f;

        /// <summary>Relative flux of a V magnitude (2.512^-m).</summary>
        public static double Flux(double vmag)
        {
            return Math.Pow(2.512, -vmag);
        }

        /// <summary>Sprite alpha: sqrt-compressed flux — 1.000 at mag −1.5,
        /// 0.501 at 0, 0.032 at 6 (each +1 mag = ×0.631).</summary>
        public static float Alpha(float vmag)
        {
            double a = Math.Pow(2.512, -(vmag + 1.5) / 2.0);
            if (a < 0.0) a = 0.0;
            if (a > 1.0) a = 1.0;
            return (float)a;
        }

        /// <summary>Log-linear sprite size in pixels: 20 px at mag −1.5,
        /// 17.4 px at 0, 2 px at 6 (size = 2 + 18·((6−m)/7.5)^0.7).</summary>
        public static float SizePx(float vmag)
        {
            double t = (DimMag - vmag) / (DimMag - BrightMag);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return (float)(MinSizePx + (MaxSizePx - MinSizePx) * Math.Pow(t, 0.7));
        }

        /// <summary>Final size/alpha with the navigation-star boost
        /// (×1.3 size, ×1.2 alpha ≈ +0.75 mag of flux).</summary>
        public static void BrightnessFor(float vmag, bool isNav, out float sizePx, out float alpha)
        {
            sizePx = SizePx(vmag);
            alpha = Alpha(vmag);
            if (isNav)
            {
                sizePx *= NavSizeFactor;
                alpha *= NavAlphaFactor;
                if (alpha > 1f) alpha = 1f;
            }
        }
    }
}
