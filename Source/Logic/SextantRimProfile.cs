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

        /// <summary>
        /// Eyecup (наглазник) texture: fully opaque up to this fraction of its
        /// own radius, then a long soft falloff. Lives here, not in the sprite
        /// baker, so the coverage invariant below can be tested without Unity.
        /// </summary>
        public const float EyecupSolidTo = 0.60f;

        /// <summary>
        /// Eyecup side as a multiple of the ocular side. The ocular is a
        /// FRACTION of the screen height, so the shade must be derived from it:
        /// the original fixed 1000 units covered a 603-unit ocular at 1360x768
        /// and then stopped at a 1267-unit one (2560x1440), which left the
        /// outer silhouette to the Mask's binary stencil cut — the angular,
        /// unblended rim the author reported (74 -&gt; 0 luminance in one pixel).
        ///
        /// The number is not cosmetic: the shade is opaque only to
        /// <c>0.5 * EyecupSolidTo</c> of its side, so covering the aperture
        /// needs a factor above <c>1 / EyecupSolidTo = 1.667</c>. 1.75 leaves a
        /// 5 % margin instead of landing exactly on the boundary.
        /// </summary>
        public const float EyecupFactor = 1.75f;

        /// <summary>Aperture radius in ocular-side units (the SDF circle).</summary>
        public static double ApertureRadiusUnits(double ocularSideUnits)
        {
            return Sanitize(ocularSideUnits) * 0.5d;
        }

        /// <summary>
        /// Radius, in ocular-side units, out to which the eyecup is fully
        /// opaque (and therefore able to hide the stencil cut).
        /// </summary>
        public static double ShadeOpaqueRadiusUnits(double ocularSideUnits)
        {
            return Sanitize(ocularSideUnits) * EyecupFactor * EyecupSolidTo * 0.5d;
        }

        /// <summary>
        /// The invariant the angular rim broke: the shade's opaque part must
        /// reach past the aperture at EVERY ocular size, so the silhouette is
        /// always the shade's own soft falloff and never the stencil's binary
        /// cut. Independent of resolution, uiScale and the ocular fraction.
        /// </summary>
        public static bool ShadeCoversAperture(double ocularSideUnits)
        {
            return ShadeOpaqueRadiusUnits(ocularSideUnits) > ApertureRadiusUnits(ocularSideUnits);
        }

        private static double Sanitize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0d ? 0d : value;
        }

        private const float TubeShadowStart = 0.55f;
        private const float TubeShadowMid = 0.85f;
        private const float TubeShadowMidAlpha = 0.45f;
        /// <summary>
        /// The tube shadow is fully opaque from here outward (author
        /// 2026-10-02: "перекрой это внахлест"). The defect the author
        /// reports lives at r/R 0.93-1.02; an opaque ring from 0.90 covers
        /// it with margin, whatever the smeared content is. Price, stated
        /// plainly: the picture loses the outer ~7 % of its radius to the
        /// bezel. The steel bands are untouched — they sit on top of the
        /// black tube as the designed highlight, static and
        /// turn-independent; only the opacity underneath moved.
        /// </summary>
        private const float TubeShadowFull = 0.90f;

        /// <summary>
        /// Width of every band's rise and fall, in normalized radius. It is the
        /// author's 2026-10-01 staircase fix: at 0.002 the steel bevel ended in
        /// 0.7 px, so its bright edge dropped 39 -> 4 luminance in a SINGLE
        /// pixel (measured live) and read as a stepped silhouette. The bands
        /// below were widened in step, because a soft edge inside a band
        /// narrower than twice the softness would lose its peak.
        /// </summary>
        public const float BandSoftness = 0.016f;

        private const float InnerGrooveStart = 0.910f;
        private const float InnerGrooveEnd = 0.954f;
        private const float BevelStart = 0.928f;
        private const float BevelEnd = 0.976f;
        private const float OuterLipStart = 0.954f;
        private const float OuterLipEnd = 0.990f;

        /// <summary>
        /// One screen pixel in normalized-radius units, for the sharpest case
        /// the overlay is drawn at (a 600-unit ocular side, i.e. the lower end
        /// of the 0.6..0.95 screen-fraction band). Stating it here keeps the
        /// smoothness budget in the same units the profile is authored in
        /// instead of leaving it to a comment.
        /// </summary>
        public const float RadiusPerPixel = 0.0033f;

        /// <summary>
        /// Largest luminance change allowed between two neighbouring pixels of
        /// the composited rim, measured over a sunlit sky (the case where a
        /// step is most visible). The old 0.002 bands needed 0.11 here, i.e.
        /// 28 of 255 levels in one pixel; the widened ones need 0.061. Alpha
        /// alone cannot express this: the tube is already ~95% opaque at the
        /// bevel, so the step shows up in the composited COLOUR.
        /// </summary>
        public const float MaxLuminanceRisePerPixel = 0.075f;
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
            float groove = SmoothBand(radius, InnerGrooveStart, InnerGrooveEnd, BandSoftness) * 0.24f;
            CompositeOver(ref sample, 0f, 0f, 0f, groove);

            // Muted, directional steel reflection. It is intentionally faint:
            // the glass remains the focus and the reticle stays unobscured.
            float band = SmoothBand(radius, BevelStart, BevelEnd, BandSoftness);
            if (float.IsNaN(lightFacing) || float.IsInfinity(lightFacing))
                lightFacing = 0f;
            float light = 0.22f + 0.78f * Clamp01(lightFacing);
            float highlightAlpha = band * (0.18f + 0.28f * light);
            float steel = 0.31f + 0.18f * light;
            CompositeOver(ref sample, steel, steel * 1.02f, steel * 0.94f, highlightAlpha);

            // The outer lip rolls back into the original black tube shadow.
            float outerLip = SmoothBand(radius, OuterLipStart, OuterLipEnd, BandSoftness) * 0.32f;
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
