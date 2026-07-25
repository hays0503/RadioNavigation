namespace UBOAT.Mods.RadioNavigation
{
    public struct StationState
    {
        public bool InBeam;
        public int TransitionIndex;
        public float Gain;
        public float BeatFrequency;
        public int DotCount;
        public int CurrentBearing;
        public int CallsignLength;
        public float NavigationStartTime;
        public ConsolCycleConfig Config;
        public NavigationPattern Pattern;
    }
}
