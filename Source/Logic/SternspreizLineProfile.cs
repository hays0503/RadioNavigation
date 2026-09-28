using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sternspreizglas (astigmatizer lens) line-profile math. Unity-free.
    ///
    /// Established boundary (see docs/sternspreiz-phase1-psf.md): no source
    /// publishes a measured line length or intensity profile for the glass, so
    /// the uniform finite line is a mathematical baseline only. The caller
    /// supplies the angular length; this class owns the discretization, the
    /// energy normalization and the angular/pixel/world conversions.
    /// </summary>
    public static class SternspreizLineProfile
    {
        /// <summary>Texture column count (samples along the line) of the baked
        /// streak sprite; 96 keeps the sharply pointed tips from staircase-stepping.</summary>
        public const int DefaultSamples = 96;

        /// <summary>Texture height (rows across the line) of the baked streak
        /// sprite; 48 keeps the gradient of the wide, soft-edged rhombus smooth.</summary>
        public const int DefaultStreakRows = 48;

        /// <summary>Soft-edge fraction of the line length (each end).</summary>
        public const float DefaultEdgeFraction = 0.18f;

        /// <summary>
        /// Width-taper exponent: 0 = constant thickness (flat bar), 1 = the local
        /// half-width shrinks linearly to <see cref="DefaultMinWidth"/> at the tips.
        /// 2.2 = sharp-tipped rhombus: the local half-width at the line midpoint is
        /// only 0.5^2.2 ~ 0.22 of the full width (author request 2026-11-07:
        /// sharper tips, wider body).
        /// </summary>
        public const float DefaultWidthTaper = 2.2f;

        /// <summary>Minimum local half-width at the tips, as a fraction of the full
        /// width; 0.05 keeps the tips hair-thin instead of truncating the spike.</summary>
        public const float DefaultMinWidth = 0.05f;

        /// <summary>Full-brightness core fraction of the local half-width (band flat
        /// out to here); 0.45 lets the soft edge dissolve over the remaining 55% of
        /// the local width, so the rhombus reads blurred rather than crisp.</summary>
        public const float DefaultCoreFraction = 0.45f;

        /// <summary>Allocation bound passed to the kernel/convolution helpers.</summary>
        public const int DefaultMaxRadiusPixels = 1024;

        /// <summary>
        /// Flat-top row with smoothstep edges, normalized to sum 1. A point
        /// source spread by the glass keeps its total flux, so a longer line is
        /// dimmer per sample; the row itself carries the shape only.
        /// </summary>
        public static float[] BuildFlatTopRow(int samples, float edgeFraction)
        {
            if (samples < 2)
                throw new ArgumentOutOfRangeException("samples");
            if (float.IsNaN(edgeFraction) || float.IsInfinity(edgeFraction)
                || edgeFraction < 0f || edgeFraction >= 0.5f)
                throw new ArgumentOutOfRangeException("edgeFraction");

            var row = new float[samples];
            double total = 0d;
            for (int i = 0; i < samples; i++)
            {
                float u = (i + 0.5f) / samples; // (0,1) sample centre
                float value = 1f;
                if (edgeFraction > 0f)
                {
                    value = SmoothStep(0f, edgeFraction, u)
                        * SmoothStep(0f, edgeFraction, 1f - u);
                }
                row[i] = value;
                total += value;
            }

            if (total <= 0d)
                throw new InvalidOperationException("Profile row collapsed to zero energy.");
            for (int i = 0; i < samples; i++)
                row[i] = (float)(row[i] / total);
            return row;
        }

        /// <summary>
        /// Baked alpha map of the streak sprite: columns along the line, rows
        /// across it, row-major, values in [0,1]. Pass 0 of the lens shader
        /// samples <c>_MainTex</c> directly (<c>tex2D(_MainTex, uv) * i.color</c>),
        /// so baking the taper into the texture makes that pass render it for
        /// free, without changing the draw pass or the quad geometry.
        ///
        /// <c>along = |2u-1|</c> (0 = line centre, 1 = tip);
        /// <c>w(u) = max(minWidth, (1-along)^widthTaper)</c> = local half-width;
        /// <c>across = |2v-1| / w(u)</c> (0 = centre line, 1 = local edge);
        /// <c>band = 1 - SmoothStep(coreFraction, 1, across)</c> (flat core, soft edge);
        /// <c>alpha = profile[x] * band</c> with
        /// <c>profile = BuildFlatTopRow(columns, edgeFraction)</c>, so the existing
        /// end brightness taper is preserved. widthTaper = 0 reproduces a
        /// constant-width band.
        /// </summary>
        public static float[] BuildStreakAlphaMap(int columns, int rows, float edgeFraction,
            float widthTaper, float minWidth, float coreFraction)
        {
            if (columns < 2)
                columns = 2;
            if (rows < 2)
                rows = 2;
            ValidateStreakParameters(widthTaper, minWidth, coreFraction);

            float[] profile = BuildFlatTopRow(columns, edgeFraction);
            var alpha = new float[columns * rows];
            for (int x = 0; x < columns; x++)
            {
                float u = (x + 0.5f) / columns;
                float width = LocalHalfWidth(Math.Abs(2f * u - 1f), widthTaper, minWidth);
                float columnProfile = profile[x];
                for (int y = 0; y < rows; y++)
                {
                    float v = (y + 0.5f) / rows;
                    float across = Math.Abs(2f * v - 1f) / width;
                    float band = 1f - SmoothStep(coreFraction, 1f, across);
                    alpha[y * columns + x] = columnProfile * band;
                }
            }
            return alpha;
        }

        /// <summary>Local half-width of the streak at <paramref name="along"/>
        /// (0 = line centre, 1 = tip): max(minWidth, (1-along)^widthTaper).</summary>
        private static float LocalHalfWidth(float along, float widthTaper, float minWidth)
        {
            float baseWidth = 1f - along;
            if (baseWidth < 0f)
                baseWidth = 0f;
            float width = widthTaper > 0f ? (float)Math.Pow(baseWidth, widthTaper) : 1f;
            if (width < minWidth)
                width = minWidth;
            if (width > 1f)
                width = 1f;
            return width;
        }

        private static void ValidateStreakParameters(float widthTaper, float minWidth, float coreFraction)
        {
            if (float.IsNaN(widthTaper) || float.IsInfinity(widthTaper) || widthTaper < 0f)
                throw new ArgumentOutOfRangeException("widthTaper");
            if (float.IsNaN(minWidth) || float.IsInfinity(minWidth) || minWidth <= 0f || minWidth > 1f)
                throw new ArgumentOutOfRangeException("minWidth");
            if (float.IsNaN(coreFraction) || float.IsInfinity(coreFraction)
                || coreFraction < 0f || coreFraction >= 1f)
                throw new ArgumentOutOfRangeException("coreFraction");
        }

        /// <summary>
        /// Resamples a caller-supplied line-spread kernel (for example
        /// <see cref="SternspreizPsfMath.BuildHorizontalLineSpreadKernel"/>)
        /// into a normalized row of <paramref name="samples"/> coefficients
        /// spanning the full kernel extent. Used when the profile comes from
        /// convolved sprite math rather than from a flat-top baseline.
        /// </summary>
        public static float[] ResampleKernel(float[] kernel, int samples)
        {
            if (kernel == null)
                throw new ArgumentNullException("kernel");
            if (samples < 2)
                throw new ArgumentOutOfRangeException("samples");
            if (kernel.Length == 0)
                throw new ArgumentException("Kernel is empty.", "kernel");

            var row = new float[samples];
            double total = 0d;
            if (kernel.Length == 1)
            {
                for (int i = 0; i < samples; i++)
                    row[i] = 1f / samples;
                return row;
            }

            double half = (kernel.Length - 1) * 0.5d;
            for (int i = 0; i < samples; i++)
            {
                double u = (i + 0.5d) / samples * 2d - 1d;   // (-1,1)
                double position = half * (u + 1d);           // [0, kernel.Length-1]
                int left = (int)Math.Floor(position);
                if (left < 0)
                    left = 0;
                if (left > kernel.Length - 1)
                    left = kernel.Length - 1;
                int right = Math.Min(left + 1, kernel.Length - 1);
                double frac = position - left;
                row[i] = (float)(kernel[left] * (1d - frac) + kernel[right] * frac);
                total += row[i];
            }

            if (total <= 0d)
                throw new InvalidOperationException("Resampled profile collapsed to zero energy.");
            for (int i = 0; i < samples; i++)
                row[i] = (float)(row[i] / total);
            return row;
        }

        /// <summary>
        /// World width of a line whose angular length is <paramref name="angleDeg"/>
        /// and whose distance is <paramref name="distanceM"/>: 2·d·tan(θ/2).
        /// Zoom independence comes from expressing the length in angles, not pixels.
        /// </summary>
        public static float AngularWidthMeters(double angleDeg, double distanceM)
        {
            if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg) || angleDeg < 0d)
                throw new ArgumentOutOfRangeException("angleDeg");
            if (double.IsNaN(distanceM) || double.IsInfinity(distanceM) || distanceM <= 0d)
                throw new ArgumentOutOfRangeException("distanceM");

            double halfRad = angleDeg * 0.5d * Math.PI / 180d;
            return (float)(2d * distanceM * Math.Tan(halfRad));
        }

        /// <summary>
        /// Pixels covered by an angle for a camera with a vertical field of
        /// view and a square-ish viewport measured in pixels (the sextant PIP
        /// render target is square, so the vertical mapping is used for both
        /// axes).
        /// </summary>
        public static double PixelsForAngle(double angleDeg, double verticalFovDeg, int viewportPixels)
        {
            if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg) || angleDeg < 0d)
                throw new ArgumentOutOfRangeException("angleDeg");
            if (double.IsNaN(verticalFovDeg) || double.IsInfinity(verticalFovDeg)
                || verticalFovDeg <= 0d || verticalFovDeg >= 179d)
                throw new ArgumentOutOfRangeException("verticalFovDeg");
            if (viewportPixels <= 0)
                throw new ArgumentOutOfRangeException("viewportPixels");

            double halfFov = verticalFovDeg * 0.5d * Math.PI / 180d;
            double halfAngle = angleDeg * 0.5d * Math.PI / 180d;
            double ratio = Math.Tan(halfAngle) / Math.Tan(halfFov);
            return ratio * viewportPixels;
        }

        /// <summary>Inverse of <see cref="PixelsForAngle"/>.</summary>
        public static double AngleForPixels(double pixels, double verticalFovDeg, int viewportPixels)
        {
            if (double.IsNaN(pixels) || double.IsInfinity(pixels) || pixels < 0d)
                throw new ArgumentOutOfRangeException("pixels");
            if (double.IsNaN(verticalFovDeg) || double.IsInfinity(verticalFovDeg)
                || verticalFovDeg <= 0d || verticalFovDeg >= 179d)
                throw new ArgumentOutOfRangeException("verticalFovDeg");
            if (viewportPixels <= 0)
                throw new ArgumentOutOfRangeException("viewportPixels");

            double halfFov = verticalFovDeg * 0.5d * Math.PI / 180d;
            double ratio = pixels / viewportPixels * Math.Tan(halfFov);
            return 2d * Math.Atan(ratio) * 180d / Math.PI;
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
                return x >= edge1 ? 1f : 0f;
            float t = (x - edge0) / (edge1 - edge0);
            if (t < 0f)
                t = 0f;
            if (t > 1f)
                t = 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
