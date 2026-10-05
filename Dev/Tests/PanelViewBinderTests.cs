using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class PanelViewBinderTests
    {
        private static StationDiagnostics Diag(string callsign, float gain, bool inBeam)
        {
            return new StationDiagnostics
            {
                Callsign = callsign,
                Gain = gain,
                InBeam = inBeam,
                SchedulerPhase = StationScheduler.Phase.Navigation,
                NavigationElapsed = 0f
            };
        }

        [Fact]
        public void BuildPatternRows_NullDiags_ReturnsEmpty()
        {
            Assert.Empty(PanelViewBinder.BuildPatternRows(null, 0, 1, 1, 5));
        }

        [Fact]
        public void BuildPatternRows_AllSilent_ReturnsEmpty()
        {
            var diags = new[] { Diag("ABC", 0f, false) };

            Assert.Empty(PanelViewBinder.BuildPatternRows(diags, 0, 1, 1, 5));
        }

        [Fact]
        public void BuildPatternRows_DominantRow_HasRecvCounts()
        {
            var diags = new[] { Diag("ABC", 0.9f, true) };

            PanelViewBinder.PatternRow[] rows =
                PanelViewBinder.BuildPatternRows(diags, 0, 3, 2, 5);

            Assert.Single(rows);
            Assert.Contains("Recv: 3\u2022 2\u25AC", rows[0].Text);
        }

        [Fact]
        public void BuildPatternRows_NonDominantRow_OmitsRecv()
        {
            var diags = new[]
            {
                Diag("ABC", 0.9f, true),
                Diag("DEF", 0.5f, false)
            };

            PanelViewBinder.PatternRow[] rows =
                PanelViewBinder.BuildPatternRows(diags, 0, 3, 2, 5);

            Assert.Equal(2, rows.Length);
            Assert.DoesNotContain("Recv:", rows[1].Text);
        }

        [Fact]
        public void BuildPatternRows_RespectsMaxRows()
        {
            var diags = new[]
            {
                Diag("A", 0.9f, true),
                Diag("B", 0.8f, false),
                Diag("C", 0.7f, false)
            };

            Assert.Equal(2, PanelViewBinder.BuildPatternRows(diags, 0, 0, 0, 2).Length);
        }

        [Fact]
        public void BuildPatternRows_InBeamFlag_PassedThrough()
        {
            var diags = new[]
            {
                Diag("A", 0.9f, true),
                Diag("B", 0.8f, false)
            };

            PanelViewBinder.PatternRow[] rows =
                PanelViewBinder.BuildPatternRows(diags, -1, 0, 0, 5);

            Assert.True(rows[0].InBeam);
            Assert.False(rows[1].InBeam);
        }

        [Fact]
        public void BuildStationText_NegativeDomIdx_ReturnsBase()
        {
            Assert.Equal("Seville",
                PanelViewBinder.BuildStationText("Seville", -1, default(StationDiagnostics)));
        }

        [Fact]
        public void BuildStationText_Dominant_AppendsPhaseSuffix()
        {
            var dom = Diag("ABC", 0.9f, true);

            Assert.Equal("Seville [Navigation]",
                PanelViewBinder.BuildStationText("Seville", 0, dom));
        }

        [Fact]
        public void BuildSignalView_NullRich_PlaceholderDimmed()
        {
            SignalPanelView.State view = PanelViewBinder.BuildSignalView(null, null);

            Assert.Equal("--- Signal: ", view.SignalText);
            Assert.True(view.Dimmed);
        }

        [Fact]
        public void BuildSignalView_Rich_ComposesBlock()
        {
            SignalPanelView.State view = PanelViewBinder.BuildSignalView("ABC", "ABC");

            Assert.Contains("ABC", view.SignalText);
            Assert.False(view.Dimmed);
            Assert.Equal("Decoded: ABC", view.DecodedText);
        }
    }
}
