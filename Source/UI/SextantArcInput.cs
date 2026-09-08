namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Arc-angle step math + fine-drum repeat state behind
    /// SextantController.HandleArcInput (P5.4 slice 4b, SRP): key polling
    /// stays in the controller, the rate/quantum math and the press-hold
    /// repeat cycle live here so they run offline in
    /// SextantArcInputTests. Pure C#.
    /// </summary>
    public sealed class SextantArcInput
    {
        public const float RateDegPerSec = 2f;
        public const float ShiftMultiplier = 5f;
        public const int FineStepQuant = 1;
        public const float FineRepeatDelay = 0.3f;
        public const float FineRepeatInterval = 0.15f;

        private float fineRepeatTimer;

        public void ResetFineHold()
        {
            fineRepeatTimer = 0f;
        }

        /// <summary>
        /// Advances the fine-drum repeat cycle. Returns true when a tick
        /// fires this frame: immediately on press, then after the delay,
        /// then every interval while held — like turning the drum.
        /// </summary>
        public bool AdvanceFineHold(float dt, bool pressed)
        {
            if (pressed)
            {
                fineRepeatTimer = FineRepeatDelay;
                return true;
            }
            fineRepeatTimer -= dt;
            if (fineRepeatTimer <= 0f)
            {
                fineRepeatTimer = FineRepeatInterval;
                return true;
            }
            return false;
        }

        public static double StepCoarse(double arc, float dir, float dt, bool fast)
        {
            float rate = RateDegPerSec * (fast ? ShiftMultiplier : 1f);
            return SextantArc.Quantize(arc + dir * rate * dt);
        }

        /// <summary>One fine-adjustment step (0.2' drum tick), always on-grid.</summary>
        public static double StepFine(double arc, float dir)
        {
            return SextantArc.Quantize(arc + dir * FineStepQuant / SextantArc.StepsPerDeg);
        }
    }
}
