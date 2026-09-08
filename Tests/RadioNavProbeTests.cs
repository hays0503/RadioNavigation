using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// RadioNavProbe JSON snapshot v2: identity fields, decoded text,
    /// dominant flag, empty fleet, NaN safety.
    /// </summary>
    public sealed class RadioNavProbeTests
    {
        private static StationDiagnostics[] Fleet()
        {
            return new StationDiagnostics[]
            {
                new StationDiagnostics { Name = "Bushmills", Callsign = "MWN", Gain = 0.5f, CurrentBearing = 10f, BearingToBoat = 12f, DotCount = 3, DashCount = 2, NavigationElapsed = 12.5f, SchedulerPhase = StationScheduler.Phase.Navigation },
                new StationDiagnostics { Name = "Stavanger", Callsign = "LEG", Gain = 0.9f, CurrentBearing = 20f, BearingToBoat = 22f, DotCount = 0 },
            };
        }

        [Fact]
        public void Build_FullSnapshot_ContainsAllFields()
        {
            string json = RadioNavProbeJson.Build(true, 45.5f, 266f, 1, true, 59.1f, 2.0f, 1, Fleet(), "MWN", true, 3, 7, true, true, 2, 5, 9, 7, 1, "stack", 2, 1, 7, 7);
            Assert.Contains("\"loaded\":true", json);
            Assert.Contains("\"visible\":true", json);
            Assert.Contains("\"radioOn\":true", json);
            Assert.Contains("\"csChars\":3", json);
            Assert.Contains("\"csElemCount\":7", json);
            Assert.Contains("\"keyerActive\":true", json);
            Assert.Contains("\"kSeq\":true", json);
            Assert.Contains("\"kIdx\":2", json);
            Assert.Contains("\"csEmitted\":5", json);
            Assert.Contains("\"csSeen\":9", json);
            Assert.Contains("\"csProc\":7", json);
            Assert.Contains("\"clears\":1", json);
            Assert.Contains("\"clearStack\":\"stack\"", json);
            Assert.Contains("\"decAtt\":2", json);
            Assert.Contains("\"decOk\":1", json);
            Assert.Contains("\"bufLen\":7", json);
            Assert.Contains("\"expLen\":7", json);
            Assert.Contains("\"angleDeg\":45.5", json);
            Assert.Contains("\"frequencyKHz\":266", json);
            Assert.Contains("\"zone\":1", json);
            Assert.Contains("\"dominant\":1", json);
            Assert.Contains("\"decoded\":\"MWN\"", json);
            Assert.Contains("\"stationCount\":2", json);
            Assert.Contains("\"name\":\"Stavanger\"", json);
            Assert.Contains("\"callsign\":\"LEG\"", json);
            Assert.Contains("\"gain\":0.9", json);
            Assert.Contains("\"dots\":3", json);
            Assert.Contains("\"phase\":\"Navigation\"", json);
            Assert.Contains("\"dashes\":2", json);
            Assert.Contains("\"elapsed\":12.5", json);
        }

        [Fact]
        public void Build_DominantFlag_MarksOnlyDominantStation()
        {
            string json = RadioNavProbeJson.Build(false, 0f, -1f, -1, false, 0f, 0f, 0, Fleet(), null, false, 0, -1, false, false, -1, 0, 0, 0, 0, "", 0, 0, -1, -1);
            Assert.Contains("\"decoded\":null", json);
            Assert.Contains("{\"name\":\"Bushmills\"", json);
            int first = json.IndexOf("\"dominant\":true");
            int last = json.LastIndexOf("\"dominant\":true");
            Assert.NotEqual(-1, first);
            Assert.Equal(first, last);
        }

        [Fact]
        public void Build_NullFleet_EmptyArray()
        {
            string json = RadioNavProbeJson.Build(false, 0f, -1f, -1, false, 0f, 0f, -1, null, null, false, 0, -1, false, false, -1, 0, 0, 0, 0, "", 0, 0, -1, -1);
            Assert.Contains("\"stationCount\":0", json);
            Assert.Contains("\"stations\":[]", json);
        }

        [Fact]
        public void Build_NaNFloats_NullLiterals()
        {
            StationDiagnostics[] fleet = new StationDiagnostics[] { new StationDiagnostics { Gain = float.NaN } };
            string json = RadioNavProbeJson.Build(true, 0f, 0f, 0, false, 0f, 0f, -1, fleet, null, false, 0, -1, false, false, -1, 0, 0, 0, 0, "", 0, 0, -1, -1);
            Assert.Contains("\"gain\":null", json);
        }
    }
}
