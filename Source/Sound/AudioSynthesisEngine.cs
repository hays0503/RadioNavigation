using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class AudioSynthesisEngine
    {
        public const int SampleRate = 44100;
        private const float BfoBase = 1000f;
        private const float BfoMin = 300f;
        private const float BfoMax = 1800f;

        private readonly Atmosphere _atmosphere;
        private readonly ReceiverDsp _dsp;
        private readonly float _ticksPerSample;
        private float _tickAccumulator;
        private float _outputVolume = 1f;

        public float OutputVolume
        {
            get => _outputVolume;
            set => _outputVolume = Mathf.Clamp(value, 0f, 1f);
        }

        public float QualityFactor { get; set; } = 20f;
        public float Feedback { get; private set; }
        public Atmosphere Atmosphere => _atmosphere;
        public ReceiverDsp Dsp => _dsp;

        public AudioSynthesisEngine()
        {
            _atmosphere = new Atmosphere(Environment.TickCount);
            _dsp = new ReceiverDsp(SampleRate);

            float samplesPerMs = SampleRate / 1000f;
            float msPerTick = 50f;
            _ticksPerSample = 1f / (samplesPerMs * msPerTick);
        }

        public void ApplyFeedback(float angle)
        {
            Feedback = Mathf.Clamp01(angle / 140f);

            QualityFactor = 20f + Feedback * Feedback * 180f;

            _atmosphere.NoiseFloor = Mathf.Lerp(0.004f, 0.0005f, Feedback);
            _atmosphere.ImpulseProbability = Mathf.Lerp(0.002f, 0.0002f, Feedback);

            _dsp.WhistleLevel = Mathf.Clamp01((Feedback - 0.75f) / 0.2f);
        }

        public void UpdateStationBeats(StationState[] stations, CarrierGenerator[] carriers, float currentFrequencyKHz, int currentZoneIndex, int sonneZoneIndex)
        {
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i].Gain < 0.001f)
                    continue;

                float offset = currentFrequencyKHz - (stations[i].BeatFrequency - BfoBase) / 1000f;
                stations[i].BeatFrequency = Mathf.Clamp(stations[i].BeatFrequency, BfoMin, BfoMax);
                carriers[i].Frequency = stations[i].BeatFrequency;
            }
        }

        public void ProcessAudio(float[] data, int channels, StationState[] stations, StationScheduler[] schedulers, MorseKeyer[] keyers, CarrierGenerator[] carriers, bool radioOn, int currentZoneIndex, int sonneZoneIndex)
        {
            if (currentZoneIndex != sonneZoneIndex)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            for (int i = 0; i < data.Length; i += channels)
            {
                _tickAccumulator += _ticksPerSample;
                while (_tickAccumulator >= 1f)
                {
                    for (int s = 0; s < stations.Length; s++)
                    {
                        if (stations[s].Gain < 0.001f)
                            continue;

                        if (!keyers[s].IsActive)
                        {
                            var sched = schedulers[s];

                            if (sched.CurrentPhase == StationScheduler.Phase.Pause ||
                                sched.CurrentPhase == StationScheduler.Phase.PreNavPause ||
                                sched.CurrentPhase == StationScheduler.Phase.PostNavSilence)
                            {
                                var prevPhase = sched.CurrentPhase;
                                sched.Tick();
                                if (prevPhase == StationScheduler.Phase.PreNavPause &&
                                    sched.CurrentPhase == StationScheduler.Phase.Navigation)
                                {
                                    stations[s].NavigationStartTime = Time.time;
                                    Debug.Log("[RadioNav][NAV] PreNavPause->Navigation st=" + s +
                                        " NavStartTime=" + Time.time.ToString("F2"));
                                }
                            }

                            var consolPattern = sched.GetCurrentConsolPattern();
                            if (consolPattern != null && consolPattern.Length > 0)
                            {
                                keyers[s].LoadConsol(consolPattern, sched, stations[s].Config, stations[s].CallsignLength);
                            }
                            else
                            {
                                var seq = sched.GetCurrentSequence();
                                if (seq == null || seq.ElementCount == 0)
                                {
                                    if (seq != null)
                                        sched.OnSequenceFinished();
                                    sched.Tick();
                                }
                                else
                                {
                                    keyers[s].Load(seq, sched, stations[s].Config);
                                }
                            }
                        }

                        keyers[s].Advance();
                    }
                    _tickAccumulator -= 1f;
                }

                if (!radioOn)
                    continue;

                float sample = 0f;
                for (int s = 0; s < stations.Length; s++)
                {
                    if (stations[s].Gain < 0.001f)
                        continue;
                    if (keyers[s].KeyOn)
                        sample += stations[s].Gain * carriers[s].NextSample();
                    else
                        carriers[s].NextSample();
                }

                sample = _atmosphere.Apply(sample, SampleRate);
                sample = _dsp.Process(sample, SampleRate);

                for (int c = 0; c < channels; c++)
                    data[i + c] = sample * _outputVolume;
            }
        }
    }
}
