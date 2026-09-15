using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class CarrierGenerator
    {
        private const float MaxFrequencyHz = 20000f;
        private const float SmoothingFactor = 0.1f;

        private double phase;
        private volatile float targetFrequency;
        private float smoothedFrequency;
        private float sampleRate;

        public float Frequency
        {
            get => targetFrequency;
            // F40: unbounded carrier frequencies alias above Nyquist; keep
            // the carrier below a sane audio ceiling (well above the
            // 300-1800 Hz beat range the controller actually produces).
            set => targetFrequency = Math.Max(0f, Math.Min(value, MaxFrequencyHz));
        }

        public CarrierGenerator(float sampleRate)
        {
            this.sampleRate = sampleRate;
            phase = 0.0;
        }

        // F141: one-pole smoothing prevents a phase discontinuity (click) when
        // the frequency is retuned between main-thread writes and audio reads.
        // F169: phase stays normalized to [0,1) so the accumulator never
        // accumulates float precision loss at large sample counts.
        public float NextSample()
        {
            smoothedFrequency += (targetFrequency - smoothedFrequency) * SmoothingFactor;

            if (smoothedFrequency.IsNonPositive())
                return 0f;

            float sample = (float)Math.Sin(2.0 * Math.PI * phase);
            phase += smoothedFrequency / sampleRate;

            if (phase >= 1.0)
                phase -= 1.0;

            return sample;
        }

        public void Reset()
        {
            phase = 0.0;
        }
    }

    public class Atmosphere
    {
        // F156: the rare "loud" crackle path used to be hardcoded magic
        // numbers — name them so both impulse paths are visible and tunable.
        private const float LoudCrackleProbability = 0.0001f;
        private const float LoudCrackleAmplitude = 0.15f;

        private readonly System.Random rng;
        private float qsbPhase;
        private float cachedGaussian;
        private bool hasCachedGaussian;

        // Tuning written on the main thread (AudioSynthesisEngine.ApplyFeedback)
        // and read on the audio thread (Apply). volatile closes the visibility
        // gap; a non-volatile field may be hoisted out of the sample loop.
        private volatile float noiseFloor = 0.002f;
        private volatile float impulseProbability = 0.001f;

        public float NoiseFloor
        {
            get { return noiseFloor; }
            set { noiseFloor = value; }
        }

        public float ImpulseProbability
        {
            get { return impulseProbability; }
            set { impulseProbability = value; }
        }

        public float ImpulseAmplitude { get; set; } = 0.3f;
        public float QsbDepth { get; set; } = 0.75f;
        public float QsbFrequency { get; set; } = 0.15f;

        public Atmosphere(int seed)
        {
            rng = new System.Random(seed);
            qsbPhase = 0f;
        }

        // F115: clear accumulated modulation state so a recreated controller
        // does not inherit the previous QSB phase.
        public void Reset()
        {
            qsbPhase = 0f;
            hasCachedGaussian = false;
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

            if (rng.NextDouble() < LoudCrackleProbability)
                noise += LoudCrackleAmplitude * ((float)rng.NextDouble() * 2f - 1f);

            return Mathf.Clamp(sample * qsb + noise, -1f, 1f);
        }

        // F153: Box-Muller yields two independent normals per two uniforms;
        // cache the cosine half instead of discarding 50% of the RNG output.
        private float GaussianNoise()
        {
            if (hasCachedGaussian)
            {
                hasCachedGaussian = false;
                return cachedGaussian;
            }

            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double sqrtTerm = Math.Sqrt(-2.0 * Math.Log(u1));
            cachedGaussian = (float)(sqrtTerm * Math.Cos(2.0 * Math.PI * u2));
            hasCachedGaussian = true;
            return (float)(sqrtTerm * Math.Sin(2.0 * Math.PI * u2));
        }
    }

    public class ReceiverDsp
    {
        private const double WhistleFrequencyHz = 600.0;
        private const float WhistleMix = 0.12f;

        private readonly float sampleRate;
        private float agcGain;
        private float prevFilter;
        private double whistlePhase;
        private float filterCutoff = 800f;
        private float cachedAlpha;

        public float AgcAttackRate { get; set; } = 0.001f;
        public float AgcReleaseRate { get; set; } = 0.0001f;
        // F155: alpha is constant while FilterCutoff is unchanged; recompute
        // it in the setter instead of 44100 times per second in Process.
        public float FilterCutoff
        {
            get => filterCutoff;
            set
            {
                filterCutoff = value;
                cachedAlpha = ComputeAlpha(value);
            }
        }
        public float SaturationDrive { get; set; } = 1.5f;

        // Written on the main thread (ApplyFeedback), read in Process on the
        // audio thread — volatile for the same visibility reason as NoiseFloor.
        private volatile float whistleLevel;
        public float WhistleLevel
        {
            get { return whistleLevel; }
            set { whistleLevel = value; }
        }

        public ReceiverDsp(float sampleRate)
        {
            // F154: the constructor sampleRate was accepted but discarded,
            // misleading callers; it now configures the filter.
            this.sampleRate = sampleRate;
            agcGain = 1f;
            prevFilter = 0f;
            whistlePhase = 0.0;
            cachedAlpha = ComputeAlpha(filterCutoff);
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

            prevFilter += cachedAlpha * (agcSample - prevFilter);

            float saturated = (float)Math.Tanh(SaturationDrive * prevFilter);

            if (WhistleLevel.IsSignificant())
            {
                float whistle = (float)Math.Sin(2.0 * Math.PI * WhistleFrequencyHz * whistlePhase / sampleRate);
                whistlePhase += 1.0;
                if (whistlePhase >= sampleRate) whistlePhase -= sampleRate;
                saturated += whistle * WhistleLevel * WhistleMix;
            }

            return Mathf.Clamp(saturated, -1f, 1f);
        }

        public void Reset()
        {
            agcGain = 1f;
            prevFilter = 0f;
            whistlePhase = 0.0;
        }

        private float ComputeAlpha(float cutoff)
        {
            return cutoff / (cutoff + sampleRate / (2f * (float)Math.PI));
        }
    }
}
