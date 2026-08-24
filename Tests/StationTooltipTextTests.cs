using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Tooltip text for the station map markers (StationMapMarker hover and
    /// pinned states). Lines with missing data must be omitted; frequency
    /// digits must be invariant-cultured (no decimal comma on RU locales).
    /// </summary>
    public sealed class StationTooltipTextTests
    {
        private static BeaconStation Station(
            string name = "Bushmills",
            string country = "UK",
            string callsign = "MWN",
            float? frequencyKHz = 266f,
            BeaconStationRole role = BeaconStationRole.DirectionalSonne)
        {
            return new BeaconStation
            {
                Name = name,
                Country = country,
                Role = role,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        Callsign = callsign,
                        FrequencyKHz = frequencyKHz
                    }
                }
            };
        }

        [Fact]
        public void Hover_ReturnsNameOnly()
        {
            Assert.Equal("Bushmills", StationTooltipText.Hover(Station()));
        }

        [Fact]
        public void Hover_NullName_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, StationTooltipText.Hover(Station(name: null)));
        }

        [Fact]
        public void Pinned_FullData_ThreeLines()
        {
            string text = StationTooltipText.Pinned(Station());
            Assert.Equal("Bushmills\nMWN · 266 kHz\nUK · Directional Sonne", text);
        }

        [Fact]
        public void Pinned_RadioBeaconOnly_RoleLabel()
        {
            string text = StationTooltipText.Pinned(Station(role: BeaconStationRole.RadioBeaconOnly));
            Assert.Contains("Radio beacon", text);
        }

        [Fact]
        public void Pinned_FractionalFrequency_OneDecimal()
        {
            string text = StationTooltipText.Pinned(Station(frequencyKHz: 257.5f));
            Assert.Contains("257.5 kHz", text);
        }

        [Fact]
        public void Pinned_NoCallsign_OmitsCallsignKeepsFrequency()
        {
            string text = StationTooltipText.Pinned(Station(callsign: null));
            Assert.Equal("Bushmills\n266 kHz\nUK · Directional Sonne", text);
        }

        [Fact]
        public void Pinned_NoFrequency_OmitsFrequencyKeepsCallsign()
        {
            string text = StationTooltipText.Pinned(Station(frequencyKHz: null));
            Assert.Equal("Bushmills\nMWN\nUK · Directional Sonne", text);
        }

        [Fact]
        public void Pinned_NoHistory_SignalLineOmitted()
        {
            BeaconStation station = Station();
            station.History = null;
            Assert.Equal("Bushmills\nUK · Directional Sonne", StationTooltipText.Pinned(station));
        }

        [Fact]
        public void Pinned_NoCountry_RoleLineKeepsRole()
        {
            string text = StationTooltipText.Pinned(Station(country: null));
            Assert.Equal("Bushmills\nMWN · 266 kHz\nDirectional Sonne", text);
        }

        [Fact]
        public void Pinned_EmptyHistoryEntry_AllLinesStillFormed()
        {
            BeaconStation station = Station();
            station.History = new[] { new BeaconHistoryEntry { Callsign = null, FrequencyKHz = null } };
            Assert.Equal("Bushmills\nUK · Directional Sonne", StationTooltipText.Pinned(station));
        }
    }
}
