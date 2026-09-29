using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>RGBA sample for the procedural sextant ocular rim.</summary>
    public struct SextantRimSample
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    /// <summary>
    /// Unity-free radial profile for the ocular edge overlay. The broad black
    /// tube falloff is retained; a narrow, directionally lit graphite bevel is
    /// added at the aperture edge so the field reads as glass inside a real
    /// eyecup, not as a second dark vignette. <paramref name="lightFacing"/>
    /// is the radial normal's dot product with the fixed upper-left light
    /// direction, avoiding per-pixel trigonometry while baking the texture.
    /// </summary>
    public static class SextantRimProfile
    {
        public const int TextureSize = 512;

        private const float TubeShadowStart = 0.55f;
        private const float TubeShadowMid = 0.85f;
        private const float TubeShadowMidAlpha = 0.45f;
        private const float TubeShadowFull = 0.97f;

        private const float InnerGrooveStart = 0.925f;
        private const float InnerGrooveEnd = 0.941f;
        private const float BevelStart = 0.940f;
        private const float BevelEnd = 0.963f;
        private const float OuterLipStart = 0.962f;
        private const float OuterLipEnd = 0.978f;
        /// <summary>Evaluates the source-over RGBA texture at a normalized radius.</summary>
        public static SextantRimSample Evaluate(float radius, float lightFacing)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius))
                radius = 1f;
            if (radius < 0f)
                radius = 0f;

            float tubeAlpha = TubeShadowAlpha(radius);
            SextantRimSample sample = new SextantRimSample
            {
                R = 0f,
                G = 0f,
                B = 0f,
                A = tubeAlpha
            };

            // A narrow black groove separates the glass from the bevel.
            float groove = SmoothBand(radius, InnerGrooveStart, InnerGrooveEnd, 0.0025f) * 0.24f;
            CompositeOver(ref sample, 0f, 0f, 0f, groove);

            // Muted, directional steel reflection. It is intentionally faint:
            // the glass remains the focus and the reticle stays unobscured.
            float band = SmoothBand(radius, BevelStart, BevelEnd, 0.002f);
            if (float.IsNaN(lightFacing) || float.IsInfinity(lightFacing))
                lightFacing = 0f;
            float light = 0.22f + 0.78f * Clamp01(lightFacing);
            float highlightAlpha = band * (0.18f + 0.28f * light);
            float steel = 0.31f + 0.18f * light;
            CompositeOver(ref sample, steel, steel * 1.02f, steel * 0.94f, highlightAlpha);

            // The outer lip rolls back into the original black tube shadow.
            float outerLip = SmoothBand(radius, OuterLipStart, OuterLipEnd, 0.0025f) * 0.32f;
            CompositeOver(ref sample, 0f, 0f, 0f, outerLip);
            return sample;
        }

        private static float TubeShadowAlpha(float radius)
        {
            if (radius <= TubeShadowStart)
                return 0f;
            if (radius >= TubeShadowFull)
                return 1f;
            if (radius <= TubeShadowMid)
            {
                float t = (radius - TubeShadowStart) / (TubeShadowMid - TubeShadowStart);
                return TubeShadowMidAlpha * SmoothStep01(t);
            }

            float outer = (radius - TubeShadowMid) / (TubeShadowFull - TubeShadowMid);
            return TubeShadowMidAlpha + (1f - TubeShadowMidAlpha) * SmoothStep01(outer);
        }

        private static float SmoothBand(float value, float start, float end, float softness)
        {
            float rise = SmoothStep(start, start + softness, value);
            float fall = 1f - SmoothStep(end - softness, end, value);
            return rise * fall;
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            if (edge1 <= edge0)
                return value >= edge1 ? 1f : 0f;
            return SmoothStep01(Clamp01((value - edge0) / (edge1 - edge0)));
        }

        private static float SmoothStep01(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static void CompositeOver(ref SextantRimSample destination,
            float r, float g, float b, float alpha)
        {
            alpha = Clamp01(alpha);
            if (alpha <= 0f)
                return;

            float outAlpha = alpha + destination.A * (1f - alpha);
            if (outAlpha <= 0f)
                return;

            destination.R = (r * alpha + destination.R * destination.A * (1f - alpha)) / outAlpha;
            destination.G = (g * alpha + destination.G * destination.A * (1f - alpha)) / outAlpha;
            destination.B = (b * alpha + destination.B * destination.A * (1f - alpha)) / outAlpha;
            destination.A = outAlpha;
        }
    }
}
