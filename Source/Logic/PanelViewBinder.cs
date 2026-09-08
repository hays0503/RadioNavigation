namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// View-model computation behind InformationPanelHandler (P5.7 slice 1,
    /// F03). The panel fetches controller state and applies Text/color;
    /// everything decidable from plain data lives here so it runs offline
    /// in PanelViewBinderTests. Pure C#.
    /// </summary>
    public static class PanelViewBinder
    {
        public struct PatternRow
        {
            public readonly string Text;
            public readonly bool InBeam;

            public PatternRow(string text, bool inBeam)
            {
                Text = text;
                InBeam = inBeam;
            }
        }

        public static PatternRow[] BuildPatternRows(StationDiagnostics[] diags, int domIdx,
            int recvDots, int recvDashes, int maxRows)
        {
            if (diags == null || maxRows <= 0)
                return new PatternRow[0];
            int[] packed = PatternRowPacker.Pack(diags, maxRows);
            var rows = new PatternRow[packed.Length];
            for (int r = 0; r < packed.Length; r++)
            {
                int i = packed[r];
                int dots = i == domIdx ? recvDots : 0;
                int dashes = i == domIdx ? recvDashes : 0;
                rows[r] = new PatternRow(
                    PatternLineFormatter.BuildLine(diags[i], i, domIdx, dots, dashes),
                    diags[i].InBeam);
            }
            return rows;
        }

        public static string BuildStationText(string baseText, int domIdx, StationDiagnostics domDiag)
        {
            // F101: no dominant station (-1) must not show the default
            // diagnostics phase ("[Pause]") of a non-existent station.
            if (domIdx < 0)
                return baseText;
            return PatternLineFormatter.StationSuffix(
                baseText, domDiag.SchedulerPhase, domDiag.NavigationElapsed);
        }

        public static SignalPanelView.State BuildSignalView(string rich, string decoded)
        {
            return SignalPanelView.Resolve(rich, DecodedDisplayTracker.DisplayText(decoded));
        }
    }
}
