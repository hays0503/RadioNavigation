// Minimal offline substitutes used by the local decompiled Perlin.cs only.
namespace UnityEngine
{
    public struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator *(Vector2 v, float k) { return new Vector2(v.x * k, v.y * k); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator *(Vector3 v, float k) { return new Vector3(v.x * k, v.y * k, v.z * k); }
    }

    public static class Mathf
    {
        public static float Floor(float x) { return (float)System.Math.Floor(x); }
        public static int FloorToInt(float x) { return (int)System.Math.Floor(x); }
    }
}
