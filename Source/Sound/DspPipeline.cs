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
            set
            {
                if (float.IsNaN(value))
                    return;
                targetFrequency = Math.Max(0f, Math.Min(value, MaxFrequencyHz));
            }
        }

        public CarrierGenerator(float sampleRate)
        {
            this.sampleRate = IsValidSampleRate(sampleRate)
                ? sampleRate
                : AudioSynthesisEngine.SampleRate;
            phase = 0.0;
        }

        // F141: one-pole smoothing prevents a phase discontinuity (click) when
        // the frequency is retuned between main-thread writes and audio reads.
        // F169: phase stays normalized to [0,1) so the accumulator never
        // accumulates float precision loss at large sample counts.
        public float NextSample()
        {
            smoothedFrequency += (targetFrequency - smoothedFrequency) * SmoothingFactor;
            if (targetFrequency == 0f && smoothedFrequency < 1e-6f)
                smoothedFrequency = 0f;

            if (float.IsNaN(smoothedFrequency) || smoothedFrequency.IsNonPositive())
                return 0f;

            float sample = (float)Math.Sin(2.0 * Math.PI * phase);
            phase += smoothedFrequency / sampleRate;

            if (phase >= 1.0)
                phase -= Math.Floor(phase);

            return sample;
        }

        public void Reset()
        {
            phase = 0.0;
            smoothedFrequency = 0f;
            if (float.IsNaN(targetFrequency) || float.IsInfinity(targetFrequency))
                targetFrequency = 0f;
        }

        internal static bool IsValidSampleRate(float sampleRate)
        {
            return sampleRate > 0f && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
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
            if (float.IsNaN(sample) || float.IsInfinity(sample))
                sample = 0f;

            float qsb = 1f;
            if (CarrierGenerator.IsValidSampleRate(sampleRate))
            {
                qsb = 1f - QsbDepth * (0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * qsbPhase));
                if (QsbFrequency > 0f)
                {
                    qsbPhase += QsbFrequency / sampleRate;
                    if (qsbPhase >= 1f)
                        qsbPhase -= (float)Math.Floor(qsbPhase);
                }
            }

            float noise = GaussianNoise() * NoiseFloor;

            if (rng.NextDouble() < ImpulseProbability)
                noise += ImpulseAmplitude * ((float)rng.NextDouble() * 2f - 1f);

            if (rng.NextDouble() < LoudCrackleProbability)
                noise += LoudCrackleAmplitude * ((float)rng.NextDouble() * 2f - 1f);

            float mixed = sample * qsb + noise;
            if (float.IsNaN(mixed) || float.IsInfinity(mixed))
                return 0f;
            return Mathf.Clamp(mixed, -1f, 1f);
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

        private float sampleRate;
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
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return;
                filterCutoff = Mathf.Clamp(value, 0f, sampleRate * 0.45f);
                cachedAlpha = ComputeAlpha(filterCutoff);
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
            this.sampleRate = CarrierGenerator.IsValidSampleRate(sampleRate)
                ? sampleRate
                : AudioSynthesisEngine.SampleRate;
            agcGain = 1f;
            prevFilter = 0f;
            whistlePhase = 0.0;
            filterCutoff = Mathf.Clamp(filterCutoff, 0f, this.sampleRate * 0.45f);
            cachedAlpha = ComputeAlpha(filterCutoff);
        }

        private static bool IsNonFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }

        public float Process(float sample, float sampleRate)
        {
            if (IsNonFinite(sample))
                return 0f;

            if (CarrierGenerator.IsValidSampleRate(sampleRate) && Math.Abs(sampleRate - this.sampleRate) > 0.5f)
            {
                this.sampleRate = sampleRate;
                cachedAlpha = ComputeAlpha(filterCutoff);
            }

            float envelope = Math.Abs(sample);

            if (envelope > agcGain)
                agcGain += AgcAttackRate * (envelope - agcGain);
            else
                agcGain += AgcReleaseRate * (envelope - agcGain);

            if (float.IsNaN(agcGain) || agcGain.IsNegligible(0.01f))
                agcGain = 0.01f;

            float agcSample = sample / agcGain;

            prevFilter += cachedAlpha * (agcSample - prevFilter);
            if (IsNonFinite(prevFilter) || Math.Abs(prevFilter) < 1e-15f)
                prevFilter = 0f;

            float saturated = (float)Math.Tanh(SaturationDrive * prevFilter);

            if (WhistleLevel.IsSignificant() && CarrierGenerator.IsValidSampleRate(sampleRate))
            {
                float whistle = (float)Math.Sin(2.0 * Math.PI * WhistleFrequencyHz * whistlePhase / sampleRate);
                whistlePhase += 1.0;
                if (whistlePhase >= sampleRate) whistlePhase -= sampleRate;
                saturated += whistle * WhistleLevel * WhistleMix;
            }

            if (IsNonFinite(saturated))
            {
                prevFilter = 0f;
                return 0f;
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
            float clampedCutoff = Mathf.Clamp(cutoff, 0f, sampleRate * 0.45f);
            float alpha = clampedCutoff / (clampedCutoff + sampleRate / (2f * (float)Math.PI));
            return Mathf.Clamp01(alpha);
        }
    }
}
