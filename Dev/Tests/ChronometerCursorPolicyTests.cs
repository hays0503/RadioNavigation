using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Pins the two-zone cursor decision stream. Each case corresponds to a
    // reported symptom from the live sessions; the policy is pure, so the
    // whole state machine is testable without Unity.
    public class ChronometerCursorPolicyTests
    {
        private readonly ChronometerCursorPolicy policy = new ChronometerCursorPolicy();

        [Fact]
        public void EnterCenter_EmitsArrowsExpand()
        {
            Assert.Equal(ChronometerCursorCommand.ArrowsExpand, policy.Evaluate(true, false, false));
        }

        [Fact]
        public void ToggleWhileCentered_SwapsArrowsExactlyOnce()
        {
            policy.Evaluate(true, false, false);
            Assert.Equal(ChronometerCursorCommand.ArrowsReduce, policy.Evaluate(true, false, true));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, false, true));
        }

        [Fact]
        public void CenterToBodyEdge_EmitsCrosshair()
        {
            policy.Evaluate(true, false, false);
            Assert.Equal(ChronometerCursorCommand.Crosshair, policy.Evaluate(false, true, false));
        }

        [Fact]
        public void BodyToOutside_EmitsGameDefault()
        {
            policy.Evaluate(false, true, false);
            Assert.Equal(ChronometerCursorCommand.GameDefault, policy.Evaluate(false, false, false));
        }

        [Fact]
        public void CenterToOutside_EmitsGameDefault()
        {
            policy.Evaluate(true, false, false);
            Assert.Equal(ChronometerCursorCommand.GameDefault, policy.Evaluate(false, false, false));
        }

        [Fact]
        public void MoveRequestWhileCentered_IsSuppressed()
        {
            Assert.Equal(ChronometerCursorCommand.ArrowsExpand, policy.Evaluate(true, true, false));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, true, false));
        }

        [Fact]
        public void IdenticalInputs_EmitNone()
        {
            Assert.NotEqual(ChronometerCursorCommand.None, policy.Evaluate(true, false, false));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, false, false));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, false, false));
        }

        [Fact]
        public void ToggleWithoutHoverChange_ReemitsArrowsOnce()
        {
            policy.Evaluate(true, false, false);
            Assert.Equal(ChronometerCursorCommand.ArrowsReduce, policy.Evaluate(true, false, true));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, false, true));
            Assert.Equal(ChronometerCursorCommand.ArrowsExpand, policy.Evaluate(true, false, false));
            Assert.Equal(ChronometerCursorCommand.None, policy.Evaluate(true, false, false));
        }
    }
}
