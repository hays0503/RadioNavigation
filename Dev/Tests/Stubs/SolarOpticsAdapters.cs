namespace UnityEngine
{
    // Input-only doubles for the ACTUAL SextantSolarOptics adapter. These are
    // not a water renderer and cannot establish horizon/pixel acceptance.
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
    }

    public sealed class Camera : Object
    {
        public Transform transform = new Transform("camera");
        public object WaterComponent;
        public T GetComponent<T>() where T : class { return WaterComponent as T; }
    }

    public static class Shader
    {
        public static float Curvature;
        public static float GetGlobalFloat(string name) { return Curvature; }
    }
}

namespace PlayWay.Water
{
    public sealed class Water
    {
        public UnityEngine.Transform transform = new UnityEngine.Transform("water");
    }

    public sealed class WaterCamera
    {
        public Water MainWater;
        public float WaterLevel; // deliberately different from mean sea in tests
    }
}
