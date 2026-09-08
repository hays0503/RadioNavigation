using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Fix-column layout contract for the sextant overlay worksheet column
    /// (F-Tafel card): on narrow aspects the column stacks below the reading
    /// column, on wide aspects it sits side-by-side to the left of it.
    /// Pins the layout DECISION (aspect -> placement), not the GameObject
    /// building — the builder bodies are free to vary.
    /// </summary>
    public sealed class SextantOverlayLayoutTests
    {
        [Fact]
        public void FullHd_Wide_StacksBelow()
        {
            // refW = 1920, side = (1920-600)/2 = 660 < 820.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f);

            Assert.True(p.stacked);
            Assert.Equal(1f, p.pivotX);
            Assert.Equal(1f, p.pivotY);
            Assert.Equal(-340f, p.anchorX);
            Assert.Equal(-110f, p.anchorY);
        }

        [Fact]
        public void Ultrawide_SideBySide()
        {
            // refW = 2560, side = (2560-600)/2 = 980 > 820.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(2560f, 1080f);

            Assert.False(p.stacked);
            Assert.Equal(1f, p.pivotX);
            Assert.Equal(0.5f, p.pivotY);
            Assert.Equal(-700f, p.anchorX);
            Assert.Equal(0f, p.anchorY);
        }

        [Fact]
        public void FourByThree_Narrow_StacksBelow()
        {
            // refW = 1440, side = (1440-600)/2 = 420 < 820.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1440f, 1080f);

            Assert.True(p.stacked);
        }

        [Fact]
        public void Boundary_IsStrictlyLess()
        {
            // side exactly 820 (refW = 2240): strict < keeps side-by-side.
            var at = SextantOverlayLayout.ComputeFixColumnPlacement(2240f, 1080f);
            Assert.False(at.stacked);

            // side 819.5: stacks.
            var below = SextantOverlayLayout.ComputeFixColumnPlacement(2239f, 1080f);
            Assert.True(below.stacked);
        }

        [Fact]
        public void ZeroHeight_GuardedNoCrash()
        {
            // max(1, height) guard: huge refW, deterministic side-by-side.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 0f);

            Assert.False(p.stacked);
        }
    }
}
