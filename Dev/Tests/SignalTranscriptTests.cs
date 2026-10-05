using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// SignalTranscript contract: the live transcript state behind
    /// SonneController (entries, morse buffer, decoded callsign).
    /// Extracted so element feed, callsign decode, counters and clear
    /// run offline without the controller.
    /// </summary>
    public sealed class SignalTranscriptTests
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
        public void NavigationElements_AppearInRichText()
        {
            var transcript = new SignalTranscript();
            var station = Station("E");

            transcript.AddElement(NavigationElementType.Dot, false, station);
            transcript.AddElement(NavigationElementType.Dash, false, station);

            Assert.Contains("\u25CF", transcript.RichText);
            Assert.Contains("\u25AC", transcript.RichText);
        }

        [Fact]
        public void CallsignDecoded_OnFlushByNavigationElement()
        {
            var transcript = new SignalTranscript();
            var station = Station("E");

            transcript.AddElement(NavigationElementType.Dot, true, station);
            transcript.AddElement(NavigationElementType.Dot, false, station);

            Assert.Equal("E", transcript.DecodedText);
        }

        [Fact]
        public void CallsignDecoded_OnSequenceFinish()
        {
            var transcript = new SignalTranscript();
            var station = Station("T");

            transcript.AddElement(NavigationElementType.Dash, true, station);
            transcript.FinishSequence(station);

            Assert.Equal("T", transcript.DecodedText);
        }

        [Fact]
        public void Counters_CountOnlyNavigationDotsAndDashes()
        {
            var transcript = new SignalTranscript();
            var station = Station("E");

            transcript.AddElement(NavigationElementType.Dot, false, station);
            transcript.AddElement(NavigationElementType.Dot, false, station);
            transcript.AddElement(NavigationElementType.Dash, false, station);
            transcript.AddElement(NavigationElementType.Equisignal, false, station);
            transcript.AddElement(NavigationElementType.Dot, true, station);

            Assert.Equal(2, transcript.CountDots());
            Assert.Equal(1, transcript.CountDashes());
        }

        [Fact]
        public void Clear_ResetsEverything()
        {
            var transcript = new SignalTranscript();
            var station = Station("E");
            transcript.AddElement(NavigationElementType.Dot, true, station);
            transcript.AddElement(NavigationElementType.Dot, false, station);

            transcript.Clear();

            Assert.Equal(string.Empty, transcript.RichText);
            Assert.Equal(string.Empty, transcript.DecodedText);
            Assert.Equal(0, transcript.CountDots());
            Assert.Equal(0, transcript.CountDashes());
        }
    }
}
