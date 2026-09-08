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

        // F01/F29: read from the audio thread — volatile, no torn reads.
        private volatile bool keyOn;
        private volatile bool isActive;
        // Pitch-split support: element type of the currently keyed sound,
        // published for the audio thread the same way as keyOn/isActive.
        private volatile bool currentIsDash;

        public bool KeyOn => keyOn;
        public bool IsActive => isActive;
        public bool IsTone => state == State.Tone;
        public bool CurrentElementIsDash => currentIsDash;

        // Probe diagnostic: elements completed while IsCallsignElement was
        // true. Zero growth during live Callsign phases localizes the flag
        // loss to the keyer/scheduler side rather than the decoder gate.
        public static int CallsignFlaggedElements;

        public bool HasSequence => sequence.IsNotNull();
        public int ElementIndex => elementIndex;

        public bool IsCallsignElement =>
            sequence.IsNotNull()
            && scheduler.IsNotNull()
            && scheduler.CurrentPhase == StationScheduler.Phase.Callsign;

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
            isActive = sequence.IsNotNull() && sequence.ElementCount.IsPositive();

            if (isActive)
                StartElement();
        }

        public void Load(NavigationPattern pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            Load((IKeySequence)pattern, scheduler, config);
        }

        public void LoadConsol(NavigationElementType[] pattern, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
        {
            sequence = null;
            this.scheduler = scheduler;
            this.config = config;
            elementIndex = 0;
            ticksInState = 0;
            SetState(State.Idle);
            consolPattern = pattern;
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
            currentIsDash = false;
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
                case State.Idle:
                    break;

                case State.ElementOn:
                    if (ticksInState >= GetCurrentSoundTicks())
                    {
                        NavigationElementType elemType = GetCurrentElementType();
                        bool isCallsign = IsCallsignElement;
                        if (isCallsign)
                            CallsignFlaggedElements++;
                        OnElementCompleted?.Invoke(elemType, isCallsign);

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
            currentIsDash = IsCurrentElementDash();
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
                if (scheduler.IsNull())
                {
                    // F07: no scheduler means no next pattern can be fetched —
                    // resetting elementIndex would loop the same pattern
                    // forever; go idle instead.
                    SetState(State.Idle);
                    isActive = false;
                    return;
                }
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
                        // consol pattern.
                        sequence = nextSeq;
                        consolPattern = null;
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

        // F184: single source check shared by the tick getters.
        private bool IsCurrentElementDash()
        {
            if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
                return consolPattern[elementIndex] == NavigationElementType.Dash;
            if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
                return sequence.GetElement(elementIndex);
            return false;
        }

        private int GetCurrentSoundTicks()
        {
            if (IsCallsignTiming())
            {
                return IsCurrentElementDash()
                    ? MorseTiming.GetCallsignDashSoundTicks(config)
                    : MorseTiming.GetCallsignDotSoundTicks(config);
            }

            return IsCurrentElementDash()
                ? MorseTiming.GetDashSoundTicks(config)
                : MorseTiming.GetDotSoundTicks(config);
        }

        private int GetCurrentSilenceTicks()
        {
            if (IsCallsignTiming())
            {
                return IsCurrentElementDash()
                    ? MorseTiming.GetCallsignDashSilenceTicks(config)
                    : MorseTiming.GetCallsignDotSilenceTicks(config);
            }

            return IsCurrentElementDash()
                ? MorseTiming.GetDashSilenceTicks(config)
                : MorseTiming.GetDotSilenceTicks(config);
        }

        private bool IsCallsignTiming()
        {
            return sequence.IsNotNull()
                && scheduler.IsNotNull()
                && scheduler.CurrentPhase == StationScheduler.Phase.Callsign;
        }


    }
}
