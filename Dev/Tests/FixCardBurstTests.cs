using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P6 burst annotation on the worksheet card.</summary>
    public sealed class FixCardBurstTests
    {
        [Fact]
        public void ComposeBurst_PlainMean()
        {
            string text = FixCardComposer.ComposeBurst(0.4, 5, 0);
            Assert.Equal("\u00B1 0.4\u2032 (n=5)", text);
        }

        [Fact]
        public void ComposeBurst_WithRejectedSample()
        {
            string text = FixCardComposer.ComposeBurst(0.3, 4, 1);
            Assert.Equal("\u00B1 0.3\u2032 (n=4, 1 \u043E\u0442\u0431\u0440)", text);
        }

        [Fact]
        public void ComposeBurst_RoughSwellFlag()
        {
            string text = FixCardComposer.ComposeBurst(1.6, 5, 0);
            Assert.Contains("\u043A\u0430\u0447\u043A\u0430", text);
            Assert.Contains("1.6\u2032", text);
        }

        [Fact]
        public void ComposeIndexError_Signed()
        {
            Assert.Equal("\u0418\u041F +0.4\u2032", FixCardComposer.ComposeIndexError(0.4));
            Assert.Equal("\u0418\u041F \u22120.5\u2032", FixCardComposer.ComposeIndexError(-0.5));
        }

        [Fact]
        public void ComposeBlockReason_NamesTheRefusal()
        {
            Assert.Contains("\u043D\u0438\u0437\u043A\u043E",
                FixCardComposer.ComposeBlockReason(SightBlock.Low));
            Assert.Contains("\u0434\u043D\u0451\u043C",
                FixCardComposer.ComposeBlockReason(SightBlock.Daylight));
            Assert.Contains("\u041B\u0443\u043D\u0430",
                FixCardComposer.ComposeBlockReason(SightBlock.MoonNew));
            Assert.Equal("", FixCardComposer.ComposeBlockReason(SightBlock.None));
        }
    }
}
