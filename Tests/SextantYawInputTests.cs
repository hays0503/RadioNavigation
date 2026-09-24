using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Keyboard aim rotation (2026-11-04): left/right arrows rotate the
    /// sextant view like the bridge binocular, Shift speeds up, Ctrl trims in
    /// fine steps. Pins the deltas and the guards.
    /// </summary>
    public sealed class SextantYawInputTests
    {
        [Fact]
        public void DeltaCoarse_ScalesWithDirectionAndTime()
        {
            Assert.Equal(0.2f, SextantYawInput.DeltaCoarse(1f, 0.1f, false), 5);
            Assert.Equal(-0.2f, SextantYawInput.DeltaCoarse(-1f, 0.1f, false), 5);
        }

        [Fact]
        public void DeltaCoarse_ShiftMultiplies()
        {
            Assert.Equal(SextantYawInput.RateDegPerSec * SextantYawInput.ShiftMultiplier * 0.1f,
                SextantYawInput.DeltaCoarse(1f, 0.1f, true), 5);
        }

        [Fact]
        public void DeltaCoarse_Guards()
        {
            Assert.Equal(0f, SextantYawInput.DeltaCoarse(1f, 0f, false));
            Assert.Equal(0f, SextantYawInput.DeltaCoarse(1f, -1f, false));
            Assert.Equal(0f, SextantYawInput.DeltaCoarse(1f, float.NaN, false));
            Assert.Equal(0f, SextantYawInput.DeltaCoarse(float.NaN, 0.1f, false));
        }

        [Fact]
        public void DeltaFine_IsAFixedStep()
        {
            Assert.Equal(SextantYawInput.FineStepDeg, SextantYawInput.DeltaFine(1f), 5);
            Assert.Equal(-SextantYawInput.FineStepDeg, SextantYawInput.DeltaFine(-1f), 5);
            Assert.Equal(0f, SextantYawInput.DeltaFine(float.NaN));
        }

        [Fact]
        public void FineIsSmallerThanOneCoarseFrame()
        {
            // The fine step must be a precision nudge, not a coarse jump.
            Assert.True(SextantYawInput.FineStepDeg
                < SextantYawInput.RateDegPerSec * 0.1f);
        }
    }
}
