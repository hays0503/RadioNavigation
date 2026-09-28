namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Decision for hiding the game's decorative star layer.
    ///
    /// The game's sky is a baked, time-rotated star sphere (TOD): measured live
    /// 2026-09-28 its stars sit a median ~6.3 deg away from the mod catalog, so
    /// with both layers visible the sky shows two different star fields and the
    /// Sternspreiz streaks cannot line up with what the eye sees.
    ///
    /// Fail-safe rule: hide the game layer ONLY when the mod's own star sky is
    /// actually the one being drawn (the bundled shader resolved). Otherwise a
    /// shader/loading problem would leave a completely empty night sky.
    /// </summary>
    public static class StarLayerSuppression
    {
        /// <summary>The TOD global that scales the game's star layer
        /// (set every frame by TOD_Sky; verified as a literal in
        /// com.uboat.dependencies.dll).</summary>
        public const string GlobalName = "TOD_StarVisibility";

        public static bool ShouldHide(bool preferenceOn, bool ourSkyActive)
        {
            return preferenceOn && ourSkyActive;
        }
    }
}
