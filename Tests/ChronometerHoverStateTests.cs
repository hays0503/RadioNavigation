using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ChronometerHoverStateTests
    {
        [Fact]
        public void Initial_ForwardSuppressedUntilChange()
        {
            var state = new ChronometerHoverState();

            bool inside;
            Assert.False(state.TryConsumeForward(out inside));
            Assert.False(inside);
        }

        [Fact]
        public void SetMoveHover_ForwardEmitsTrue()
        {
            var state = new ChronometerHoverState();
            bool unused;
            state.TryConsumeForward(out unused);

            state.SetMoveHover(true);

            bool inside;
            Assert.True(state.TryConsumeForward(out inside));
            Assert.True(inside);
        }

        [Fact]
        public void UnchangedMove_Suppressed()
        {
            var state = new ChronometerHoverState();
            state.SetMoveHover(true);
            bool unused;
            state.TryConsumeForward(out unused);

            state.SetMoveHover(true);

            bool inside;
            Assert.False(state.TryConsumeForward(out inside));
        }

        [Fact]
        public void TrueToFalse_EmitsAgain()
        {
            var state = new ChronometerHoverState();
            state.SetMoveHover(true);
            bool unused;
            state.TryConsumeForward(out unused);

            state.SetMoveHover(false);

            bool inside;
            Assert.True(state.TryConsumeForward(out inside));
            Assert.False(inside);
        }

        [Fact]
        public void ScaleHover_Exposed_DoesNotAffectForward()
        {
            var state = new ChronometerHoverState();
            bool unused;
            state.TryConsumeForward(out unused);

            state.SetScaleHover(true);

            Assert.True(state.ScaleHover);
            Assert.False(state.MoveHover);
            bool inside;
            Assert.False(state.TryConsumeForward(out inside));
        }

        [Fact]
        public void MoveHover_Exposed()
        {
            var state = new ChronometerHoverState();

            state.SetMoveHover(true);

            Assert.True(state.MoveHover);
        }
    }
}
