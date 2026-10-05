using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// SignalDecoder contract: the dominant-station gate + transcript
    /// ownership behind SonneController's element/sequence handlers.
    /// </summary>
    public sealed class SignalDecoderTests
    {
        private static BeaconStation Station(string callsign)
        {
            return new BeaconStation
            {
                Name = "TestStation",
                Latitude = 50.0,
                Longitude = 0.0,
                History = new[]
                {
                    new BeaconHistoryEntry { Callsign = callsign },
                },
            };
        }

        [Fact]
        public void AddElement_NonDominant_Ignored()
        {
            var decoder = new SignalDecoder();

            decoder.AddElement(NavigationElementType.Dot, false, Station("E"), false);

            Assert.Equal(0, decoder.CountDots());
            Assert.Equal("", decoder.RichText.Trim());
        }

        [Fact]
        public void AddElement_DominantDot_Counted()
        {
            var decoder = new SignalDecoder();

            decoder.AddElement(NavigationElementType.Dot, false, Station("E"), true);

            Assert.Equal(1, decoder.CountDots());
            Assert.Contains("\u25CF", decoder.RichText);
        }

        [Fact]
        public void CountDashes_DominantDash()
        {
            var decoder = new SignalDecoder();

            decoder.AddElement(NavigationElementType.Dash, false, Station("T"), true);

            Assert.Equal(1, decoder.CountDashes());
            Assert.Equal(0, decoder.CountDots());
        }

        [Fact]
        public void FinishSequence_NonDominant_Ignored()
        {
            var decoder = new SignalDecoder();
            var station = Station("E");

            decoder.AddElement(NavigationElementType.Dot, true, station, true);
            decoder.FinishSequence(station, false);

            Assert.Equal("", decoder.DecodedText);
        }

        [Fact]
        public void FinishSequence_Dominant_DecodesCallsign()
        {
            var decoder = new SignalDecoder();
            var station = Station("E");

            decoder.AddElement(NavigationElementType.Dot, true, station, true);
            decoder.FinishSequence(station, true);

            Assert.Equal("E", decoder.DecodedText);
        }

        [Fact]
        public void Clear_ResetsCountsAndText()
        {
            var decoder = new SignalDecoder();
            var station = Station("E");

            decoder.AddElement(NavigationElementType.Dot, true, station, true);
            decoder.FinishSequence(station, true);
            decoder.Clear();

            Assert.Equal(0, decoder.CountDots());
            Assert.Equal("", decoder.DecodedText);
        }
    }
}
