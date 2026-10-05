using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Fix-column layout contract for the sextant overlay worksheet column
    /// (F-Tafel card): on wide unit spaces (unitWidth >= 1650) the card is
    /// pinned to the screen's top-left corner, on narrower ones it keeps the
    /// stacked fallback below the reading column (left edge clamped on
    /// screen). Pins the layout DECISION (aspect + UI scale -> placement),
    /// not the GameObject building — the builder bodies are free to vary.
    /// </summary>
    public sealed class SextantOverlayLayoutTests
    {
        [Fact]
        public void FullHd_Scale115_TopLeft()
        {
            // 1920x1080 / 1.15 = 1669.57 x 939.13 units -> top-left.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, 1.15f);

            Assert.False(p.stacked);
            Assert.Equal(0f, p.pivotX);
            Assert.Equal(1f, p.pivotY);
            Assert.Equal(-810.78f, p.anchorX, 1);
            Assert.Equal(389.57f, p.anchorY, 1);
            // Card size = (FixColumnWidthUnits, maxHeightUnits).
            Assert.Equal(342f, SextantOverlayLayout.FixColumnWidthUnits);
            Assert.Equal(839.13f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void FullHd_Scale120_StacksBelow()
        {
            // 1920x1080 / 1.20 = 1600 x 900 units -> below the threshold.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, 1.20f);

            Assert.True(p.stacked);
            Assert.Equal(1f, p.pivotX);
            Assert.Equal(1f, p.pivotY);
            // Legacy overload: ocular 600, gap 30 -> -(600/2) - 30 = -330.
            Assert.Equal(-330f, p.anchorX);
            Assert.Equal(-110f, p.anchorY);
            // 900/2 - 110 - 20 = 320.
            Assert.Equal(320f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void Ultrawide_Scale155_TopLeft()
        {
            // 2560x1080 / 1.55 = 1651.61 x 696.77 -> just over the threshold.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(2560f, 1080f, 1.55f);

            Assert.False(p.stacked);
            Assert.Equal(596.77f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void Ultrawide_Scale160_StacksBelow()
        {
            // 2560x1080 / 1.60 = 1600 x 675 units.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(2560f, 1080f, 1.60f);

            Assert.True(p.stacked);
            Assert.Equal(207.5f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void FourByThree_Scale085_TopLeft()
        {
            // 1440x1080 / 0.85 = 1694.12 x 1270.59 units -> top-left; usable
            // height = 1270.59 - 80 - 20 = 1170.59.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1440f, 1080f, 0.85f);

            Assert.False(p.stacked);
            Assert.Equal(1170.59f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void FourByThree_Scale090_StacksBelow()
        {
            // 1440x1080 / 0.90 = 1600 x 1200 units.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1440f, 1080f, 0.90f);

            Assert.True(p.stacked);
            // 1200/2 - 110 - 20 = 470.
            Assert.Equal(470f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void FullHd_Scale050_TopLeft()
        {
            // 1920x1080 / 0.50 = 3840 x 2160 units.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, 0.5f);

            Assert.False(p.stacked);
            // 2160 - 80 - 20 = 2060.
            Assert.Equal(2060f, p.maxHeightUnits, 1);
        }

        [Fact]
        public void Boundary_AtTopLeftMinWidth_IsTopLeft()
        {
            // unitWidth exactly 1650: >= keeps the top-left branch.
            var at = SextantOverlayLayout.ComputeFixColumnPlacement(1650f, 1080f, 1f);
            Assert.False(at.stacked);

            // 1649.5 units: falls back to stacked.
            var below = SextantOverlayLayout.ComputeFixColumnPlacement(1649.5f, 1080f, 1f);
            Assert.True(below.stacked);
        }

        [Fact]
        public void Stacked_VeryNarrow_ClampsLeftEdgeOnScreen()
        {
            // 800-unit space: the reading-column x (-340) would run the
            // 342-wide card off the left screen edge (340 + 342 > 400), so
            // the left edge is clamped to a 24-unit margin: anchorX =
            // -unitWidth/2 + 24 + width = -34, left edge -376 = -400 + 24.
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(800f, 1080f, 1f);

            Assert.True(p.stacked);
            Assert.Equal(-34f, p.anchorX);
            Assert.True(p.anchorX - SextantOverlayLayout.FixColumnWidthUnits >= -400f + 24f,
                "card left edge must stay 24 units inside the screen's left edge");
        }

        [Fact]
        public void ZeroHeight_GuardedNoCrash()
        {
            // A zero screen height must not divide by zero or produce NaN; the
            // usable height is clamped to zero and the card fit then keeps the
            // base font (FixCardLayout treats <= 0 as unknown).
            var p = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 0f);

            Assert.False(float.IsNaN(p.maxHeightUnits));
            Assert.True(p.maxHeightUnits >= 0f);
            Assert.False(float.IsNaN(p.anchorX));
        }

        [Fact]
        public void SideBudget_LeavesEveryColumnOnScreen()
        {
            // The ocular width cap must leave each side its column plus the
            // gap (2026-11-04: the sun line was folded into the bodies block,
            // so the widest side content is the sun column, not a 291-unit
            // line). If a column grows past this, the readout would run off
            // the screen edge again — this test is the tripwire.
            float perSide = SextantOverlayLayout.SideColumnsUnits * 0.5f;
            float widestColumn = System.Math.Max(SextantOverlayLayout.ColumnWidthUnits,
                SextantOverlayLayout.SunColumnWidthUnits);
            Assert.True(SextantOverlayLayout.ColumnGapUnits + widestColumn <= perSide,
                "gap + widest side column must fit the per-side budget");
        }

        [Fact]
        public void BodiesTextAvailable_AtFullHd()
        {
            // 1080p: the block's text top is 159.9 units, the game's bottom
            // button row starts 193 units above the screen's bottom edge.
            Assert.Equal(506.9f, SextantOverlayLayout.BodiesTextAvailableUnits(1080f, 1f), 1);
            Assert.Equal(304.4f, SextantOverlayLayout.BodiesTextAvailableUnits(1080f, 1.6f), 1);
            Assert.Equal(236.9f, SextantOverlayLayout.BodiesTextAvailableUnits(1080f, 2f), 1);
        }

        [Fact]
        public void BodiesTextAvailable_DegenerateInputs()
        {
            Assert.Equal(0f, SextantOverlayLayout.BodiesTextAvailableUnits(0f, 1f));
            Assert.Equal(0f, SextantOverlayLayout.BodiesTextAvailableUnits(-100f, 1f));
            Assert.Equal(0f, SextantOverlayLayout.BodiesTextAvailableUnits(float.NaN, 1f));
            // Invalid UI scale falls back to 1.
            Assert.Equal(506.9f, SextantOverlayLayout.BodiesTextAvailableUnits(1080f, 0f), 1);
            Assert.Equal(506.9f, SextantOverlayLayout.BodiesTextAvailableUnits(1080f, float.NaN), 1);
        }

        [Fact]
        public void BodiesFont_FitsTheLiveBlockAtScale16()
        {
            // Live 2026-11-04: the 29-line block measures 348 units at font
            // 11; the uiScale-1.6 room is 304.4 -> the fitter returns 9
            // (measuring 284.7), so the list clears the game's bottom UI.
            float room = SextantOverlayLayout.BodiesTextAvailableUnits(1080f, 1.6f);
            int fitted = FixCardLayout.FontSize(348f, room, 11, FixCardLayout.MinFontSize);
            Assert.Equal(9, fitted);
            Assert.True(348f * fitted / 11f <= room,
                "the fitted font must fit the available room");
        }

        [Fact]
        public void UiScale_Invalid_FallsBackToOne()
        {
            var one = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, 1f);
            var zero = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, 0f);
            var nan = SextantOverlayLayout.ComputeFixColumnPlacement(1920f, 1080f, float.NaN);

            Assert.Equal(one.maxHeightUnits, zero.maxHeightUnits);
            Assert.Equal(one.maxHeightUnits, nan.maxHeightUnits);
            Assert.Equal(one.anchorX, zero.anchorX);
            Assert.Equal(one.anchorX, nan.anchorX);
        }
    }
}
