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
        /// <summary>Offline/test fallback. The live engine never uses this
        /// constant for DSP; see <see cref="DeviceSampleRate"/>.</summary>
        public const int SampleRate = 44100;
        private const float TickIntervalSeconds = 0.05f; // 50 ms per logical tick
        private static int deviceSampleRate;
        private static bool deviceSampleRateResolved;

        /// <summary>
        /// F234: the game runs at 48000 Hz on the live machine, while the DSP
        /// constants were written against 44100. Sampling at the wrong rate
        /// shifts every oscillator/filter by 48000/44100 (+146.7 cents).
        /// Resolve once on the main thread; an invalid/unavailable device value
        /// falls back to the offline constant so unit tests remain deterministic.
        /// </summary>
        public static int DeviceSampleRate
        {
            get
            {
                if (!deviceSampleRateResolved)
                {
                    int rate = 0;
                    try
                    {
                        rate = AudioSettings.outputSampleRate;
                    }
                    catch (Exception)
                    {
                        rate = 0;
                    }
                    deviceSampleRate = rate > 0 ? rate : SampleRate;
                    deviceSampleRateResolved = true;
                }
                return deviceSampleRate;
            }
        }

        private readonly Atmosphere atmosphere;
        private readonly ReceiverDsp dsp;
        private readonly int sampleRate;
        // F94: double accumulator — float drift over hours of gameplay could
        // shift the 50 ms tick tempo.
        private double tickAccumulator;
        // Main-thread writes (volume knob), audio-thread reads (ProcessAudio
        // multiplies every sample) — volatile.
        private volatile float outputVolume = 1f;

        public float OutputVolume
        {
            get => outputVolume;
            set => outputVolume = Mathf.Clamp(value, 0f, 1f);
        }

        // F115: clear accumulated DSP state (QSB phase, AGC gain, filter
        // state) when the controller is destroyed; a recreated engine must
        // start from a clean state.
        // Accepted race (audited 2026-09-04): Reset runs on the main thread
        // while the audio thread may be inside Atmosphere.Apply. rng itself
        // is only touched from Apply (audio thread, single caller — grep
        // verified), so the generator is race-free; the worst interleaving
        // is one Gaussian sample recomputed after hasCachedGaussian flips —
        // inaudible. Locking the audio path would cost more than the bug.
        public void Reset()
        {
            atmosphere.Reset();
            dsp.Reset();
            tickAccumulator = 0.0;
        }

        // Main-thread write (ApplyFeedback), audio-thread read (UpdateCarrierPitch
        // runs main-thread, but QualityFactor feeds gains read by ProcessAudio) — volatile.
        private volatile float qualityFactor = Constants.QualityFactorBase;
        private float feedback;

        public float QualityFactor
        {
            get => qualityFactor;
            private set => qualityFactor = value;
        }
        public float Feedback { get => feedback; private set => feedback = value; }
        public Atmosphere Atmosphere => atmosphere;
        public ReceiverDsp Dsp => dsp;

        public AudioSynthesisEngine()
            : this(Environment.TickCount)
        {
        }

        /// <summary>Test seam: the atmosphere noise is seeded with the wall
        /// clock in production (Environment.TickCount), which made the offline
        /// render goldens flaky — a fixed seed makes the burst deterministic.</summary>
        public AudioSynthesisEngine(int atmosphereSeed)
        {
            atmosphere = new Atmosphere(atmosphereSeed);
            sampleRate = DeviceSampleRate;
            dsp = new ReceiverDsp(sampleRate);
        }

        public void ApplyFeedback(float angle)
        {
            Feedback = SpinnerScale.ToUnit(angle);

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
        public void UpdateStations(float deltaSeconds, float currentTime, StationState[] stations, StationScheduler[] schedulers, MorseKeyer[] keyers, CarrierGenerator[] carriers, bool radioOn)
        {
            if (!radioOn)
                return;

            tickAccumulator += deltaSeconds;
            int fleet = SimulationFleetCount(stations, schedulers, keyers, carriers);
            while (tickAccumulator >= TickIntervalSeconds)
            {
                tickAccumulator -= TickIntervalSeconds;

                for (int s = 0; s < fleet; s++)
                {
                    if (stations[s].Gain.IsNegligible(0.001f))
                        continue;

                    var sched = schedulers[s];
                    var phaseBefore = sched.CurrentPhase;

                    if (!keyers[s].IsActive)
                    {
                        bool phaseChanged = false;

                        var consolPattern = sched.GetCurrentConsolPattern();
                        if (consolPattern == null)
                        {
                            phaseChanged = AdvanceIdleSequence(sched, keyers[s], stations[s].Config, phaseChanged);
                        }
                        else if (consolPattern.Length.IsPositive())
                        {
                            keyers[s].LoadConsol(consolPattern, sched, stations[s].Config);
                        }
                        else
                        {
                            phaseChanged = AdvanceIdleSequence(sched, keyers[s], stations[s].Config, phaseChanged);
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

                    UpdateCarrierPitch(carriers[s], stations[s], keyers[s]);

                    if (phaseBefore == StationScheduler.Phase.PreNavPause &&
                        sched.CurrentPhase == StationScheduler.Phase.Navigation)
                    {
                        stations[s].NavigationStartTime = currentTime;
                    }
                }
            }
        }

        // Pitch-split: dot/dash tones sit above/below the station beat so the
        // elements are distinguishable by pitch, not only duration; equisignal
        // stays on the base beat. Main-thread only, and element boundaries are
        // tick-aligned so the switch never lands mid-tone. Stations without a
        // BeatFrequency keep whatever carrier frequency was set manually.
        private static void UpdateCarrierPitch(CarrierGenerator carrier, StationState station, MorseKeyer keyer)
        {
            float beat = station.BeatFrequency;
            if (beat.IsNegligible(0.001f))
                return;
            if (!keyer.KeyOn || keyer.IsTone)
                carrier.Frequency = beat;
            else if (keyer.CurrentElementIsDash)
                carrier.Frequency = beat - Constants.DashPitchOffsetHz;
            else
                carrier.Frequency = beat + Constants.DotPitchOffsetHz;
        }

        // Audio thread: read-only. Only volatile MorseKeyer flags are sampled;
        // no scheduler/keyer mutations, no UnityEngine API. Entry guards: any
        // throw here kills the whole audio graph, so null/ragged inputs bail
        // out silently instead (one Math.Min outside the loop, zero allocs).
        // Usable fleet length: null arrays count as empty, ragged arrays
        // (partial teardown) clamp to the shortest so the loop can never
        // index out of range.
        private static int SilencedFleetCount(StationState[] stations, MorseKeyer[] keyers, CarrierGenerator[] carriers)
        {
            if (stations.IsNull() || keyers.IsNull() || carriers.IsNull())
                return 0;
            return Math.Min(stations.Length, Math.Min(keyers.Length, carriers.Length));
        }

        // Same clamp for the main-thread simulation (schedulers included).
        private static int SimulationFleetCount(StationState[] stations, StationScheduler[] schedulers, MorseKeyer[] keyers, CarrierGenerator[] carriers)
        {
            if (stations.IsNull() || schedulers.IsNull() || keyers.IsNull() || carriers.IsNull())
                return 0;
            return Math.Min(stations.Length, Math.Min(schedulers.Length, Math.Min(keyers.Length, carriers.Length)));
        }

        /// <summary>
        /// Idle-keyer sequence handling: a pending sequence is loaded, a
        /// zero-length sequence finishes the phase. Extracted so the caller
        /// keeps explicit null guards (PVS V3080) without a conditional-access
        /// diagnostic (RCS1146).
        /// </summary>
        private bool AdvanceIdleSequence(StationScheduler sched, MorseKeyer keyer,
            ConsolCycleConfig config, bool phaseChanged)
        {
            var seq = sched.GetCurrentSequence();
            if (seq == null)
                return phaseChanged;
            if (seq.ElementCount.IsZero())
            {
                sched.OnSequenceFinished();
                return true;
            }
            keyer.Load(seq, sched, config);
            return phaseChanged;
        }

        public void ProcessAudio(float[] data, int channels, StationState[] stations, MorseKeyer[] keyers, CarrierGenerator[] carriers, bool radioOn, int currentZoneIndex, int sonneZoneIndex)
        {
            if (data.IsNull() || data.Length.IsZero() || channels.IsNonPositive())
                return;

            // An empty fleet means teardown — silence, not a DSP-processed
            // noise floor on a zero sample
            // (verified by ProcessAudio_RaggedFleet_ClampsToShortest).
            int count = SilencedFleetCount(stations, keyers, carriers);
            if (count.IsZero() || currentZoneIndex != sonneZoneIndex || !radioOn)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            // A ragged tail (Length % channels != 0) has no full frame —
            // stop before it instead of indexing out of range on the audio
            // thread, where a throw kills the whole graph.
            int frames = data.Length - data.Length % channels;
            for (int i = 0; i < frames; i += channels)
            {
                float sample = 0f;
                for (int s = 0; s < count; s++)
                {
                    if (stations[s].Gain.IsNegligible(0.001f))
                        continue;
                    if (keyers[s].KeyOn)
                        sample += stations[s].Gain
                            * (keyers[s].CurrentElementIsDash ? Constants.DashGainBoost : 1f)
                            * carriers[s].NextSample();
                    else
                        carriers[s].NextSample();
                }

                sample = atmosphere.Apply(sample, sampleRate);
                sample = dsp.Process(sample, sampleRate);

                for (int c = 0; c < channels; c++)
                    data[i + c] = sample * outputVolume;
            }
        }
    }
}
