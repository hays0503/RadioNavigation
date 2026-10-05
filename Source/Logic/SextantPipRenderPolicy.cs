namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// An attached PIP is rendered recursively by DeepPipeline even when its
    /// Camera.enabled is false (the game's windowed periscope uses this route).
    /// Enabling it as well adds a second render in the same game frame. An
    /// open-edge history reset then only protects the first of those renders.
    /// Live A/B: the double route reproduced the first-frame smear; the single
    /// route kept the captured openings clear (SEXTANT_PLAN section 16.24).
    /// </summary>
    public static class SextantPipRenderPolicy
    {
        // Live A/B only: 0 reproduces the old attached + standalone route.
        public const string PrefsKey = "RadioNav.PipSingleRender";

        public static bool UseStandaloneCamera(bool attached, bool rootRendering,
            bool singleRender)
        {
            // Never disable the only working route: a missing attachment or an
            // inactive main camera must fall back to Unity's standalone list.
            return !singleRender || !attached || !rootRendering;
        }
    }
}
