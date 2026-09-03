namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-station diagnostics snapshot. Hoisted top-level from
    /// SonneController (SRP — the snapshot shape is not controller logic)
    /// so the pure assembly in StationDiagnosticsReport is offline-testable
    /// without the MonoBehaviour.
    /// </summary>
    public struct StationDiagnostics
    {
        public string Name;
        public string Callsign;
        public bool InBeam;
        public bool IsDashSector;
        public int SectorNumber;
        public float Gain;
        public float BeatFrequency;
        public int TransitionIndex;
        public NavigationPattern Pattern;
        public StationScheduler.Phase SchedulerPhase;
        public int DotCount;
        public int EqSignIndex;
        public int DashCount;
        public float CurrentBearing;
        public float BearingToBoat;
        public float NavigationElapsed;
    }
}
