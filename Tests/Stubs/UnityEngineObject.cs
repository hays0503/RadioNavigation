namespace UnityEngine
{
    /// <summary>
    /// Minimal Object stub mirroring Unity's fake-null semantics: a
    /// destroyed object is a non-null reference that compares == null.
    /// DestroySelf simulates UnityEngine.Object.Destroy for tests.
    /// </summary>
    public class Object
    {
        private bool destroyed;

        public void DestroySelf() { destroyed = true; }

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.destroyed;
            bool bNull = ReferenceEquals(b, null) || b.destroyed;
            if (aNull || bNull)
                return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object o) => this == (o as Object);

        public override int GetHashCode() => base.GetHashCode();
    }
}
