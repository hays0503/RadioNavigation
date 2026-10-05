namespace UnityEngine
{
    /// <summary>Minimal Color stub for the Unity-free logic linked into the
    /// tests (SextantSightLineColor). Mirrors the UnityEngine API surface the
    /// pure code touches.</summary>
    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = 1f;
        }

        public static Color Lerp(Color x, Color y, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return new Color(
                x.r + (y.r - x.r) * t,
                x.g + (y.g - x.g) * t,
                x.b + (y.b - x.b) * t,
                x.a + (y.a - x.a) * t);
        }
    }
}
