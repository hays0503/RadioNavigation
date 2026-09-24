namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Keyboard aim rotation for the sextant (author request 2026-11-04,
    /// mirroring the bridge binocular's arrow-key rotation): the left/right
    /// arrows rotate the view, Shift speeds it up, Ctrl gives the fine step —
    /// the same coarse/fine pair as the arc drum. Deltas only (the caller adds
    /// them to its yaw trim); pure, no Unity — unit-tested offline.
    /// </summary>
    public static class SextantYawInput
    {
        /// <summary>Coarse rotation rate (degrees per second) — matches the
        /// arc drum's feel.</summary>
        public const float RateDegPerSec = 2f;

        /// <summary>Shift multiplier (the arc drum uses the same).</summary>
        public const float ShiftMultiplier = 5f;

        /// <summary>Fine step per key tick (degrees) — the Ctrl trim.</summary>
        public const float FineStepDeg = 0.05f;

        /// <summary>Coarse delta for this frame (dir -1/+1). Invalid inputs
        /// return 0 (no motion).</summary>
        public static float DeltaCoarse(float dir, float dt, bool fast)
        {
            if (float.IsNaN(dir) || float.IsNaN(dt) || dt <= 0f)
                return 0f;
            return dir * RateDegPerSec * (fast ? ShiftMultiplier : 1f) * dt;
        }

        /// <summary>Fine delta for one key tick (dir -1/+1).</summary>
        public static float DeltaFine(float dir)
        {
            if (float.IsNaN(dir))
                return 0f;
            return dir * FineStepDeg;
        }
    }
}
