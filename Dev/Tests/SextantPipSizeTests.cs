using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 ocular render-target sizing: half the screen (legacy) or
    /// the ocular's on-screen pixels (2026-11-04), clamped.</summary>
    public sealed class SextantPipSizeTests
    {
        [Fact]
        public void HalfTheScreenHeight()
        {
            Assert.Equal(540, SextantPipSize.ForScreenHeight(1080));
            Assert.Equal(720, SextantPipSize.ForScreenHeight(1440));
            Assert.Equal(256, SextantPipSize.ForScreenHeight(512));
        }

        [Fact]
        public void ClampsToTheSafeBand()
        {
            Assert.Equal(SextantPipSize.MinSize, SextantPipSize.ForScreenHeight(320));
            Assert.Equal(SextantPipSize.MaxSize, SextantPipSize.ForScreenHeight(4320));
        }

        [Fact]
        public void UnknownScreenHeight_FallsBack()
        {
            Assert.Equal(SextantPipSize.FallbackSize, SextantPipSize.ForScreenHeight(0));
            Assert.Equal(SextantPipSize.FallbackSize, SextantPipSize.ForScreenHeight(-100));
        }

        [Fact]
        public void ForOcularPixels_TracksTheOcularSize()
        {
            // 0.88 * 1080 = 950 px at uiScale 1.
            Assert.Equal(950, SextantPipSize.ForOcularPixels(950f));
            Assert.Equal(1536, SextantPipSize.ForOcularPixels(2000f));
            Assert.Equal(256, SextantPipSize.ForOcularPixels(100f));
        }

        [Fact]
        public void ForOcularPixels_Unknown_FallsBack()
        {
            Assert.Equal(SextantPipSize.FallbackSize, SextantPipSize.ForOcularPixels(0f));
            Assert.Equal(SextantPipSize.FallbackSize, SextantPipSize.ForOcularPixels(-5f));
            Assert.Equal(SextantPipSize.FallbackSize, SextantPipSize.ForOcularPixels(float.NaN));
        }
    }
}
