namespace UnityEngine
{
    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity
        {
            get { return new Quaternion(0f, 0f, 0f, 1f); }
        }

        public static Quaternion Inverse(Quaternion rotation)
        {
            float norm = rotation.x * rotation.x + rotation.y * rotation.y
                + rotation.z * rotation.z + rotation.w * rotation.w;
            if (norm <= 1e-12f)
                return identity;
            return new Quaternion(-rotation.x / norm, -rotation.y / norm,
                -rotation.z / norm, rotation.w / norm);
        }

        public static Quaternion operator *(Quaternion lhs, Quaternion rhs)
        {
            return new Quaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y - lhs.x * rhs.z + lhs.y * rhs.w + lhs.z * rhs.x,
                lhs.w * rhs.z + lhs.x * rhs.y - lhs.y * rhs.x + lhs.z * rhs.w,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);
        }
    }
}
