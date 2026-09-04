using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// PatternLineFormatter contract: the per-station pattern card line +
    /// mm:ss duration behind InformationPanelHandler. Extracted so line
    /// assembly runs offline on synthetic diagnostics.
    /// </summary>
    public sealed class PatternLineFormatterTests
    {
        private static StationDiagnostics Diag()
        {
            return new StationDiagnostics
            {
                Name = "TestStation",
                Callsign = "TT",
                InBeam = true,
                IsDashSector = true,
                SectorNumber = 2,
                Gain = 0.89f,
                BeatFrequency = 1500f,
                TransitionIndex = 40,
                EqSignIndex = 40,
                DotCount = 25,
                DashCount = 35,
                CurrentBearing = 100.5f,
                BearingToBoat = 280.5f,
                SchedulerPhase = StationScheduler.Phase.Navigation,
                NavigationElapsed = 65f,
            };
        }

        [Fact]
        public void BuildLine_Dominant_ContainsAllFields()
        {
            string line = PatternLineFormatter.BuildLine(Diag(), 0, 0, 3, 7);

            Assert.Contains("TT", line);
            Assert.Contains("[IN BEAM]", line);
            Assert.Contains("89%", line);
            Assert.Contains("BFO:1500Hz", line);
            Assert.Contains("Navigation", line);
            Assert.Contains("ToBoat:280.5\u00B0", line);
            Assert.Contains("01:05", line);
            Assert.Contains("Zone:S2(DASH)", line);
            Assert.Contains("EQ@40/60", line);
            Assert.Contains("Dots:25", line);
            Assert.Contains("Dashes:35", line);
            Assert.Contains("Bearing:100.5\u00B0", line);
            Assert.Contains("Recv: 3\u2022 7\u25AC", line);
        }

        [Fact]
        public void BuildLine_NonDominant_OmitsRecv()
        {
            string line = PatternLineFormatter.BuildLine(Diag(), 1, 0, 3, 7);

            Assert.DoesNotContain("Recv:", line);
        }

        [Fact]
        public void BuildLine_NullCallsign_ShowsUnknown()
        {
            StationDiagnostics diag = Diag();
            diag.Callsign = null;

            string line = PatternLineFormatter.BuildLine(diag, 0, 0, 0, 0);

            Assert.Contains("?", line);
        }

        [Fact]
        public void BuildLine_OutsideNoElapsed()
        {
            StationDiagnostics diag = Diag();
            diag.InBeam = false;
            diag.IsDashSector = false;
            diag.NavigationElapsed = 0f;

            string line = PatternLineFormatter.BuildLine(diag, 0, 1, 0, 0);

            Assert.Contains("[OUTSIDE]", line);
            Assert.Contains("(DOT)", line);
            Assert.DoesNotContain("01:05", line);
        }

        [Fact]
        public void FormatDuration_ReferencePoints()
        {
            Assert.Equal("00:00", PatternLineFormatter.FormatDuration(0f));
            Assert.Equal("01:05", PatternLineFormatter.FormatDuration(65f));
            Assert.Equal("02:05", PatternLineFormatter.FormatDuration(125f));
            Assert.Equal("59:59", PatternLineFormatter.FormatDuration(3599f));
        }
    }
}
