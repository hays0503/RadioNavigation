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

        private IKeySequence sequence;
        private StationScheduler scheduler;
        private int elementIndex;
        private int ticksInState;
        private State state;
        private ConsolCycleConfig config;
        private int callsignLength;

        public bool KeyOn => state == State.ElementOn || state == State.Tone;
        public bool IsActive { get; private set; }
        public bool IsTone => state == State.Tone;
        public bool IsCallsignElement => consolPattern.IsNotNull() && elementIndex < callsignLength;

        public event Action<NavigationElementType, bool> OnElementCompleted;
        public event Action OnSequenceFinished;

        public void Load(IKeySequence sequence, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            this.sequence = sequence;
            this.scheduler = scheduler;
            this.config = config;
            elementIndex = 0;
            ticksInState = 0;
            state = State.Idle;
            callsignLength = 0;
            IsActive = sequence.IsNotNull() && sequence.ElementCount.IsPositive();

            if (IsActive)
                StartElement();
        }

        public void Load(NavigationPattern pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            Load((IKeySequence)pattern, scheduler, config);
        }

        public void LoadConsol(NavigationElementType[] pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig), int callsignLength = 0)
        {
            sequence = null;
            this.scheduler = scheduler;
            this.config = config;
            elementIndex = 0;
            ticksInState = 0;
            state = State.Idle;
            consolPattern = pattern;
            this.callsignLength = callsignLength;
            IsActive = pattern.IsNotNull() && pattern.Length.IsPositive();

            if (IsActive)
                StartElement();
        }

        private NavigationElementType[] consolPattern;

        public void Reset()
        {
            elementIndex = 0;
            ticksInState = 0;
            state = State.Idle;
            IsActive = false;
            consolPattern = null;
        }

        public void Advance()
        {
            if (!IsActive)
            {
                if (scheduler.IsNotNull())
                    scheduler.Tick();
                return;
            }

            ticksInState++;

            switch (state)
            {
                case State.ElementOn:
                    if (ticksInState >= GetCurrentSoundTicks())
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
                    if (ticksInState >= GetCurrentSilenceTicks())
                        AdvanceElement();
                    break;

                case State.Tone:
                    if (ticksInState >= MorseTiming.GetToneTicks(config))
                    {
                        OnElementCompleted?.Invoke(NavigationElementType.Equisignal, IsCallsignElement);
                        AdvanceElement();
                    }
                    break;
            }
        }

        private void StartElement()
        {
            if (elementIndex < consolPattern?.Length)
            {
                var elem = consolPattern[elementIndex];
                if (elem == NavigationElementType.Equisignal)
                {
                    state = State.Tone;
                    ticksInState = 0;
                    return;
                }
            }

            state = State.ElementOn;
            ticksInState = 0;
        }

        private void StartElementSilence()
        {
            state = State.ElementSilence;
            ticksInState = 0;
        }

        private void AdvanceElement()
        {
            elementIndex++;

            if (consolPattern.IsNotNull() && elementIndex >= consolPattern.Length)
            {
                OnSequenceFinished?.Invoke();
                if (scheduler.IsNotNull())
                {
                    scheduler.OnSequenceFinished();
                    var nextPattern = scheduler.GetCurrentConsolPattern();
                    if (nextPattern.IsNotNull() && nextPattern.Length.IsPositive())
                    {
                        consolPattern = nextPattern;
                        elementIndex = 0;
                        callsignLength = 0;
                        StartElement();
                        return;
                    }
                    else
                    {
                        state = State.Idle;
                        IsActive = false;
                        return;
                    }
                }
                else
                {
                    elementIndex = 0;
                }
            }
            else if (sequence.IsNotNull() && elementIndex >= sequence.ElementCount)
            {
                OnSequenceFinished?.Invoke();
                if (scheduler.IsNotNull())
                {
                    scheduler.OnSequenceFinished();
                    var nextSeq = scheduler.GetCurrentSequence();
                    if (nextSeq.IsNotNull() && nextSeq.ElementCount.IsPositive())
                    {
                        sequence = nextSeq;
                        elementIndex = 0;
                        StartElement();
                        return;
                    }
                    else
                    {
                        state = State.Idle;
                        IsActive = false;
                        return;
                    }
                }
                else
                {
                    elementIndex = 0;
                }
            }

            StartElement();
        }

        private NavigationElementType GetCurrentElementType()
        {
            if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
                return consolPattern[elementIndex];

            if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
                return sequence.GetElement(elementIndex) ? NavigationElementType.Dash : NavigationElementType.Dot;

            return NavigationElementType.Dot;
        }

        private int GetCurrentSoundTicks()
        {
            bool isDash = false;

            if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
                isDash = consolPattern[elementIndex] == NavigationElementType.Dash;
            else if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
                isDash = sequence.GetElement(elementIndex);

            return isDash ? MorseTiming.GetDashSoundTicks(config) : MorseTiming.GetDotSoundTicks(config);
        }

        private int GetCurrentSilenceTicks()
        {
            bool isDash = false;

            if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
                isDash = consolPattern[elementIndex] == NavigationElementType.Dash;
            else if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
                isDash = sequence.GetElement(elementIndex);

            return isDash ? MorseTiming.GetDashSilenceTicks(config) : MorseTiming.GetDotSilenceTicks(config);
        }


    }
}
