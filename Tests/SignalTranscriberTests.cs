using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// SignalTranscriber contract: the (element, callsign) -&gt; (glyph,
    /// entry type) mapping behind SonneController.OnStationElementCompleted.
    /// Extracted so the transcript alphabet is pinned offline.
    /// </summary>
    public sealed class SignalTranscriberTests
    {
        private static SignalEntry Map(NavigationElementType elem, bool isCallsign)
        {
            SignalTranscriber.MapElement(elem, isCallsign, out SignalCharType type, out char c);
            return new SignalEntry(c, type);
        }

        [Fact]
        public void Equisignal_MapsToEquals()
        {
            Assert.Equal(new SignalEntry('=', SignalCharType.Equisignal), Map(NavigationElementType.Equisignal, false));
            Assert.Equal(new SignalEntry('=', SignalCharType.Equisignal), Map(NavigationElementType.Equisignal, true));
        }

        [Fact]
        public void Dash_Navigation_MapsToDash()
        {
            Assert.Equal(new SignalEntry('-', SignalCharType.Dash), Map(NavigationElementType.Dash, false));
        }

        [Fact]
        public void Dash_Callsign_MapsToCallsign()
        {
            Assert.Equal(new SignalEntry('-', SignalCharType.Callsign), Map(NavigationElementType.Dash, true));
        }

        [Fact]
        public void Dot_Navigation_MapsToDot()
        {
            Assert.Equal(new SignalEntry('.', SignalCharType.Dot), Map(NavigationElementType.Dot, false));
        }

        [Fact]
        public void Dot_Callsign_MapsToCallsign()
        {
            Assert.Equal(new SignalEntry('.', SignalCharType.Callsign), Map(NavigationElementType.Dot, true));
        }
    }
}
