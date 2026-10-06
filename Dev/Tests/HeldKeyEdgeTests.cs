using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class HeldKeyEdgeTests
    {
        [Fact]
        public void Advance_InitialReleaseDoesNotPress()
        {
            var edge = new HeldKeyEdge();

            Assert.False(edge.Advance(false));
        }

        [Fact]
        public void Advance_HoldProducesOneRisingEdge()
        {
            var edge = new HeldKeyEdge();

            Assert.True(edge.Advance(true));
            for (int i = 0; i < 120; i++)
                Assert.False(edge.Advance(true));
        }

        [Fact]
        public void Advance_ReleaseThenPressProducesAnotherEdge()
        {
            var edge = new HeldKeyEdge();

            Assert.True(edge.Advance(true));
            Assert.False(edge.Advance(false));
            Assert.True(edge.Advance(true));
        }

        [Fact]
        public void Synchronize_HeldKeyAtEnableDoesNotCreateEdge()
        {
            var edge = new HeldKeyEdge();

            edge.Synchronize(true);

            Assert.False(edge.Advance(true));
            Assert.False(edge.Advance(false));
            Assert.True(edge.Advance(true));
        }

        [Fact]
        public void RisingEdge_FineHoldRepeatsOnConfiguredTimerNotOnHeldFrames()
        {
            var edge = new HeldKeyEdge();
            var fine = new SextantArcInput();

            bool pressed = edge.Advance(true);
            Assert.True(fine.AdvanceFineHold(0.016f, pressed));

            Assert.False(fine.AdvanceFineHold(0.1f, edge.Advance(true)));
            Assert.True(fine.AdvanceFineHold(0.21f, edge.Advance(true)));
            Assert.False(fine.AdvanceFineHold(0.1f, edge.Advance(true)));
            Assert.True(fine.AdvanceFineHold(0.06f, edge.Advance(true)));
        }
    }
}
