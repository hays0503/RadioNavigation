using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Rate-gated reset of the render pipeline's temporal history while the
    /// aim is swinging fast. Pure decision; the Unity writes live in
    /// SextantPipPose.
    ///
    /// Why the MAIN camera and not just the PIP pair (measured 2026-10-02):
    /// a 220 deg/s probe spin rotates the PIP cameras with the main view
    /// static and renders sharp frames, while fast mouse turns smear the
    /// whole 3D frame (ocular AND main view, UI sharp, instantaneous
    /// capture). So the smear follows the main camera, and the per-camera
    /// PIP reset was reverted as addressing a non-effect. The pipeline keeps
    /// temporal history the game itself resets on camera transitions; a fast
    /// rotation outruns history convergence, and smeared bright content next
    /// to the sharp black bezel reads as ragged bright arcs hugging the rim.
    /// Resetting while fast keeps the presented frame sharp; when the turn
    /// stops the history rebuilds on its own.
    /// </summary>
    public static class SextantTemporalReset
    {
        /// <summary>PlayerPrefs kill-switch. 1 = reset while fast (default).</summary>
        public const string PrefsKey = "RadioNav.TemporalReset";

        /// <summary>
        /// Aim rate at and above which history is dropped, in deg/s. Sits
        /// above normal hand motion and well below a mouse flick; resetting a
        /// little early only costs shimmer-free sharp frames, while resetting
        /// late leaves the smear.
        /// </summary>
        public const double ResetRateDegPerSec = 25.0;

        /// <summary>Pure decision: drop temporal history this frame?</summary>
        public static bool ShouldReset(double rateDegPerSec, bool prefEnabled)
        {
            if (!prefEnabled)
                return false;
            if (double.IsNaN(rateDegPerSec) || double.IsInfinity(rateDegPerSec))
                return false;
            if (rateDegPerSec < 0d)
                return false;
            return rateDegPerSec >= ResetRateDegPerSec;
        }

        /// <summary>Reads the kill-switch.</summary>
        public static bool IsEnabled()
        {
            return PlayerPrefs.GetInt(PrefsKey, 1) != 0;
        }
    }
}
