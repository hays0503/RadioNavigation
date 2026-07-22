namespace UBOAT.Mods.TestUI
{
    public class StationScheduler
    {
        public enum Phase
        {
            Pause,
            Callsign,
            Navigation,
            CyclePause
        }

        public CallsignPattern Callsign { get; private set; }
        public NavigationPattern Navigation { get; set; }
        public NavigationElementType[] CurrentConsolPattern { get; set; }
        public Phase CurrentPhase { get; private set; }
        public int PauseDurationTicks { get; set; } = 8;
        public int CyclePauseDurationTicks { get; set; } = 16;
        public int[] BearingTable { get; set; }
        public ConsolCycleConfig Config { get; set; }

        private int _pauseTicksRemaining;

        public StationScheduler(string callsign)
        {
            Callsign = new CallsignPattern(callsign);
            Navigation = new NavigationPattern(new Group[0], 8);
            CurrentConsolPattern = null;
            CurrentPhase = Phase.Pause;
            _pauseTicksRemaining = PauseDurationTicks;
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
            if (CurrentPhase == Phase.Pause || CurrentPhase == Phase.CyclePause)
                return null;
            return CurrentConsolPattern;
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

                case Phase.CyclePause:
                    _pauseTicksRemaining--;
                    if (_pauseTicksRemaining <= 0)
                    {
                        CurrentPhase = Phase.Pause;
                        _pauseTicksRemaining = PauseDurationTicks;
                    }
                    break;
            }
        }

        public void OnSequenceFinished()
        {
            switch (CurrentPhase)
            {
                case Phase.Pause:
                    CurrentPhase = Phase.CyclePause;
                    _pauseTicksRemaining = CyclePauseDurationTicks;
                    break;

                case Phase.Callsign:
                    CurrentPhase = Phase.Navigation;
                    break;

                case Phase.Navigation:
                    CurrentPhase = Phase.CyclePause;
                    _pauseTicksRemaining = CyclePauseDurationTicks;
                    break;
            }
        }

        public void Reset()
        {
            CurrentPhase = Phase.Pause;
            _pauseTicksRemaining = PauseDurationTicks;
        }

        public void CalculateCyclePause(int patternElementCount, float tickMs)
        {
            float patternDurationMs = patternElementCount * tickMs;
            float targetCycleMs = Config.MaxNavigationCount * Config.DotDurationMs * 2;
            float pauseMs = System.Math.Max(Config.MinCyclePauseMs, targetCycleMs - patternDurationMs);
            CyclePauseDurationTicks = System.Math.Max(1, (int)(pauseMs / tickMs));
        }
    }
}
