namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Legacy, live-switchable cloud-mask experiment, retained for diagnostics
    /// only. Production occlusion is provided by the single PointSky pass being
    /// drawn before UniStorm's cloud composite; do not enable these modes as a
    /// substitute for that ordering. Modes 1/2 use an uncalibrated dome-map UV,
    /// and mode 3 is invalid: PointSky runs before the cloud composite, while its
    /// output target does not preserve alpha (the observed blend factors are
    /// Zero/Zero for alpha). <see cref="Mode"/> 0 is the shipping/default path.
    /// </summary>
    internal static class CloudOcclusionSwitch
    {
        /// <summary>0 = production ordering only; 1–3 = legacy uncalibrated/invalid cloud-mask experiments.</summary>
        internal static int Mode;

        /// <summary>Occlusion strength override, clamped to [0,1] when applied.</summary>
        internal static float Strength = 1f;

        internal static void Set(int mode)
        {
            Mode = mode;
        }

        internal static void SetStrength(float strength)
        {
            Strength = strength;
        }

        internal static void Reset()
        {
            Mode = 0;
            Strength = 1f;
        }
    }
}
