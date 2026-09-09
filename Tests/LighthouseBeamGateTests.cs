using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class LighthouseBeamGateTests
    {
        [Fact]
        public void BeamFactor_HeadOn_ReturnsOne()
        {
            Assert.Equal(1.0, LighthouseBeamGate.BeamFactor(0.0, 1.0, 0.0, 5.0), 9);
        }

        [Fact]
        public void BeamFactor_Behind_ReturnsZero()
        {
            Assert.Equal(0.0, LighthouseBeamGate.BeamFactor(0.0, 1.0, 0.0, -5.0), 9);
        }

        [Fact]
        public void BeamFactor_Side90_ReturnsZero()
        {
            Assert.Equal(0.0, LighthouseBeamGate.BeamFactor(1.0, 0.0, 0.0, 5.0), 9);
        }

        [Fact]
        public void BeamFactor_HalfAngle_ReturnsHalf()
        {
            Assert.Equal(0.5, LighthouseBeamGate.BeamFactor(0.0, 1.0, 0.5, 0.8660254), 6);
        }

        [Fact]
        public void BeamFactor_Overhead_ReturnsOne()
        {
            Assert.Equal(1.0, LighthouseBeamGate.BeamFactor(0.0, 1.0, 0.0, 0.0), 9);
        }

        [Fact]
        public void FlareTakesOver_AllGoodFppNear_True()
        {
            Assert.True(LighthouseBeamGate.FlareTakesOver(true, true, true, true, false, 500.0, 30000.0));
        }

        [Fact]
        public void FlareTakesOver_OffBeamNight_True()
        {
            // The steady lamp-glow floor keeps the flare valid off-beam:
            // no beam-alignment condition in the takeover decision.
            Assert.True(LighthouseBeamGate.FlareTakesOver(true, true, true, true, false, 1753.0, 26532.0));
        }

        [Fact]
        public void FlareTakesOver_FppBeyondCap_False()
        {
            Assert.False(LighthouseBeamGate.FlareTakesOver(true, true, true, true, false, 31000.0, 30000.0));
        }

        [Fact]
        public void FlareTakesOver_DeviceFar_True()
        {
            Assert.True(LighthouseBeamGate.FlareTakesOver(true, true, true, true, true, 5000.0, 30000.0));
        }

        [Fact]
        public void FlareTakesOver_MissingPieces_False()
        {
            Assert.False(LighthouseBeamGate.FlareTakesOver(false, true, true, true, false, 500.0, 30000.0));
            Assert.False(LighthouseBeamGate.FlareTakesOver(true, true, false, true, false, 500.0, 30000.0));
            Assert.False(LighthouseBeamGate.FlareTakesOver(true, true, true, false, false, 500.0, 30000.0));
        }

        [Fact]
        public void FlareTakesOver_FarClipCulled_False()
        {
            Assert.False(LighthouseBeamGate.FlareTakesOver(true, true, true, true, false, 500.0, 500.0));
        }

        [Fact]
        public void IsOnScreen_Corners()
        {
            Assert.True(LighthouseBeamGate.IsOnScreen(0.0, 0.0, 1920.0, 1080.0));
            Assert.True(LighthouseBeamGate.IsOnScreen(1920.0, 1080.0, 1920.0, 1080.0));
            Assert.False(LighthouseBeamGate.IsOnScreen(-1.0, 500.0, 1920.0, 1080.0));
            Assert.False(LighthouseBeamGate.IsOnScreen(1921.0, 500.0, 1920.0, 1080.0));
        }
    }
}
