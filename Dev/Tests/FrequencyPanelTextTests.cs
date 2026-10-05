using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// FrequencyPanelText contract: the frequency/station header lines
    /// behind InformationPanelHandler.OnFrequencyChanged. Extracted so
    /// line assembly runs offline.
    /// </summary>
    public sealed class FrequencyPanelTextTests
    {
        [Fact]
        public void FreqLine_AssemblesFormattedBand()
        {
            string line = FrequencyPanelText.FreqLine(266f, 1).Replace(',', '.');

            Assert.Equal("Freq: 266.0 kHz (Band II)", line);
        }

        [Fact]
        public void FreqLine_BadZone_ShowsUnknownBand()
        {
            string line = FrequencyPanelText.FreqLine(266f, 99);

            Assert.Contains("(Band ?)", line);
        }

        [Fact]
        public void StationName_ExactStationFrequency_Matches()
        {
            string name = FrequencyPanelText.StationName(257f, 1);

            Assert.Contains("FRQ", name);
        }

        [Fact]
        public void StationName_NonZone1_IsEmpty()
        {
            Assert.Equal(string.Empty, FrequencyPanelText.StationName(100f, 0));
        }
    }
}
