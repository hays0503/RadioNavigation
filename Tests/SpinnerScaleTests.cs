using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// SpinnerScale contract: the single source of truth for the 140°
    /// spinner range (angle -&gt; unit / percent). Unifies the scattered
    /// angle/140f divisions in SonneController, InformationPanelHandler
    /// and AudioSynthesisEngine.
    /// </summary>
    public sealed class SpinnerScaleTests
    {
        [Fact]
        public void MaxAngle_Is140()
        {
            Assert.Equal(140f, SpinnerScale.MaxAngle);
        }

        [Fact]
        public void ToUnit_MidRange_IsHalf()
        {
            Assert.Equal(0.5f, SpinnerScale.ToUnit(70f));
        }

        [Fact]
        public void ToUnit_Endpoints()
        {
            Assert.Equal(0f, SpinnerScale.ToUnit(0f));
            Assert.Equal(1f, SpinnerScale.ToUnit(140f));
        }

        [Fact]
        public void ToUnit_ClampsOutside()
        {
            Assert.Equal(0f, SpinnerScale.ToUnit(-10f));
            Assert.Equal(1f, SpinnerScale.ToUnit(200f));
        }

        [Fact]
        public void ToPercent_ReferencePoints()
        {
            Assert.Equal(0, SpinnerScale.ToPercent(0f));
            Assert.Equal(50, SpinnerScale.ToPercent(70f));
            Assert.Equal(100, SpinnerScale.ToPercent(140f));
        }
    }
}
