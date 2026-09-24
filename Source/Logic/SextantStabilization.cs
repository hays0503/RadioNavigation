using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Ocular stabilization policy (author decisions 2026-11-04): the aim must
    /// stay 1:1 with the mouse (a low-passed yaw made the view "slide" behind
    /// the hand — author feedback), while the boat's wobble must not dance the
    /// star across the crosshair. The design is therefore RAW + WASHOUT:
    ///  - the PIP yaw is the raw view yaw plus an offset — no temporal filter,
    ///    so moving the mouse moves the view exactly;
    ///  - while the mouse rests the offset freezes the world yaw (the aim
    ///    hold: the star stops moving with the boat);
    ///  - when the mouse moves again the offset bleeds out slowly
    ///    (<see cref="WashoutTauSeconds"/>), so there is no fast catch-up
    ///    slide and no unbounded drift;
    ///  - the POSITION is deliberately never touched (bino parity, author
    ///    decision after live testing): the game's bridge binocular keeps the
    ///    camera at the character's head with no damping, and any position
    ///    filtering/freeze showed as height jumps in the ocular.
    /// Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class SextantStabilization
    {
        /// <summary>PlayerPrefs gate (default ON): 0 disables the stabilization.</summary>
        public const string PrefsKey = "RadioNav.Stabilization";

        /// <summary>PlayerPrefs gate for the aim freeze (default ON): while the
        /// mouse is still the world yaw is frozen, so the boat's wobble stops
        /// moving the star across the crosshair.</summary>
        public const string LockPrefsKey = "RadioNav.StabLock";

        /// <summary>Washout time constant of the freeze offset (seconds): the
        /// frozen pose bleeds back to the live pose at ~3%/s — imperceptible
        /// (≤0.03°/s at ×6) yet bounded, and never a fast slide.</summary>
        public const float WashoutTauSeconds = 30f;

        /// <summary>Mouse-idle time that freezes the world yaw (seconds).</summary>
        public const float LockSeconds = 0.4f;

        /// <summary>Per-frame |dx|+|dy| below which the aim counts as still.</summary>
        public const float MouseIdleEpsilon = 0.05f;

        /// <summary>Idle-accumulator cap (keeps the float bounded).</summary>
        public const float MaxIdleSeconds = 10f;

        /// <summary>Largest yaw offset the freeze may hold (degrees): the boat's
        /// wobble is a few degrees, so a larger offset means the live pose
        /// jumped (teleport, camera transition) — the freeze then releases
        /// instead of pointing the view far away from the live one.</summary>
        public const float MaxYawOffsetDeg = 20f;

        /// <summary>True while an offset is small enough to be held by the
        /// freeze. NaN is never usable.</summary>
        public static bool OffsetUsable(float offset, float maxAbs)
        {
            if (float.IsNaN(offset) || float.IsNaN(maxAbs) || maxAbs <= 0f)
                return false;
            return (offset < 0f ? -offset : offset) <= maxAbs;
        }

        /// <summary>Idle-time accumulator for the aim freeze: a frame whose
        /// mouse motion stays below the epsilon keeps counting (capped), any
        /// real motion resets it. Invalid inputs keep the current value.</summary>
        public static float AdvanceIdle(float idleSeconds, float mouseMotion, float dt)
        {
            if (float.IsNaN(idleSeconds) || idleSeconds < 0f)
                idleSeconds = 0f;
            if (float.IsNaN(mouseMotion) || mouseMotion > MouseIdleEpsilon)
                return 0f;
            if (float.IsNaN(dt) || dt <= 0f)
                return idleSeconds;
            float next = idleSeconds + dt;
            return next > MaxIdleSeconds ? MaxIdleSeconds : next;
        }

        /// <summary>True when the aim has been still long enough to freeze the
        /// world yaw (the sighting hold: the star stops dancing with the boat).</summary>
        public static bool ShouldLock(float idleSeconds)
        {
            return !float.IsNaN(idleSeconds) && idleSeconds >= LockSeconds;
        }

        /// <summary>Exponential low-pass step: alpha = 1 - exp(-dt/tau).
        /// tau &lt;= 0 returns the target (no smoothing); dt &lt;= 0 keeps the
        /// current value; NaN inputs fall back to the safe side.</summary>
        public static float Smooth(float current, float target, float tau, float dt)
        {
            if (float.IsNaN(tau) || tau <= 0f)
                return target;
            if (float.IsNaN(dt) || dt <= 0f)
                return current;
            float alpha = 1f - (float)Math.Exp(-dt / tau);
            return current + (target - current) * alpha;
        }

        /// <summary>Bleeds the freeze offset back to zero with the washout
        /// time constant. A NaN offset falls back to 0 (no offset is always
        /// safe); dt &lt;= 0 keeps the current offset.</summary>
        public static float DecayOffset(float offset, float dt)
        {
            if (float.IsNaN(offset))
                return 0f;
            if (offset == 0f)
                return 0f;
            return Smooth(offset, 0f, WashoutTauSeconds, dt);
        }
    }
}
