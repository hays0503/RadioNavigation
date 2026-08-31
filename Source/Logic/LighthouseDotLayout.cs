using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure layout math for the screen-space lighthouse dot/streak overlay
    /// (P5.4 LBG-2). Computes positions, sizes and per-layer alphas from the
    /// visual model plus camera/screen inputs. No Unity object mutation and
    /// no game-type references — the caller applies the returned metrics to
    /// its RectTransforms. Unit-tested like LighthouseVisualModel.
    ///
    /// Tuning constants live here as the single source of truth; the glow
    /// controller and its diagnostics reference them through this class.
    /// </summary>
    public static class LighthouseDotLayout
    {
        // Perceptual dot size multiplier (+10%): scales core, halo and
        // streak together; the model's pixel caps still apply via DotSizePx.
        public const float DotSizeScale = 1.1f;
        public const float CoreDotAlpha = 0.95f;
        // Halo is atmospheric scattering (Mie): physically zero in clear air,
        // peaks in fog. Alpha scales with Weather Fogginess — hence low max.
        public const float HaloMaxAlpha = 0.12f;
        public const float StreakAlpha = 0.5f;
        public const float StreakLengthMultiplier = 6.5f;
        public const float StreakThicknessMultiplier = 0.25f;
        public const float StreakFadeNearM = 70f;
        public const float StreakFadeFarM = 200f;

        /// <summary>Computed dot/streak geometry + layer alphas.</summary>
        public struct Metrics
        {
            public float Size;
            // Canvas-anchored center offset (screen minus half canvas).
            public Vector2 CenterOffset;
            public float StreakLength;
            public float StreakThickness;
            public float HorizonAngleDeg;
            public float CoreAlpha;
            public float HaloAlpha;
            public float StreakAlphaFinal;
        }

        /// <summary>
        /// Mirrors the pre-refactor UpdateDot math byte-for-byte. All inputs
        /// are plain floats so the method is pure and testable off-Unity.
        /// </summary>
        public static Metrics Compute(
            float distance,
            float screenX, float screenY,
            float screenWidth, float screenHeight,
            float iVisual,
            float horizonAngleDeg)
        {
            var m = new Metrics();

            float size = (float)LighthouseVisualModel.DotSizePx(distance) * DotSizeScale;
            m.Size = size;
            m.CenterOffset = new Vector2(screenX - screenWidth * 0.5f, screenY - screenHeight * 0.5f);

            float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StreakFadeNearM, StreakFadeFarM, distance));
            m.StreakLength = Mathf.Min(
                size * StreakLengthMultiplier * Mathf.Lerp(0.2f, 1f, f),
                (float)LighthouseVisualModel.StreakLengthCapPx(distance));
            m.StreakThickness = Mathf.Max(2f, size * StreakThicknessMultiplier * Mathf.Lerp(0.2f, 1f, f));
            m.HorizonAngleDeg = horizonAngleDeg;
            m.StreakAlphaFinal = StreakAlpha * iVisual * f;

            m.CoreAlpha = CoreDotAlpha * iVisual;
            m.HaloAlpha = HaloMaxAlpha * iVisual;
            return m;
        }
    }
}
