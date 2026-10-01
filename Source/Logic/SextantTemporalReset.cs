using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Rate-gated reset of the render pipeline's temporal history on the two
    /// PIP cameras. Pure decision; the Unity call lives in SextantPipPose.
    ///
    /// Why this exists (measured 2026-10-02, not assumed): on fast mouse turns
    /// the presented 3D frame smears — the whole scene, in the ocular AND the
    /// main view, while UI text and the bezel stay sharp (same-frame control,
    /// instantaneous back-buffer capture). The smeared bright clouds next to
    /// the sharp black bezel read as ragged bright arcs hugging the rim. The
    /// pipeline keeps temporal history per camera (AdditionalCameraData.
    /// TemporalEffectsResetRequested exists for exactly this; the game itself
    /// resets it on camera transitions), and a fast rotation outruns history
    /// convergence. Resetting PIP history while the turn is fast keeps the
    /// ocular sharp; the main view is untouched, so vanilla is preserved.
    ///
    /// What it is NOT: the lit wall (drawn under the bezel, measured 0.000
    /// contribution), the rim leak (capped 0.070, wrong place), PPv1 (ruled
    /// out experimentally: PPv1 off on PIP gives identical smear), PPv2 motion
    /// blur (off in the author's settings by the inverted flag). The reset
    /// does not name an effect — it drops the history whatever accumulates.
    /// </summary>
    public static class SextantTemporalReset
    {
        /// <summary>PlayerPrefs kill-switch. 1 = reset while fast (default).</summary>
        public const string PrefsKey = "RadioNav.TemporalReset";

        /// <summary>
        /// Aim rate at and above which PIP temporal history is dropped, in
        /// deg/s. NOT tuned to look nice: 22 deg/s of probe spin renders a
        /// clean frame without any reset (verified live), so the threshold
        /// sits above the fastest verified-clean rate and only mouse flicks
        /// reach it. Lowering it would change probe-verified frames.
        /// </summary>
        public const double ResetRateDegPerSec = 25.0;

        /// <summary>Pure decision: drop PIP temporal history this frame?</summary>
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

        /// <summary>Reads the kill-switch. Kept beside the decision so the
        /// call-site contract (gated, never unconditional) is one place.</summary>
        public static bool IsEnabled()
        {
            return PlayerPrefs.GetInt(PrefsKey, 1) != 0;
        }
    }
}
