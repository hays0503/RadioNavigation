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
        public void FilterDimAlpha_ScalesWithCount()
        {
            Assert.Equal(0f, SextantReadout.FilterDimAlpha(0));
            Assert.Equal(0.6f, SextantReadout.FilterDimAlpha(2), 5);
        }
    }
}
