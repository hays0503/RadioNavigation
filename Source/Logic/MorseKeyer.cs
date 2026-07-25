using System;

namespace UBOAT.Mods.RadioNavigation
{
    public class MorseKeyer
    {
        private enum State
        {
            Idle,
            ElementOn,
            ElementSilence,
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
                    if (_ticksInState >= GetCurrentSoundTicks())
                    {
                        NavigationElementType elemType = GetCurrentElementType();
                        OnElementCompleted?.Invoke(elemType, IsCallsignElement);

                        if (elemType == NavigationElementType.Equisignal)
                            AdvanceElement();
                        else
                            StartElementSilence();
                    }
                    break;

                case State.ElementSilence:
                    if (_ticksInState >= GetCurrentSilenceTicks())
                        AdvanceElement();
                    break;

                case State.Tone:
                    if (_ticksInState >= MorseTiming.GetToneTicks(_config))
                    {
                        OnElementCompleted?.Invoke(NavigationElementType.Equisignal, IsCallsignElement);
                        AdvanceElement();
                    }
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

        private void StartElementSilence()
        {
            _state = State.ElementSilence;
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

        private NavigationElementType GetCurrentElementType()
        {
            if (_consolPattern != null && _elementIndex < _consolPattern.Length)
                return _consolPattern[_elementIndex];

            if (_sequence != null && _elementIndex < _sequence.ElementCount)
                return _sequence.GetElement(_elementIndex) ? NavigationElementType.Dash : NavigationElementType.Dot;

            return NavigationElementType.Dot;
        }

        private int GetCurrentSoundTicks()
        {
            bool isDash = false;

            if (_consolPattern != null && _elementIndex < _consolPattern.Length)
                isDash = _consolPattern[_elementIndex] == NavigationElementType.Dash;
            else if (_sequence != null && _elementIndex < _sequence.ElementCount)
                isDash = _sequence.GetElement(_elementIndex);

            return isDash ? MorseTiming.GetDashSoundTicks(_config) : MorseTiming.GetDotSoundTicks(_config);
        }

        private int GetCurrentSilenceTicks()
        {
            bool isDash = false;

            if (_consolPattern != null && _elementIndex < _consolPattern.Length)
                isDash = _consolPattern[_elementIndex] == NavigationElementType.Dash;
            else if (_sequence != null && _elementIndex < _sequence.ElementCount)
                isDash = _sequence.GetElement(_elementIndex);

            return isDash ? MorseTiming.GetDashSilenceTicks(_config) : MorseTiming.GetDotSilenceTicks(_config);
        }


    }
}
