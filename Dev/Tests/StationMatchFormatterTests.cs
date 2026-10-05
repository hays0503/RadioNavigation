using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Locks StationMatchFormatter (P5.7 IPH-3), extracted from
    /// InformationPanelHandler.FindClosestStation. Reference stations from
    /// BeaconData: Quimper/Ploneis FRQ 257, Stavanger LEC 319, Lugo LG 285,
    /// Seville SL 315 kHz; match window is Constants.StationMatchDeltaKHz=20.
    /// </summary>
    public sealed class StationMatchFormatterTests
    {
        [Fact]
        public void Format_NonZone1_ReturnsEmpty()
        {
            Assert.Equal("", StationMatchFormatter.Format(300f, 0));
            Assert.Equal("", StationMatchFormatter.Format(300f, 2));
        }

        [Fact]
        public void Format_ExactStationFrequency_MatchesCallsign()
        {
            // 257 kHz is Quimper/Ploneis (FRQ).
            string line = StationMatchFormatter.Format(257f, 1);
            Assert.Contains("FRQ", line);
            Assert.Contains("257", line);
        }

        [Fact]
        public void Format_NearestStation_Wins()
        {
            // 286 kHz is closest to Lugo (LG, 285).
            string line = StationMatchFormatter.Format(286f, 1);
            Assert.Contains("LG", line);
        }

        [Fact]
        public void Format_FarFromAnyStation_ReturnsEmpty()
        {
            // 100 kHz is >20 kHz from every station.
            Assert.Equal("", StationMatchFormatter.Format(100f, 1));
        }

        [Fact]
        public void Format_GainIncreases_CloserToStation()
        {
            // Parse the trailing gain% and check it grows as we approach 285.
            int gainAt10 = GainOf(StationMatchFormatter.Format(295f, 1));
            int gainAt1 = GainOf(StationMatchFormatter.Format(286f, 1));
            Assert.True(gainAt1 > gainAt10);
        }

        private static int GainOf(string line)
        {
            if (string.IsNullOrEmpty(line))
                return -1;
            int pct = line.LastIndexOf('%');
            int sp = line.LastIndexOf(' ', pct);
            return int.Parse(line.Substring(sp + 1, pct - sp - 1));
        }
    }
}
