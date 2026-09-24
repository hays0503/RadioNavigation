using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Dark adaptation of the lookout's eyes (CELESTIAL_REALISM_PLAN P4):
    /// stepping onto the bridge does not show the full star field at once.
    /// The state builds toward 1 with τ = 10 s (≈63% at 10 s, ≈92% at 25 s),
    /// freezes while shaded (red light / shade filters) and resets on a white
    /// flash (sun glare, pain). Pure state machine, no Unity — the owner feeds
    /// dt and the scene flags and folds Value into the star vertex alpha.
    /// </summary>
    public sealed class DarkAdaptationState
    {
        /// <summary>Time constant: 1 − e^(−t/τ).</summary>
        public const double TauSeconds = 10.0;

        /// <summary>Frame-spike guard: a hitch must not jump the ramp.</summary>
        private const double MaxStepSeconds = 0.25;

        private double value;

        /// <summary>Current adaptation in [0,1].</summary>
        public double Value
        {
            get { return value; }
        }

        /// <summary>White flash / sun glare: the eyes are blinded again.</summary>
        public void Reset()
        {
            value = 0.0;
        }

        /// <summary>Advances the ramp. <paramref name="dark"/> is the scene
        /// state (outside, sky visible, no pain); <paramref name="shielded"/>
        /// holds the value (red light / shade filters preserve adaptation).</summary>
        public void Step(double dt, bool dark, bool shielded)
        {
            if (!dark || shielded || dt <= 0.0)
                return;
            if (dt > MaxStepSeconds)
                dt = MaxStepSeconds;
            value += (1.0 - value) * (dt / TauSeconds);
            if (value > 1.0)
                value = 1.0;
        }

        /// <summary>True while the ramp is visibly moving (the star mesh needs
        /// rebuilding); 1% of the remaining gap is the threshold.</summary>
        public bool IsRamping
        {
            get { return value < 0.99; }
        }
    }
}
