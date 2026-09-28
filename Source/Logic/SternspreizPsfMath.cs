using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Unity-free, parameterized horizontal line-spread kernel for the
    /// Sternspreizglas experiment. This is a mathematical baseline only: the
    /// available instrument description specifies a horizontal line image but
    /// does not publish a measured optical transfer profile or line length.
    /// </summary>
    public static class SternspreizPsfMath
    {
        /// <summary>
        /// Builds an odd-sized, unit-sum kernel for a uniform horizontal line
        /// centered on a pixel. Each coefficient is the overlap of that pixel's
        /// unit-width cell with the continuous line segment, divided by the
        /// segment length. A zero-length line is a centered impulse.
        ///
        /// maxRadiusPixels is an explicit caller-owned allocation bound; this
        /// method does not choose a physical or visual spread length.
        /// </summary>
        public static float[] BuildHorizontalLineSpreadKernel(
            float lineLengthPixels, int maxRadiusPixels)
        {
            if (float.IsNaN(lineLengthPixels) || float.IsInfinity(lineLengthPixels)
                || lineLengthPixels < 0f)
                throw new ArgumentOutOfRangeException("lineLengthPixels");
            if (maxRadiusPixels < 0)
                throw new ArgumentOutOfRangeException("maxRadiusPixels");
            if (lineLengthPixels == 0f)
                return new[] { 1f };

            double halfLength = lineLengthPixels * 0.5d;
            double radiusValue = Math.Max(0d, Math.Ceiling(halfLength + 0.5d) - 1d);
            if (radiusValue > maxRadiusPixels
                || radiusValue > (int.MaxValue - 1) * 0.5d)
                throw new ArgumentOutOfRangeException("lineLengthPixels",
                    "The requested line exceeds the caller-provided kernel radius.");

            int radius = (int)radiusValue;
            var kernel = new float[radius * 2 + 1];
            double total = 0d;
            for (int i = 0; i < kernel.Length; i++)
            {
                int offset = i - radius;
                double cellStart = offset - 0.5d;
                double cellEnd = offset + 0.5d;
                double overlap = Math.Min(cellEnd, halfLength)
                    - Math.Max(cellStart, -halfLength);
                if (overlap <= 0d)
                    continue;

                kernel[i] = (float)(overlap / lineLengthPixels);
                total += kernel[i];
            }

            for (int i = 0; i < kernel.Length; i++)
                kernel[i] = (float)(kernel[i] / total);

            return kernel;
        }

        /// <summary>
        /// Applies the horizontal kernel to a row-major alpha field and pads
        /// the output width by the kernel radius on each side. The expanded
        /// field preserves the source sum instead of clipping the streak at
        /// the original sprite edges. The caller supplies all optical and
        /// allocation parameters; this method has no Unity/runtime dependency.
        /// </summary>
        public static float[] ConvolveHorizontalField(float[] sourceAlpha,
            int width, int height, float lineLengthPixels, int maxRadiusPixels,
            out int resultWidth)
        {
            resultWidth = 0;
            if (sourceAlpha == null)
                throw new ArgumentNullException("sourceAlpha");
            if (width <= 0 || height <= 0 || (long)width * height != sourceAlpha.LongLength)
                throw new ArgumentException("Field dimensions must match the row-major data.",
                    "sourceAlpha");

            float[] kernel = BuildHorizontalLineSpreadKernel(
                lineLengthPixels, maxRadiusPixels);
            long resultWidthValue = (long)width + kernel.Length - 1;
            long resultLength = resultWidthValue * height;
            if (resultWidthValue > int.MaxValue || resultLength > int.MaxValue)
                throw new ArgumentOutOfRangeException("width",
                    "The expanded field exceeds the supported array size.");

            resultWidth = (int)resultWidthValue;
            var result = new float[(int)resultLength];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = y * width;
                int resultRow = y * resultWidth;
                for (int x = 0; x < width; x++)
                {
                    float sample = sourceAlpha[sourceRow + x];
                    for (int k = 0; k < kernel.Length; k++)
                        result[resultRow + x + k] += sample * kernel[k];
                }
            }

            return result;
        }
    }
}
