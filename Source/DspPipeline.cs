using System;

namespace UBOAT.Mods.TestUI
{
    public class CarrierGenerator
    {
        private double _phase;
        private float _frequency;
        private float _sampleRate;

        public float Frequency
        {
            get => _frequency;
            set => _frequency = Math.Max(0f, value);
        }

        public CarrierGenerator(float sampleRate)
        {
            _sampleRate = sampleRate;
            _phase = 0.0;
        }

        public float NextSample()
        {
            if (_frequency <= 0f)
                return 0f;

            float sample = (float)Math.Sin(2.0 * Math.PI * _frequency * _phase / _sampleRate);
            _phase += 1.0;

            if (_phase >= _sampleRate)
                _phase -= _sampleRate;

            return sample;
        }

        public void Reset()
        {
            _phase = 0.0;
        }
    }

    public class Atmosphere
    {
        private readonly Random _rng;
        private float _qsbPhase;

        public float NoiseFloor { get; set; } = 0.002f;
        public float ImpulseProbability { get; set; } = 0.001f;
        public float ImpulseAmplitude { get; set; } = 0.3f;
        public float QsbDepth { get; set; } = 0.75f;
        public float QsbFrequency { get; set; } = 0.15f;

        public Atmosphere(int seed)
        {
            _rng = new Random(seed);
            _qsbPhase = 0f;
        }

        public float Apply(float sample, float sampleRate)
        {
            float qsb = 1f - QsbDepth * (0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * QsbFrequency * _qsbPhase / sampleRate));
            _qsbPhase += 1f;
            if (_qsbPhase >= sampleRate)
                _qsbPhase -= sampleRate;

            float noise = GaussianNoise() * NoiseFloor;

            if (_rng.NextDouble() < ImpulseProbability)
                noise += ImpulseAmplitude * ((float)_rng.NextDouble() * 2f - 1f);

            if (_rng.NextDouble() < 0.0001)
                noise += 0.15f * ((float)_rng.NextDouble() * 2f - 1f);

            return sample * qsb + noise;
        }

        private float GaussianNoise()
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
    }

    public class ReceiverDsp
    {
        private float _agcGain;
        private float _prevFilter;
        private double _whistlePhase;

        public float AgcAttackRate { get; set; } = 0.001f;
        public float AgcReleaseRate { get; set; } = 0.0001f;
        public float FilterCutoff { get; set; } = 800f;
        public float SaturationDrive { get; set; } = 1.5f;
        public float WhistleLevel { get; set; } = 0f;

        public ReceiverDsp(float sampleRate)
        {
            _agcGain = 1f;
            _prevFilter = 0f;
            _whistlePhase = 0.0;
        }

        public float Process(float sample, float sampleRate)
        {
            float envelope = Math.Abs(sample);

            if (envelope > _agcGain)
                _agcGain += AgcAttackRate * (envelope - _agcGain);
            else
                _agcGain += AgcReleaseRate * (envelope - _agcGain);

            if (_agcGain < 0.01f)
                _agcGain = 0.01f;

            float agcSample = sample / _agcGain;

            float alpha = FilterCutoff / (FilterCutoff + sampleRate / (2f * (float)Math.PI));
            _prevFilter += alpha * (agcSample - _prevFilter);

            float saturated = (float)Math.Tanh(SaturationDrive * _prevFilter);

            if (WhistleLevel > 0.001f)
            {
                float whistle = (float)Math.Sin(2.0 * Math.PI * 600.0 * _whistlePhase / sampleRate);
                _whistlePhase += 1.0;
                if (_whistlePhase >= sampleRate) _whistlePhase -= sampleRate;
                saturated += whistle * WhistleLevel * 0.12f;
            }

            return saturated;
        }

        public void Reset()
        {
            _agcGain = 1f;
            _prevFilter = 0f;
            _whistlePhase = 0.0;
        }
    }
}
