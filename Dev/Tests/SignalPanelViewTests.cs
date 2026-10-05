using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// SignalPanelView contract: the resolved signal-block state behind
    /// InformationPanelHandler.UpdateSignalDisplay. Extracted so block
    /// selection runs offline; the panel only applies Text/color.
    /// </summary>
    public sealed class SignalPanelViewTests
    {
        [Fact]
        public void Resolve_NullRich_PlaceholderDimmed()
        {
            SignalPanelView.State view = SignalPanelView.Resolve(null, "Decoded: ");

            Assert.Equal("--- Signal: ", view.SignalText);
            Assert.True(view.Dimmed);
            Assert.Equal("Decoded: ", view.DecodedText);
        }

        [Fact]
        public void Resolve_EmptyRich_PlaceholderDimmed()
        {
            SignalPanelView.State view = SignalPanelView.Resolve(string.Empty, "Decoded: ");

            Assert.Equal("--- Signal: ", view.SignalText);
            Assert.True(view.Dimmed);
        }

        [Fact]
        public void Resolve_RichComposesBlock()
        {
            SignalPanelView.State view = SignalPanelView.Resolve("RICH", "Decoded: TT");

            Assert.Equal("--- Signal: RICH", view.SignalText);
            Assert.False(view.Dimmed);
            Assert.Equal("Decoded: TT", view.DecodedText);
        }
    }
}
