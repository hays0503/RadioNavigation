using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// StationGainModel contract: the receiver selectivity curve
    /// (1/sqrt(1+Q²·ratio²)) and the BFO beat clamp. Extracted from
    /// SonneController.UpdateStationGains; the ShieldTests local copy of
    /// the formula now calls production code instead of duplicating it.
    /// </summary>
    public sealed class StationGainModelTests
    {
        [Fact]
        public void Gain_TunedExactly_GainOne()
        {
            Assert.Equal(1f, StationGainModel.ComputeGain(319f, 319f, 20f));
        }

        [Fact]
        public void Gain_315_vs_319_QF20_IsAbout089_CrossTalk()
        {
            Assert.InRange(StationGainModel.ComputeGain(315f, 319f, 20f), 0.85f, 0.92f);
        }

        [Fact]
        public void Gain_315_vs_319_QF200_IsAbout02_Suppressed()
        {
            Assert.InRange(StationGainModel.ComputeGain(315f, 319f, 200f), 0.15f, 0.30f);
        }

        [Fact]
        public void Gain_266_vs_257_QF20_HighCrossTalk()
        {
            Assert.InRange(StationGainModel.ComputeGain(266f, 257f, 20f), 0.55f, 0.65f);
        }

        [Theory]
        [InlineData(float.NaN, 257f)]
        [InlineData(257f, float.NaN)]
        public void Gain_NaN_ShouldBeZero(float f, float f0)
        {
            Assert.Equal(0f, StationGainModel.ComputeGain(f, f0, 20f));
        }

        [Fact]
        public void Gain_ZeroF0_ShouldBeZero()
        {
            Assert.Equal(0f, StationGainModel.ComputeGain(300f, 0f, 20f));
        }

        [Fact]
        public void Beat_ZeroOffset_IsBase()
        {
            Assert.Equal(1000f, StationGainModel.ComputeBeatFrequency(0f));
        }

        [Fact]
        public void Beat_PositiveOffset_AddsHertz()
        {
            Assert.Equal(1500f, StationGainModel.ComputeBeatFrequency(0.5f));
        }

        [Fact]
        public void Beat_LargePositiveOffset_ClampsAtMax()
        {
            Assert.Equal(1800f, StationGainModel.ComputeBeatFrequency(0.9f));
        }

        [Fact]
        public void Beat_LargeNegativeOffset_ClampsAtMin()
        {
            Assert.Equal(300f, StationGainModel.ComputeBeatFrequency(-0.9f));
        }
    }
}
