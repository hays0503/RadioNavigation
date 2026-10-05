using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Locks StationCallsignResolver (P5.6 SC-1), extracted from
    /// SonneController.TryGetCallsignFromHistory: resolves a station's callsign
    /// from its first history entry, reporting absence (F87) rather than
    /// fabricating one.
    /// </summary>
    public sealed class StationCallsignResolverTests
    {
        private static BeaconStation WithHistory(BeaconHistoryEntry[] history)
        {
            return new BeaconStation { History = history };
        }

        [Fact]
        public void TryGet_FirstHistoryCallsign_ReturnsTrue()
        {
            var station = WithHistory(new[]
            {
                new BeaconHistoryEntry { Callsign = "FRQ", FrequencyKHz = 257f },
                new BeaconHistoryEntry { Callsign = "OLD", FrequencyKHz = 200f }
            });
            Assert.True(StationCallsignResolver.TryGet(station, out string cs));
            Assert.Equal("FRQ", cs);
        }

        [Fact]
        public void TryGet_NullHistory_ReturnsFalse()
        {
            var station = WithHistory(null);
            Assert.False(StationCallsignResolver.TryGet(station, out string cs));
            Assert.Null(cs);
        }

        [Fact]
        public void TryGet_EmptyHistory_ReturnsFalse()
        {
            var station = WithHistory(new BeaconHistoryEntry[0]);
            Assert.False(StationCallsignResolver.TryGet(station, out string cs));
            Assert.Null(cs);
        }

        [Fact]
        public void TryGet_EmptyCallsign_ReturnsFalse()
        {
            var station = WithHistory(new[] { new BeaconHistoryEntry { Callsign = "" } });
            Assert.False(StationCallsignResolver.TryGet(station, out string cs));
            Assert.Null(cs);
        }
    }
}
