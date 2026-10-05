namespace UnityEngine
{
    /// <summary>Offline test stub. Zero means "no device rate", which makes
    /// AudioSynthesisEngine exercise its deterministic 44100 fallback.</summary>
    public static class AudioSettings
    {
        public static int outputSampleRate { get; set; }
    }
}
