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

        private int _pauseTicksRemaining;

        public StationScheduler(string callsign)
        {
            Callsign = new CallsignPattern(callsign);
            Navigation = new NavigationPattern(new Group[0], 8);
            CurrentConsolPattern = null;
            CurrentPhase = Phase.Pause;
            _pauseTicksRemaining = 1;
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

        public NavigationElementType[] GetCurrentConsolPattern()
        {
            switch (CurrentPhase)
            {
                case Phase.Callsign:
                case Phase.Navigation:
                    return CurrentConsolPattern;
                default:
                    return null;
            }
        }

        public void Tick()
        {
            switch (CurrentPhase)
            {
                case Phase.Pause:
                    _pauseTicksRemaining--;
                    if (_pauseTicksRemaining <= 0)
                        CurrentPhase = Phase.Callsign;
                    break;

                case Phase.PreNavPause:
                    _pauseTicksRemaining--;
                    if (_pauseTicksRemaining <= 0)
                        CurrentPhase = Phase.Navigation;
                    break;

                case Phase.PostNavSilence:
                    _pauseTicksRemaining--;
                    if (_pauseTicksRemaining <= 0)
                    {
                        CurrentPhase = Phase.Pause;
                        _pauseTicksRemaining = 1;
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
                    _pauseTicksRemaining = ToTicks(Config.PreNavPauseMs);
                    break;

                case Phase.Navigation:
                    CurrentPhase = Phase.PostNavSilence;
                    _pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
                    break;
            }
        }

        public void Reset()
        {
            CurrentPhase = Phase.PostNavSilence;
            _pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
        }

        private static int ToTicks(float ms)
        {
            return MorseTiming.ToTicks(ms);
        }
    }
}
