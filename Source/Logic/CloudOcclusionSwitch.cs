namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Live-switchable cloud-occlusion mode for the mod's celestial layer,
    /// driven by the MCP bridge (reflect_set). UniStorm's clouds write no depth,
    /// so the mod's stars/planets/Moon/Sun (drawn after the composite) always
    /// paint over them. Two opacity sources exist: UniStorm's volumetric dome
    /// (<c>fullCloudBuff0</c> alpha, modes 1/2) and the screen-space cloud
    /// opacity the composite writes into the game's <c>GBuffer3</c> target
    /// alpha (mode 3). The five sky renderers push the mode/strength into their
    /// material every frame through <see cref="CloudOcclusionBinding.Apply"/>,
    /// so a bridge write takes effect within a frame or two without a restart.
    /// <see cref="Mode"/> 0 is OFF — the shader's default path is untouched.
    /// </summary>
    internal static class CloudOcclusionSwitch
    {
        /// <summary>0 = off, 1 = view-space orthographic dome map, 2 = world-azimuthal dome map, 3 = screen-space GBuffer3 cloud alpha.</summary>
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
