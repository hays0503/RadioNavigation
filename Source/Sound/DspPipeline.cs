using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class CarrierGenerator
    {
        private double phase;
        private volatile float targetFrequency;
        private float smoothedFrequency;
        private float sampleRate;

        public float Frequency
        {
            get => targetFrequency;
            set => targetFrequency = Math.Max(0f, value);
        }

        public CarrierGenerator(float sampleRate)
        {
            this.sampleRate = sampleRate;
            phase = 0.0;
        }

        // F141: one-pole smoothing prevents a phase discontinuity (click) when
        // the frequency is retuned between main-thread writes and audio reads.
        public float NextSample()
        {
            smoothedFrequency += (targetFrequency - smoothedFrequency) * 0.1f;

            if (smoothedFrequency.IsNonPositive())
                return 0f;

            float sample = (float)Math.Sin(2.0 * Math.PI * smoothedFrequency * phase / sampleRate);
            phase += 1.0;

            if (phase >= sampleRate)
                phase -= sampleRate;

            return sample;
        }

        public void Reset()
        {
            phase = 0.0;
        }
    }

    public class Atmosphere
    {
        private readonly System.Random rng;
        private float qsbPhase;

        public float NoiseFloor { get; set; } = 0.002f;
        public float ImpulseProbability { get; set; } = 0.001f;
        public float ImpulseAmplitude { get; set; } = 0.3f;
        public float QsbDepth { get; set; } = 0.75f;
        public float QsbFrequency { get; set; } = 0.15f;

        public Atmosphere(int seed)
        {
            rng = new System.Random(seed);
            qsbPhase = 0f;
        }

        public float Apply(float sample, float sampleRate)
        {
            float qsb = 1f - QsbDepth * (0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * QsbFrequency * qsbPhase / sampleRate));
            qsbPhase += 1f;
            if (qsbPhase >= sampleRate)
                qsbPhase -= sampleRate;

            float noise = GaussianNoise() * NoiseFloor;

            if (rng.NextDouble() < ImpulseProbability)
                noise += ImpulseAmplitude * ((float)rng.NextDouble() * 2f - 1f);

            if (rng.NextDouble() < 0.0001)
                noise += 0.15f * ((float)rng.NextDouble() * 2f - 1f);

            return Mathf.Clamp(sample * qsb + noise, -1f, 1f);
        }

        private float GaussianNoise()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
    }

    public class ReceiverDsp
    {
        private float agcGain;
        private float prevFilter;
        private double whistlePhase;

        public float AgcAttackRate { get; set; } = 0.001f;
        public float AgcReleaseRate { get; set; } = 0.0001f;
        public float FilterCutoff { get; set; } = 800f;
        public float SaturationDrive { get; set; } = 1.5f;
        public float WhistleLevel { get; set; } = 0f;

        public ReceiverDsp(float sampleRate)
        {
            agcGain = 1f;
            prevFilter = 0f;
            whistlePhase = 0.0;
        }

        public float Process(float sample, float sampleRate)
        {
            float envelope = Math.Abs(sample);

            if (envelope > agcGain)
                agcGain += AgcAttackRate * (envelope - agcGain);
            else
                agcGain += AgcReleaseRate * (envelope - agcGain);

            if (agcGain.IsNegligible(0.01f))
                agcGain = 0.01f;

            float agcSample = sample / agcGain;

            float alpha = FilterCutoff / (FilterCutoff + sampleRate / (2f * (float)Math.PI));
            prevFilter += alpha * (agcSample - prevFilter);

            float saturated = (float)Math.Tanh(SaturationDrive * prevFilter);

            if (WhistleLevel.IsSignificant())
            {
                float whistle = (float)Math.Sin(2.0 * Math.PI * 600.0 * whistlePhase / sampleRate);
                whistlePhase += 1.0;
                if (whistlePhase >= sampleRate) whistlePhase -= sampleRate;
                saturated += whistle * WhistleLevel * 0.12f;
            }

            return Mathf.Clamp(saturated, -1f, 1f);
        }

        public void Reset()
        {
            agcGain = 1f;
            prevFilter = 0f;
            whistlePhase = 0.0;
        }
    }
}
