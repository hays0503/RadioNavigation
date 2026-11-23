using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class WindLabelLayoutTests
    {
        [Fact]
        public void PlacesRightOfGlyphsWhenPanelHasRoom()
        {
            // Mirrors Parameters [-162..0] with windsock [-142..-112] and "6 уз" [-107..-72].
            WindLabelPlacement p = WindLabelLayout.TryPlace(-107f, -72f, -142f, -112f, -158f, -4f, 34f, 6f);
            Assert.True(p.Visible);
            Assert.False(p.AlignRight);
            Assert.Equal(-66f, p.AnchorX, 3);
        }

        [Fact]
        public void FallsBackBetweenIconAndGlyphsWhenRightEdgeIsClipped()
        {
            WindLabelPlacement p = WindLabelLayout.TryPlace(-60f, -15f, -150f, -120f, -158f, -4f, 36f, 6f);
            Assert.True(p.Visible);
            Assert.True(p.AlignRight);
            Assert.Equal(-66f, p.AnchorX, 3);
        }

        [Fact]
        public void HidesLabelWhenNeitherSideFitsWithoutOverlapOrClipping()
        {
            WindLabelPlacement p = WindLabelLayout.TryPlace(-107f, -30f, -142f, -112f, -158f, -4f, 36f, 6f);
            Assert.False(p.Visible);
        }

        [Fact]
        public void RejectsInvalidOrZeroWidthInputs()
        {
            Assert.False(WindLabelLayout.TryPlace(float.NaN, 0f, -10f, 0f, -50f, 50f, 20f, 6f).Visible);
            Assert.False(WindLabelLayout.TryPlace(-10f, 0f, -20f, -10f, -50f, 50f, 0f, 6f).Visible);
            Assert.False(WindLabelLayout.TryPlace(10f, 0f, -20f, -10f, -50f, 50f, 20f, 6f).Visible);
        }
    }
}
