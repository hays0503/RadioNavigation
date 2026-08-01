using System;

namespace UBOAT.Mods.RadioNavigation
{
    public static class ComparisonExtensions
    {
        public static bool IsNull(this object obj) => obj == null;
        public static bool IsNotNull(this object obj) => obj != null;

        public static bool IsZero(this int value) => value == 0;
        public static bool IsNotZero(this int value) => value != 0;
        public static bool IsPositive(this int value) => value > 0;
        public static bool IsNegative(this int value) => value < 0;
        public static bool IsNonNegative(this int value) => value >= 0;
        public static bool IsNonPositive(this int value) => value <= 0;

        public static bool IsZero(this float value) => value == 0f;
        public static bool IsNotZero(this float value) => value != 0f;
        public static bool IsPositive(this float value) => value > 0f;
        public static bool IsNegative(this float value) => value < 0f;
        public static bool IsNonNegative(this float value) => value >= 0f;
        public static bool IsNonPositive(this float value) => value <= 0f;

        public static bool IsZero(this double value) => value == 0.0;
        public static bool IsNotZero(this double value) => value != 0.0;
        public static bool IsPositive(this double value) => value > 0.0;
        public static bool IsNegative(this double value) => value < 0.0;
        public static bool IsNonNegative(this double value) => value >= 0.0;
        public static bool IsNonPositive(this double value) => value <= 0.0;

        // F74: thresholds are intentionally asymmetric — IsSignificant (1e-3)
        // guards a perceptibility/CPU cut-off in the audio thread (DspPipeline
        // whistle), IsNegligible (1e-9) is a float "zero" clamp. Callers pass
        // explicit thresholds where the default does not fit.
        public static bool IsSignificant(this float value, float threshold = 0.001f) => value > threshold;
        public static bool IsNegligible(this float value, float threshold = 1e-9f) => Math.Abs(value) < threshold;
    }
}
