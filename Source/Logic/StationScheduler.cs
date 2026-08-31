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
        public ConsolCycleConfig Config { get; set; }
        // Used to skip a missing callsign phase without stalling the scheduler.
        public int CallsignElementCount { get; private set; }

        private int pauseTicksRemaining;

        public StationScheduler(string callsign)
        {
            Callsign = new CallsignPattern(callsign);
            Navigation = new NavigationPattern(new Group[0], 8);
            CurrentConsolPattern = null;
            CallsignElementCount = SonneSignalGenerator.GetCallsignElementCount(callsign);
            // F87: with no callsign elements (unknown callsign) the Callsign
            // phase would never finish — MorseKeyer stays idle on an empty
            // sequence and the cycle would stall forever. Skip straight to
            // the pre-navigation pause (one tick; Config.PreNavPauseMs would
            // need Config, which is assigned after construction).
            if (CallsignElementCount.IsZero())
            {
                CurrentPhase = Phase.PreNavPause;
                pauseTicksRemaining = 1;
            }
            else
            {
                CurrentPhase = Phase.Pause;
                pauseTicksRemaining = 1;
            }
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
                case Phase.Callsign:
                case Phase.Navigation:
                    break;

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
