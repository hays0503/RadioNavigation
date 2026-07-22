using System;

namespace UBOAT.Mods.TestUI
{
    public class MorseKeyer
    {
        private enum State
        {
            Idle,
            ElementOn,
            Gap,
            Tone
        }

        private IKeySequence _sequence;
        private StationScheduler _scheduler;
        private int _elementIndex;
        private int _ticksInState;
        private State _state;
        private ConsolCycleConfig _config;
        private int _callsignLength;

        public bool KeyOn => _state == State.ElementOn || _state == State.Tone;
        public bool IsActive { get; private set; }
        public bool IsTone => _state == State.Tone;
        public bool IsCallsignElement => _consolPattern != null && _elementIndex < _callsignLength;

        public event Action<NavigationElementType, bool> OnElementCompleted;
        public event Action OnSequenceFinished;

        public void Load(IKeySequence sequence, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            _sequence = sequence;
            _scheduler = scheduler;
            _config = config;
            _elementIndex = 0;
            _ticksInState = 0;
            _state = State.Idle;
            _callsignLength = 0;
            IsActive = sequence != null && sequence.ElementCount > 0;

            if (IsActive)
                StartElement();
        }

        public void Load(NavigationPattern pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            Load((IKeySequence)pattern, scheduler, config);
        }

        public void LoadConsol(NavigationElementType[] pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig), int callsignLength = 0)
        {
            _sequence = null;
            _scheduler = scheduler;
            _config = config;
            _elementIndex = 0;
            _ticksInState = 0;
            _state = State.Idle;
            _consolPattern = pattern;
            _callsignLength = callsignLength;
            IsActive = pattern != null && pattern.Length > 0;

            if (IsActive)
                StartElement();
        }

        private NavigationElementType[] _consolPattern;

        public void Reset()
        {
            _elementIndex = 0;
            _ticksInState = 0;
            _state = State.Idle;
            IsActive = false;
            _consolPattern = null;
        }

        public void Advance()
        {
            if (!IsActive)
            {
                if (_scheduler != null)
                    _scheduler.Tick();
                return;
            }

            _ticksInState++;

            switch (_state)
            {
                case State.ElementOn:
                    if (_ticksInState >= GetCurrentElementTicks())
                    {
                        NavigationElementType elemType = GetCurrentElementType();
                        OnElementCompleted?.Invoke(elemType, IsCallsignElement);
                        StartGap();
                    }
                    break;

                case State.Tone:
                    if (_ticksInState >= GetToneTicks())
                    {
                        OnElementCompleted?.Invoke(NavigationElementType.Equisignal, IsCallsignElement);
                        StartGap();
                    }
                    break;

                case State.Gap:
                    int gapTicks = IsLastElement() ? GetCharGapTicks() : GetElementGapTicks();
                    if (_ticksInState >= gapTicks)
                        AdvanceElement();
                    break;
            }
        }

        private void StartElement()
        {
            if (_elementIndex < _consolPattern?.Length)
            {
                var elem = _consolPattern[_elementIndex];
                if (elem == NavigationElementType.Equisignal)
                {
                    _state = State.Tone;
                    _ticksInState = 0;
                    return;
                }
            }

            _state = State.ElementOn;
            _ticksInState = 0;
        }

        private void StartGap()
        {
            _state = State.Gap;
            _ticksInState = 0;
        }

        private void AdvanceElement()
        {
            _elementIndex++;

            if (_consolPattern != null && _elementIndex >= _consolPattern.Length)
            {
                OnSequenceFinished?.Invoke();
                if (_scheduler != null)
                {
                    _scheduler.OnSequenceFinished();
                    var nextPattern = _scheduler.GetCurrentConsolPattern();
                    if (nextPattern != null && nextPattern.Length > 0)
                    {
                        _consolPattern = nextPattern;
                        _elementIndex = 0;
                        _callsignLength = 0;
                        StartElement();
                        return;
                    }
                    else
                    {
                        _state = State.Idle;
                        IsActive = false;
                        return;
                    }
                }
                else
                {
                    _elementIndex = 0;
                }
            }
            else if (_sequence != null && _elementIndex >= _sequence.ElementCount)
            {
                OnSequenceFinished?.Invoke();
                if (_scheduler != null)
                {
                    _scheduler.OnSequenceFinished();
                    var nextSeq = _scheduler.GetCurrentSequence();
                    if (nextSeq != null && nextSeq.ElementCount > 0)
                    {
                        _sequence = nextSeq;
                        _elementIndex = 0;
                        StartElement();
                        return;
                    }
                    else
                    {
                        _state = State.Idle;
                        IsActive = false;
                        return;
                    }
                }
                else
                {
                    _elementIndex = 0;
                }
            }

            StartElement();
        }

        private bool IsLastElement()
        {
            if (_consolPattern != null)
                return _elementIndex >= _consolPattern.Length - 1;
            return _sequence != null && _elementIndex >= _sequence.ElementCount - 1;
        }

        private NavigationElementType GetCurrentElementType()
        {
            if (_consolPattern != null && _elementIndex < _consolPattern.Length)
                return _consolPattern[_elementIndex];

            if (_sequence != null && _elementIndex < _sequence.ElementCount)
                return _sequence.GetElement(_elementIndex) ? NavigationElementType.Dash : NavigationElementType.Dot;

            return NavigationElementType.Dot;
        }

        private int GetCurrentElementTicks()
        {
            if (_consolPattern != null)
            {
                var elem = _consolPattern[_elementIndex];
                if (elem == NavigationElementType.Equisignal)
                    return GetToneTicks();

                bool isDash = elem == NavigationElementType.Dash;
                return isDash ? GetDashTicks() : GetDotTicks();
            }

            if (_sequence != null)
            {
                return _sequence.GetElement(_elementIndex) ? GetDashTicks() : GetDotTicks();
            }

            return GetDotTicks();
        }

        private int GetDotTicks()
        {
            return Math.Max(1, (int)(_config.DotDurationMs / 50f));
        }

        private int GetDashTicks()
        {
            return Math.Max(1, (int)(_config.DashDurationMs / 50f));
        }

        private int GetToneTicks()
        {
            return Math.Max(1, (int)(_config.EquisignalMs / 50f));
        }

        private int GetElementGapTicks()
        {
            return Math.Max(1, (int)(_config.ElementGapMs / 50f));
        }

        private int GetCharGapTicks()
        {
            return Math.Max(1, (int)(_config.ElementGapMs * 3 / 50f));
        }

    }
}
