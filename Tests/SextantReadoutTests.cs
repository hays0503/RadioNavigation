using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantReadoutTests
    {
        [Fact]
        public void TryUpdateArc_FirstCall_Emits()
        {
            var readout = new SextantReadout();

            string text;
            Assert.True(readout.TryUpdateArc(45.5, false, out text));
            Assert.Equal(SextantArc.Format(SextantArc.Quantize(45.5)), text);
        }

        [Fact]
        public void TryUpdateArc_SameValue_Suppressed()
        {
            var readout = new SextantReadout();
            string unused;
            readout.TryUpdateArc(45.5, false, out unused);

            string text;
            Assert.False(readout.TryUpdateArc(45.5, false, out text));
            Assert.Null(text);
        }

        [Fact]
        public void TryUpdateArc_Force_Reemits()
        {
            var readout = new SextantReadout();
            string unused;
            readout.TryUpdateArc(45.5, false, out unused);

            string text;
            Assert.True(readout.TryUpdateArc(45.5, true, out text));
        }

        [Fact]
        public void Reset_Rearms()
        {
            var readout = new SextantReadout();
            string unused;
            readout.TryUpdateArc(45.5, false, out unused);
            readout.Reset();

            string text;
            Assert.True(readout.TryUpdateArc(45.5, false, out text));
        }

        [Fact]
        public void FormatFilters_Empty_ReturnsEmpty()
        {
            Assert.Equal("", SextantReadout.FormatFilters(0, 0));
            Assert.Equal("", SextantReadout.FormatFilters(-1, 0));
        }

        [Fact]
        public void FormatFilters_BothSides()
        {
            string text = SextantReadout.FormatFilters(2, 1);

            Assert.Contains("2", text);
            Assert.Contains("1", text);
            Assert.DoesNotContain("  ", text);
        }

        [Fact]
        public void FormatLens_Off_ReturnsEmpty()
        {
            Assert.Equal("", SextantReadout.FormatLens(false, 0.35f));
        }

        [Fact]
        public void FormatLens_On_ShowsLength()
        {
            string text = SextantReadout.FormatLens(true, 0.35f);

            Assert.Contains("0.35", text);
        }

        [Fact]
        public void FormatFiltersAndLens_LensOnly_EqualsLens()
        {
            Assert.Equal(SextantReadout.FormatLens(true, 0.35f),
                SextantReadout.FormatFiltersAndLens(0, 0, true, 0.35f));
        }

        [Fact]
        public void FormatFiltersAndLens_FiltersOnly_EqualsFilters()
        {
            Assert.Equal(SextantReadout.FormatFilters(1, 0),
                SextantReadout.FormatFiltersAndLens(1, 0, false, 0.35f));
        }

        [Fact]
        public void FormatFiltersAndLens_Both_ContainsFilterAndLens()
        {
            string text = SextantReadout.FormatFiltersAndLens(1, 2, true, 0.35f);

            Assert.Contains(SextantReadout.FormatFilters(1, 2), text);
            Assert.Contains(SextantReadout.FormatLens(true, 0.35f), text);
        }

        [Fact]
        public void FilterDimAlpha_ScalesWithCount()
        {
            Assert.Equal(0f, SextantReadout.FilterDimAlpha(0));
            Assert.Equal(0.6f, SextantReadout.FilterDimAlpha(2), 5);
        }
    }
}
