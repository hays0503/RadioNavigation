using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-station navigation update behind
    /// SonneController.UpdateStationNavigation: map bearings, in-beam /
    /// sector classification, bearing-change-gated table lookup and the
    /// scheduler pattern feed. Extracted (SRP — station math is not fleet
    /// management) so it runs offline against the real tables in
    /// StationNavigatorTests. Pure C#, no Unity dependency.
    /// </summary>
    public static class StationNavigator
    {
        private const float BearingChangeThresholdDeg = 0.2f;

        public static void UpdateStation(ref StationState state, BeaconStation station,
            float playerLat, float playerLon, StationScheduler scheduler)
        {
            float mapBoatToStation = GeoMath.MapBearingDeg(
                playerLat, playerLon,
                (float)station.Latitude, (float)station.Longitude);
            float mapBearingToBoat = GeoMath.MapBearingDeg(
                (float)station.Latitude, (float)station.Longitude,
                playerLat, playerLon);

            if (station.Role != BeaconStationRole.DirectionalSonne)
            {
                state.InBeam = false;
                state.DotCount = 0;
                state.IsDashSector = false;
                state.SectorNumber = 0;
                state.CurrentBearing = mapBoatToStation;
                state.BearingToBoat = mapBearingToBoat;
                state.HasNavigationBearing = false;
                state.LastNavigationBearing = 0f;
                state.TransitionIndex = 0;
                state.Pattern = new NavigationPattern(Array.Empty<Group>(), 8, 255);
                return;
            }

            state.CurrentBearing = mapBoatToStation;
            state.BearingToBoat = mapBearingToBoat;
            float signalBearing = (mapBearingToBoat + 360f) % 360f;
            state.InBeam = SonneSignalGenerator.CalculateInBeam(
                signalBearing,
                (float)station.BaseAngle,
                state.Config.SectorWidthDeg);
            state.SectorNumber = SonneSignalGenerator.CalculateSectorNumber(
                signalBearing,
                (float)station.BaseAngle);

            bool bearingChanged = !state.HasNavigationBearing
                || SonneSignalGenerator.AngularDistance(
                    state.LastNavigationBearing,
                    signalBearing) > BearingChangeThresholdDeg;
            if (bearingChanged)
            {
                state.HasNavigationBearing = true;
                state.LastNavigationBearing = signalBearing;

                NavigationElementType[] pattern;
                SonneBeamSignal signal;
                if (SonneBeam.TryGetNavigationPattern(
                    station.Name,
                    signalBearing,
                    out pattern,
                    out signal))
                {
                    state.DotCount = signal.DotCount;
                    // DotCount/DashCount are corrected CAP counts. TransitionIndex
                    // is the actual element index where the equisignal starts.
                    state.IsDashSector = signal.IsDashSector;
                    state.TransitionIndex = signal.LeadingCount;
                    state.Pattern = SonneSignalGenerator.ToNavigationPattern(pattern);

                    // The active keyer owns its current array. Replacing the
                    // scheduler reference is safe and makes the latest table
                    // result available for the next navigation phase.
                    scheduler.CurrentConsolPattern = pattern;
                }
            }
        }
    }
}
