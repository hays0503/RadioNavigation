using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Burst sampling of the sextant arc (CELESTIAL_REALISM_PLAN P6). A real
    /// sight is not a single drum reading: the lookout taps the arc a few
    /// times at ONE setting and the spread says how steady the sight was.
    /// Since 2026-09-22 (author's decision) the recorded Ho is the drum
    /// reading at the key press — the burst is a QUALITY METER: TryCommit
    /// still returns the mean of the inliers and the spread the card prints
    /// as "± σ (n=…)", but it no longer moves the recorded value. Taps taken
    /// at a different drum setting restart the burst (Tap), so the spread
    /// never mixes two settings. Pure, no Unity.
    /// </summary>
    public sealed class SextantBurstSampler
    {
        /// <summary>Maximum taps kept in one burst.</summary>
        public const int MaxSamples = 5;

        /// <summary>Spread above which the card flags the sight as rough (arcmin).</summary>
        public const double MaxSigmaArcMin = 1.0;

        /// <summary>Rejection floor: a sample is only dropped when it is
        /// beyond max(2σ, this) from the mean (arcmin).</summary>
        public const double RejectFloorArcMin = 0.5;

        /// <summary>Drum-move threshold (arcmin, one 1/600° quantum = 0.1′): a tap
        /// that differs from the previous one by at least this much starts a
        /// new burst — a burst is several readings at ONE setting.</summary>
        public const double RestartDeltaArcMin = 0.1;

        /// <summary>Tolerance on the restart test (arcmin): the arc is a
        /// double in the tens of degrees, so a one-quantum step can measure a
        /// hair under 0.1′. Real drum steps are exact quanta, so this only
        /// absorbs the floating-point noise.</summary>
        private const double RestartToleranceArcMin = 1e-6;

        private readonly List<double> samples = new List<double>(MaxSamples);

        public int Count
        {
            get { return samples.Count; }
        }

        public bool IsFull
        {
            get { return samples.Count >= MaxSamples; }
        }

        /// <summary>Adds one tap (degrees). Extra taps beyond MaxSamples are ignored.</summary>
        public void Add(double hoDeg)
        {
            if (samples.Count < MaxSamples)
                samples.Add(hoDeg);
        }

        /// <summary>
        /// Adds one tap, restarting the burst first when the drum moved since
        /// the previous tap (a mixed-setting burst would average readings
        /// taken at different settings and inflate σ). Returns true when the
        /// burst was restarted.
        /// </summary>
        public bool Tap(double hoDeg)
        {
            bool restarted = false;
            if (samples.Count > 0
                && Math.Abs(samples[samples.Count - 1] - hoDeg) * 60.0
                    >= RestartDeltaArcMin - RestartToleranceArcMin)
            {
                samples.Clear();
                restarted = true;
            }
            Add(hoDeg);
            return restarted;
        }

        public void Clear()
        {
            samples.Clear();
        }

        /// <summary>
        /// Commits the burst: mean of the inliers (one worst sample dropped
        /// when it is beyond max(2σ, 0.5′)), σ in arcmin and the rejection
        /// count. The caller uses the mean and σ as the quality metric — the
        /// recorded Ho comes from the drum, not from here. False when no
        /// sample was taken.
        /// </summary>
        public bool TryCommit(out double meanHoDeg, out double sigmaArcMin, out int rejected)
        {
            meanHoDeg = 0.0;
            sigmaArcMin = 0.0;
            rejected = 0;
            if (samples.Count == 0)
                return false;

            var kept = new List<double>(samples);
            if (kept.Count >= 3)
            {
                // Median/MAD, not mean/σ: with three samples a single wild tap
                // inflates σ so much that a 2σ test never rejects it.
                double median = Median(kept);
                double mad = MedianAbsoluteDeviation(kept, median);
                double limit = Math.Max(2.5 * mad, RejectFloorArcMin / 60.0);

                int worst = 0;
                double worstDeviation = -1.0;
                for (int i = 0; i < kept.Count; i++)
                {
                    double deviation = Math.Abs(kept[i] - median);
                    if (deviation > worstDeviation)
                    {
                        worstDeviation = deviation;
                        worst = i;
                    }
                }
                if (worstDeviation > limit)
                {
                    kept.RemoveAt(worst);
                    rejected = 1;
                }
            }

            meanHoDeg = Mean(kept);
            sigmaArcMin = Std(kept) * 60.0;
            return true;
        }

        /// <summary>True when the committed spread is too wide for a good sight.</summary>
        public static bool IsRough(double sigmaArcMin)
        {
            return sigmaArcMin > MaxSigmaArcMin;
        }

        private static double Mean(List<double> values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return sum / values.Count;
        }

        private static double Median(List<double> values)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            int middle = sorted.Count / 2;
            if ((sorted.Count & 1) == 1)
                return sorted[middle];
            return 0.5 * (sorted[middle - 1] + sorted[middle]);
        }

        private static double MedianAbsoluteDeviation(List<double> values, double median)
        {
            var deviations = new List<double>(values.Count);
            for (int i = 0; i < values.Count; i++)
                deviations.Add(Math.Abs(values[i] - median));
            return Median(deviations);
        }

        private static double Std(List<double> values)
        {
            if (values.Count < 2)
                return 0.0;
            double mean = Mean(values);
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                sum += d * d;
            }
            return Math.Sqrt(sum / (values.Count - 1));
        }
    }
}
