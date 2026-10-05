using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Unity-free deterministic contracts for faint ocular grime and motes.</summary>
    public static class SextantOpticsProfile
    {
        public const int MoteCount = 7;
        public const float MoteDriftCyclesPerSecond = 0.0125f;

        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                n = (n ^ (n >> 13)) * 1274126177u;
                n ^= n >> 16;
                return (n & 0x00ffffff) / 16777215f;
            }
        }

        /// <summary>Softly keeps the central sight picture clear and fades before the rim.</summary>
        public static float GrimeCoverage(float radius)
        {
            if (!Finite(radius) || radius <= 0.34f || radius >= 0.91f) return 0f;
            float inner = Smooth(0.34f, 0.52f, radius);
            float outer = 1f - Smooth(0.76f, 0.91f, radius);
            return inner * outer;
        }

        public static void MotePosition(int index, float seconds, out float x, out float y)
        {
            if (index < 0) index = 0;
            float phase = Hash01(index, 17, 31);
            float angle = (phase + seconds * MoteDriftCyclesPerSecond) * 6.28318530718f;
            float radius = 0.38f + Hash01(index, 29, 47) * 0.43f;
            x = (float)Math.Cos(angle) * radius;
            y = (float)Math.Sin(angle) * radius;
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
