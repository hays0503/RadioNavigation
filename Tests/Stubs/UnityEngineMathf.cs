using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int RoundToInt(float f) => (int)Math.Round(f);

        public static float Sqrt(float f) => (float)Math.Sqrt(f);

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float InverseLerp(float a, float b, float value)
        {
            if (a == b)
                return 0f;
            return Clamp01((value - a) / (b - a));
        }

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = t * t * (3f - 2f * t);
            return from + (to - from) * t;
        }

        public static float Max(float a, float b) => a > b ? a : b;

        public static float Min(float a, float b) => a < b ? a : b;

        public static float Exp(float power) => (float)Math.Exp(power);
    }
}
