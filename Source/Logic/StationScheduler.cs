namespace UBOAT.Mods.RadioNavigation
{
    public class StationScheduler
    {
        public enum Phase
        {
            Pause,
            Callsign,
            PreNavPause,
            Navigation,
            PostNavSilence
        }

        public CallsignPattern Callsign { get; private set; }
        public NavigationPattern Navigation { get; set; }
        public NavigationElementType[] CurrentConsolPattern { get; set; }
        public Phase CurrentPhase { get; private set; }
        public int[] BearingTable { get; set; }
        public ConsolCycleConfig Config { get; set; }
        // F113: the morse element count of the callsign — survives pattern
        // transitions inside MorseKeyer.AdvanceElement.
        public int CallsignElementCount { get; private set; }

        private int pauseTicksRemaining;

        public StationScheduler(string callsign)
        {
            Callsign = new CallsignPattern(callsign);
            Navigation = new NavigationPattern(new Group[0], 8);
            CurrentConsolPattern = null;
            CurrentPhase = Phase.Pause;
            pauseTicksRemaining = 1;
            CallsignElementCount = SonneSignalGenerator.GetCallsignElementCount(callsign);
        }

        public IKeySequence GetCurrentSequence()
        {
            switch (CurrentPhase)
            {
                case Phase.Callsign:
                    return Callsign;
                case Phase.Navigation:
                    return Navigation;
                default:
                    return null;
            }
        }

        // F201: the full consol pattern must only play during Navigation;
        // in the Callsign phase GetCurrentSequence() provides the short
        // callsign pattern, otherwise the pattern would be transmitted twice
        // per cycle and the callsign-only phase would never be heard.
        public NavigationElementType[] GetCurrentConsolPattern()
        {
            if (CurrentPhase != Phase.Navigation)
                return null;
            return CurrentConsolPattern;
        }

        public void Tick()
        {
            switch (CurrentPhase)
            {
                case Phase.Pause:
                    pauseTicksRemaining--;
                    if (pauseTicksRemaining.IsNonPositive())
                        CurrentPhase = Phase.Callsign;
                    break;

                case Phase.PreNavPause:
                    pauseTicksRemaining--;
                    if (pauseTicksRemaining.IsNonPositive())
                        CurrentPhase = Phase.Navigation;
                    break;

                case Phase.PostNavSilence:
                    pauseTicksRemaining--;
                    if (pauseTicksRemaining.IsNonPositive())
                    {
                        CurrentPhase = Phase.Pause;
                        pauseTicksRemaining = 1;
                    }
                    break;
            }
        }

        public void OnSequenceFinished()
        {
            switch (CurrentPhase)
            {
                case Phase.Callsign:
                    CurrentPhase = Phase.PreNavPause;
                    pauseTicksRemaining = ToTicks(Config.PreNavPauseMs);
                    break;

                case Phase.Navigation:
                    CurrentPhase = Phase.PostNavSilence;
                    pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
                    break;

                default:
                    UnityEngine.Debug.LogWarning("[RadioNav][StationScheduler] OnSequenceFinished called during " + CurrentPhase + ", ignoring");
                    break;
            }
        }

        public void Reset()
        {
            CurrentPhase = Phase.Pause;
            pauseTicksRemaining = 1;
        }

        private static int ToTicks(float ms)
        {
            return MorseTiming.ToTicks(ms);
        }
    }
}
