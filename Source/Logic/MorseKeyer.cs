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

        // F01/F29: read from the audio thread — volatile, no torn reads.
        private volatile bool keyOn;
        private volatile bool isActive;

        public bool KeyOn => keyOn;
        public bool IsActive => isActive;
        public bool IsTone => state == State.Tone;
        public bool IsCallsignElement => consolPattern.IsNotNull() && elementIndex < callsignLength;

        public event Action<NavigationElementType, bool> OnElementCompleted;
        public event Action OnSequenceFinished;

        public void Load(IKeySequence sequence, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            this.sequence = sequence;
            this.scheduler = scheduler;
            this.config = config;
            consolPattern = null; // F65: stale consol pattern must not win over the loaded sequence
            elementIndex = 0;
            ticksInState = 0;
            SetState(State.Idle);
            callsignLength = 0;
            isActive = sequence.IsNotNull() && sequence.ElementCount.IsPositive();

            if (isActive)
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
            SetState(State.Idle);
            consolPattern = pattern;
            this.callsignLength = callsignLength;
            isActive = pattern.IsNotNull() && pattern.Length.IsPositive();

            if (isActive)
                StartElement();
        }

        private NavigationElementType[] consolPattern;

        public void Reset()
        {
            elementIndex = 0;
            ticksInState = 0;
            SetState(State.Idle);
            isActive = false;
            // F165: clear both sources like Load/LoadConsol do — a stale
            // sequence must not keep feeding GetCurrentElementType() after
            // a reset.
            sequence = null;
            consolPattern = null;
        }

        public void Advance()
        {
            if (!isActive)
                return; // F01/F98: the scheduler is ticked by the caller (UpdateStations), exactly once per logical tick

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

        private void SetState(State newState)
        {
            state = newState;
            keyOn = newState == State.ElementOn || newState == State.Tone;
        }

        private void StartElement()
        {
            if (elementIndex < consolPattern?.Length)
            {
                var elem = consolPattern[elementIndex];
                if (elem == NavigationElementType.Equisignal)
                {
                    SetState(State.Tone);
                    ticksInState = 0;
                    return;
                }
            }

            SetState(State.ElementOn);
            ticksInState = 0;
        }

        private void StartElementSilence()
        {
            SetState(State.ElementSilence);
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
                        // F175: consol and sequence sources are mutually
                        // exclusive — clear the sequence when entering consol.
                        consolPattern = nextPattern;
                        sequence = null;
                        elementIndex = 0;
                        // F113: keep the callsign classification of the new
                        // pattern — 0 would merge callsign into navigation.
                        callsignLength = scheduler.CallsignElementCount;
                        StartElement();
                        return;
                    }
                    else
                    {
                        SetState(State.Idle);
                        isActive = false;
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
                        // F175: entering a plain sequence must clear any stale
                        // consol pattern and callsign classification.
                        sequence = nextSeq;
                        consolPattern = null;
                        callsignLength = 0;
                        elementIndex = 0;
                        StartElement();
                        return;
                    }
                    else
                    {
                        SetState(State.Idle);
                        isActive = false;
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

            // F114: a silent Dot fallback masked an out-of-range elementIndex;
            // surface it so an index bug is visible in the log instead of
            // producing a wrong-length tone.
            UnityEngine.Debug.LogError("[RadioNav][MorseKeyer] GetCurrentElementType: elementIndex " + elementIndex
                + " out of range (consolPattern.Length=" + (consolPattern.IsNotNull() ? consolPattern.Length : -1)
                + ", sequence.ElementCount=" + (sequence.IsNotNull() ? sequence.ElementCount : -1) + ")");
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
