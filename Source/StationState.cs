namespace UBOAT.Mods.TestUI
{
    public struct StationState
    {
        public bool InBeam;
        public int TransitionIndex;
        public float Gain;
        public float BeatFrequency;
        public ConsolCycleConfig Config;
        public int DotCount;
        public int CurrentBearing;
        public NavigationPattern Pattern;
        public StationScheduler Scheduler;
        public MorseKeyer Keyer;
        public CarrierGenerator Carrier;
        public int CallsignLength;
    }
}
