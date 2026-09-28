namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// B-V index → RGB (simplified Stellarium-like map). Unity-free so both the
    /// point-star mesh and the Sternspreiz streak field share one mapping.
    /// Alpha is deliberately NOT part of this mapping: star photometry owns it
    /// (StarBrightness is the single source).
    /// </summary>
    public static class StarColor
    {
        /// <summary>
        /// B-V index → RGB. B-V &lt; 0: blue, 0: white, 0.5: yellow-white,
        /// 1.0: orange, 1.5+: red.
        /// </summary>
        public static void FromBv(float bv, out float r, out float g, out float b)
        {
            if (bv < -0.4f)
            {
                r = 0.6f; g = 0.7f; b = 1.0f; // blue
            }
            else if (bv < 0.0f)
            {
                float t = InverseLerp(-0.4f, 0.0f, bv);
                r = Lerp(0.6f, 1.0f, t);
                g = Lerp(0.7f, 1.0f, t);
                b = 1.0f;
            }
            else if (bv < 0.5f)
            {
                float t = InverseLerp(0.0f, 0.5f, bv);
                r = 1.0f;
                g = Lerp(1.0f, 0.95f, t);
                b = Lerp(1.0f, 0.8f, t);
            }
            else if (bv < 1.0f)
            {
                float t = InverseLerp(0.5f, 1.0f, bv);
                r = 1.0f;
                g = Lerp(0.95f, 0.7f, t);
                b = Lerp(0.8f, 0.4f, t);
            }
            else if (bv < 1.5f)
            {
                float t = InverseLerp(1.0f, 1.5f, bv);
                r = 1.0f;
                g = Lerp(0.7f, 0.4f, t);
                b = Lerp(0.4f, 0.2f, t);
            }
            else
            {
                r = 1.0f; g = 0.3f; b = 0.1f; // deep red
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        private static float InverseLerp(float a, float b, float value)
        {
            if (a == b)
                return 0f;
            return Clamp01((value - a) / (b - a));
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
