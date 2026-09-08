using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SextantArcInputTests
    {
        [Fact]
        public void StepCoarse_BasicStep()
        {
            Assert.Equal(1.0, SextantArcInput.StepCoarse(0.0, 1f, 0.5f, false), 6);
        }

        [Fact]
        public void StepCoarse_FastMultipliesByFive()
        {
            Assert.Equal(5.0, SextantArcInput.StepCoarse(0.0, 1f, 0.5f, true), 6);
        }

        [Fact]
        public void StepCoarse_NegativeDir()
        {
            Assert.Equal(4.0, SextantArcInput.StepCoarse(5.0, -1f, 0.5f, false), 6);
        }

        [Fact]
        public void StepFine_OneTickIsOneQuantum()
        {
            Assert.Equal(1.0 / 300.0, SextantArcInput.StepFine(0.0, 1f), 9);
        }

        [Fact]
        public void AdvanceFineHold_PressFires()
        {
            var input = new SextantArcInput();

            Assert.True(input.AdvanceFineHold(0.016f, true));
        }

        [Fact]
        public void AdvanceFineHold_MidDelaySilent()
        {
            var input = new SextantArcInput();
            input.AdvanceFineHold(0.016f, true);

            Assert.False(input.AdvanceFineHold(0.1f, false));
        }

        [Fact]
        public void AdvanceFineHold_ExpiryFiresThenInterval()
        {
            var input = new SextantArcInput();
            input.AdvanceFineHold(0.016f, true);

            Assert.True(input.AdvanceFineHold(0.31f, false));
            Assert.False(input.AdvanceFineHold(0.1f, false));
            Assert.True(input.AdvanceFineHold(0.06f, false));
        }

        [Fact]
        public void ResetFineHold_RearmsImmediateTick()
        {
            var input = new SextantArcInput();
            input.AdvanceFineHold(0.016f, true);
            input.ResetFineHold();

            Assert.True(input.AdvanceFineHold(0.1f, false));
        }
    }
}
