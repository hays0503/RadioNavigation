namespace UnityEngine
{
    // Test fake for the actual CelestialRenderState control flow. This is not
    // evidence of Unity culling or GPU submission; those require live capture.
    public class Renderer : Component
    {
        private bool isEnabled;
        public bool ThrowOnDisable { get; set; }

        public bool enabled
        {
            get { return isEnabled; }
            set
            {
                if (!value && ThrowOnDisable)
                    throw new System.InvalidOperationException("Injected native setter failure");
                isEnabled = value;
            }
        }
    }
}
