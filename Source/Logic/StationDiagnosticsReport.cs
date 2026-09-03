namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure assembly of the per-station diagnostics snapshot behind
    /// SonneController.GetStationDiagnostics. Extracted (SRP — field
    /// mapping is not controller logic) so it is pinned offline in
    /// StationDiagnosticsReportTests. Pure C#, no Unity dependency.
    /// </summary>
    public static class StationDiagnosticsReport
    {
        public static StationDiagnostics Build(BeaconStation station, string callsign,
            StationState state, StationScheduler.Phase phase, float navigationElapsed)
        {
            return new StationDiagnostics
            {
                Name = station.Name,
                Callsign = callsign,
                InBeam = state.InBeam,
                IsDashSector = state.IsDashSector,
                SectorNumber = state.SectorNumber,
                Gain = state.Gain,
                BeatFrequency = state.BeatFrequency,
                TransitionIndex = state.TransitionIndex,
                Pattern = state.Pattern,
                SchedulerPhase = phase,
                DotCount = state.DotCount,
                // EqSignIndex is the actual position of the equisignal in the
                // 60-element emitted pattern, after the lost-sign correction.
                EqSignIndex = state.TransitionIndex,
                DashCount = state.Config.MaxNavigationCount - state.DotCount,
                CurrentBearing = state.CurrentBearing,
                BearingToBoat = state.BearingToBoat,
                NavigationElapsed = navigationElapsed
            };
        }
    }
}
