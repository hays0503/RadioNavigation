using System;

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

        /// <summary>Floor of the largest world-space extent for the game's star
        /// dome renderer. Measured live 2026-09-29: the `Stars` mesh spans
        /// ~4.4-6.1 km, while the `Light Bulb Flare`/`Lens Flare` renderers
        /// (which share a `Star4` material) are sub-metre and dozens of UI
        /// objects are named `Stars`; the extent floor keeps them all out.</summary>
        public const float DomeMinExtent = 1000f;

        /// <summary>Case-insensitive token of the dome's shared material name
        /// (live: `Material:Stars`). Also matches the inactive
        /// `UniStorm Stars`, which the active/extent conditions reject.</summary>
        public const string DomeMaterialToken = "Stars";

        public static bool ShouldHide(bool preferenceOn, bool ourSkyActive)
        {
            return preferenceOn && ourSkyActive;
        }

        /// <summary>True when a shared material name marks a game star-dome
        /// candidate (`Stars`, any casing).</summary>
        public static bool IsStarDomeMaterial(string materialName)
        {
            return !string.IsNullOrEmpty(materialName)
                && materialName.IndexOf(DomeMaterialToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>True when the largest world-space extent clears the dome
        /// floor (flare renderers and UI sprites are far smaller).</summary>
        public static bool HasDomeExtent(float extentX, float extentY, float extentZ)
        {
            return Math.Max(extentX, Math.Max(extentY, extentZ)) > DomeMinExtent;
        }

        /// <summary>Full renderer-level identification: an active object, a
        /// material whose name contains `Stars`, and a dome-sized world extent.
        /// Never matches the sub-metre flare renderers or the UI `Stars`
        /// namesakes.</summary>
        public static bool IsStarDomeCandidate(bool active, string materialName,
            float extentX, float extentY, float extentZ)
        {
            return active
                && IsStarDomeMaterial(materialName)
                && HasDomeExtent(extentX, extentY, extentZ);
        }
    }
}
