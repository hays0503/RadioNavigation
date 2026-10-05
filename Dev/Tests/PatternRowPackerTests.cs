using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// PatternRowPacker contract: the gain-gated row selection behind
    /// InformationPanelHandler.UpdatePatterns. Extracted so filtering +
    /// packing run offline on synthetic diagnostics.
    /// </summary>
    public sealed class PatternRowPackerTests
    {
        private static StationDiagnostics Diag(float gain)
        {
            return new StationDiagnostics { Gain = gain };
        }

        [Fact]
        public void Pack_SkipsSilentStations()
        {
            var diags = new[] { Diag(0f), Diag(0.5f), Diag(0.0005f), Diag(0.9f) };

            Assert.Equal(new[] { 1, 3 }, PatternRowPacker.Pack(diags, 10));
        }

        [Fact]
        public void Pack_RespectsMaxRows()
        {
            var diags = new[] { Diag(0.5f), Diag(0.6f), Diag(0.7f) };

            Assert.Equal(new[] { 0 }, PatternRowPacker.Pack(diags, 1));
        }

        [Fact]
        public void Pack_AllSilent_ReturnsEmpty()
        {
            var diags = new[] { Diag(0f), Diag(0.0009f) };

            Assert.Empty(PatternRowPacker.Pack(diags, 10));
        }

        [Fact]
        public void Pack_ThresholdBoundary_IsInclusive()
        {
            var diags = new[] { Diag(0.001f) };

            Assert.Equal(new[] { 0 }, PatternRowPacker.Pack(diags, 10));
        }
    }
}
