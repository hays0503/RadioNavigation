namespace UBOAT.Mods.RadioNavigation
{
    public struct StationState
    {
        public bool InBeam;
        public bool IsDashSector;
        public int SectorNumber;
        public int TransitionIndex;
        public float Gain;
        public float BeatFrequency;
        public int DotCount;
        public float CurrentBearing;
        public float BearingToBoat;
        public bool HasNavigationBearing;
        public float LastNavigationBearing;
        public float NavigationStartTime;
        public ConsolCycleConfig Config;
        public NavigationPattern Pattern;
    }
}
