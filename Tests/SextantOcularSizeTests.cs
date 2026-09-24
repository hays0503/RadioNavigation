using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Ocular size policy (2026-11-04): the ocular is a fraction of the unit
    /// height (screen height / UI scale), clamped by the unit width so the
    /// side columns keep their space. Pins the fraction band, the width clamp
    /// and the degenerate fallback.
    /// </summary>
    public sealed class SextantOcularSizeTests
    {
        // The real budget the layout reserves for both side columns and gaps
        // (SextantOverlayLayout keeps it in step with the column widths).
        private const float SideColumns = SextantOverlayLayout.SideColumnsUnits;

        [Fact]
        public void DefaultFraction_IsInTheBand()
        {
            Assert.Equal(0.88f, SextantOcularSize.DefaultFraction);
            Assert.True(SextantOcularSize.DefaultFraction >= SextantOcularSize.MinFraction);
            Assert.True(SextantOcularSize.DefaultFraction <= SextantOcularSize.MaxFraction);
        }

        [Fact]
        public void Fraction_ClampsIntoTheBand()
        {
            Assert.Equal(SextantOcularSize.MinFraction, SextantOcularSize.ClampFraction(0.1f));
            Assert.Equal(SextantOcularSize.MaxFraction, SextantOcularSize.ClampFraction(2f));
            Assert.Equal(0.8f, SextantOcularSize.ClampFraction(0.8f));
        }

        [Fact]
        public void Fraction_Invalid_FallsBackToDefault()
        {
            Assert.Equal(SextantOcularSize.DefaultFraction, SextantOcularSize.ClampFraction(0f));
            Assert.Equal(SextantOcularSize.DefaultFraction, SextantOcularSize.ClampFraction(-1f));
            Assert.Equal(SextantOcularSize.DefaultFraction, SextantOcularSize.ClampFraction(float.NaN));
        }

        [Fact]
        public void Units_HeightDriven_OnAWideSpace()
        {
            // 1920x1080 / 1.59 = 1207.5 x 679.2 units; 0.88 * 679.2 = 597.7 and
            // the width cap (1207.5 - 540 = 667.5) is looser, so the height
            // fraction wins — the whole point of the 2026-11-04 change.
            float size = SextantOcularSize.Units(1207.5f, 679.2f, 0.88f, SideColumns);
            Assert.Equal(597.7f, size, 1);
        }

        [Fact]
        public void Units_HeightDriven_WhenWidthIsAmple()
        {
            // 3840x2160 / 1 = 3840 x 2160 units: width cap 3000, height 1900.8.
            float size = SextantOcularSize.Units(3840f, 2160f, 0.88f, SideColumns);
            Assert.Equal(1900.8f, size, 1);
        }

        [Fact]
        public void Units_WidthCap_NeverOverlapsTheColumns()
        {
            // A narrow space: the width cap must win and leave the columns room.
            float size = SextantOcularSize.Units(1200f, 2000f, 0.95f, SideColumns);
            Assert.Equal(1200f - SideColumns, size, 3);
            Assert.True(size + SideColumns <= 1200f);
        }

        [Fact]
        public void Units_DegenerateInputs_FallBackToLegacy()
        {
            Assert.Equal(600f, SextantOcularSize.Units(0f, 1080f, 0.88f, SideColumns));
            Assert.Equal(600f, SextantOcularSize.Units(1920f, 0f, 0.88f, SideColumns));
            Assert.Equal(600f, SextantOcularSize.Units(float.NaN, 1080f, 0.88f, SideColumns));
        }

        [Fact]
        public void Units_NeverBelowOne()
        {
            // A space narrower than the columns: clamp to 1 unit, never 0/negative.
            float size = SextantOcularSize.Units(500f, 1080f, 0.88f, SideColumns);
            Assert.Equal(1f, size);
        }

        [Fact]
        public void UnitsForScreen_MatchesTheUnitSpace()
        {
            float direct = SextantOcularSize.Units(1920f / 1.59f, 1080f / 1.59f, 0.88f, SideColumns);
            float viaScreen = SextantOcularSize.UnitsForScreen(1920f, 1080f, 1.59f, 0.88f, SideColumns);
            Assert.Equal(direct, viaScreen, 3);
        }

        [Fact]
        public void UnitsForScreen_InvalidScale_FallsBackToOne()
        {
            float one = SextantOcularSize.UnitsForScreen(1920f, 1080f, 1f, 0.88f, SideColumns);
            float zero = SextantOcularSize.UnitsForScreen(1920f, 1080f, 0f, 0.88f, SideColumns);
            float nan = SextantOcularSize.UnitsForScreen(1920f, 1080f, float.NaN, 0.88f, SideColumns);
            Assert.Equal(one, zero, 3);
            Assert.Equal(one, nan, 3);
        }
    }
}
