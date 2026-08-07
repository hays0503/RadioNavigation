using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // F01/F29: ProcessAudio runs on the Unity audio thread and must not
    // mutate shared state. The scheduler/keyer state machine is advanced on
    // the main thread via UpdateStations; the audio thread only reads
    // volatile flags (MorseKeyer.KeyOn/IsActive) and generates samples.
    public class AudioSynthesisEngine
    {
        public const int SampleRate = 44100;
        private const float TickIntervalSeconds = 0.05f; // 50 ms per logical tick

        private readonly Atmosphere atmosphere;
        private readonly ReceiverDsp dsp;
        // F94: double accumulator — float drift over hours of gameplay could
        // shift the 50 ms tick tempo.
        private double tickAccumulator;
        private float outputVolume = 1f;

        public float OutputVolume
        {
            get => outputVolume;
            set => outputVolume = Mathf.Clamp(value, 0f, 1f);
        }

        // F115: clear accumulated DSP state (QSB phase, AGC gain, filter
        // state) when the controller is destroyed; a recreated engine must
        // start from a clean state.
        public void Reset()
        {
            atmosphere.Reset();
            dsp.Reset();
            tickAccumulator = 0.0;
        }

        public float QualityFactor { get; set; } = Constants.QualityFactorBase;
        public float Feedback { get; private set; }
        public Atmosphere Atmosphere => atmosphere;
        public ReceiverDsp Dsp => dsp;

        public AudioSynthesisEngine()
        {
            atmosphere = new Atmosphere(Environment.TickCount);
            dsp = new ReceiverDsp(SampleRate);
        }

        public void ApplyFeedback(float angle)
        {
            Feedback = Mathf.Clamp01(angle / 140f);

            QualityFactor = Constants.QualityFactorBase + Feedback * Feedback * Constants.QualityFactorFeedbackPerUnit;

            atmosphere.NoiseFloor = Mathf.Lerp(0.004f, 0.0005f, Feedback);
            atmosphere.ImpulseProbability = Mathf.Lerp(0.002f, 0.0002f, Feedback);

            dsp.WhistleLevel = Mathf.Clamp01((Feedback - 0.75f) / 0.2f);
        }

        // Main thread: advance the scheduler/keyer state machines at 50 ms
        // logical ticks. radioOn gate (F132): no simulation while the radio is
        // off. currentTime stamps NavigationStartTime on the PreNavPause ->
        // Navigation transition (F13 design: never Time.time on the audio
        // thread; here everything already runs on the main thread).
        public void UpdateStations(float deltaSeconds, float currentTime, StationState[] stations, StationScheduler[] schedulers, MorseKeyer[] keyers, bool radioOn)
        {
            if (!radioOn)
                return;

            tickAccumulator += deltaSeconds;
            while (tickAccumulator >= TickIntervalSeconds)
            {
                tickAccumulator -= TickIntervalSeconds;

                for (int s = 0; s < stations.Length; s++)
                {
                    if (stations[s].Gain.IsNegligible(0.001f))
                        continue;

                    var sched = schedulers[s];
                    var phaseBefore = sched.CurrentPhase;

                    if (!keyers[s].IsActive)
                    {
                        bool phaseChanged = false;

                        var consolPattern = sched.GetCurrentConsolPattern();
                        if (consolPattern.IsNotNull() && consolPattern.Length.IsPositive())
                        {
                            keyers[s].LoadConsol(consolPattern, sched, stations[s].Config);
                        }
                        else
                        {
                            var seq = sched.GetCurrentSequence();
                            if (seq.IsNull() || seq.ElementCount.IsZero())
                            {
                                if (seq.IsNotNull())
                                {
                                    sched.OnSequenceFinished();
                                    phaseChanged = true;
                                }
                            }
                            else
                            {
                                keyers[s].Load(seq, sched, stations[s].Config);
                            }
                        }

                        // F98: exactly one scheduler tick per logical tick.
                        // The phase block above is not the tick — the idle
                        // keyer's scheduler advances here.
                        if (!phaseChanged)
                            sched.Tick();
                    }
                    else
                    {
                        keyers[s].Advance();
                    }

                    if (phaseBefore == StationScheduler.Phase.PreNavPause &&
                        sched.CurrentPhase == StationScheduler.Phase.Navigation)
                    {
                        stations[s].NavigationStartTime = currentTime;
                    }
                }
            }
        }

        // Audio thread: read-only. Only volatile MorseKeyer flags are sampled;
        // no scheduler/keyer mutations, no UnityEngine API.
        public void ProcessAudio(float[] data, int channels, StationState[] stations, MorseKeyer[] keyers, CarrierGenerator[] carriers, bool radioOn, int currentZoneIndex, int sonneZoneIndex)
        {
            if (currentZoneIndex != sonneZoneIndex)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            if (!radioOn)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            for (int i = 0; i < data.Length; i += channels)
            {
                float sample = 0f;
                for (int s = 0; s < stations.Length; s++)
                {
                    if (stations[s].Gain.IsNegligible(0.001f))
                        continue;
                    if (keyers[s].KeyOn)
                        sample += stations[s].Gain * carriers[s].NextSample();
                    else
                        carriers[s].NextSample();
                }

                sample = atmosphere.Apply(sample, SampleRate);
                sample = dsp.Process(sample, SampleRate);

                for (int c = 0; c < channels; c++)
                    data[i + c] = sample * outputVolume;
            }
        }
    }
}
