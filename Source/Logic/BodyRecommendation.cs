using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Picks the best second sight for a two-body fix (SEXTANT_PLAN v2.2):
    /// from the bodies currently above the horizon, the one whose azimuth is
    /// closest to 90° from the first sight — the crossing geometry that
    /// minimizes 1/sin(θ) intercept amplification. Pure C#, unit-tested
    /// offline in BodyRecommendationTests.
    /// </summary>
    public static class BodyRecommendation
    {
        /// <summary>Sights below this altitude are unreliable (refraction and
        /// horizon dip are not modelled by the game).</summary>
        public const double MinAltitudeDeg = 10.0;

        public const double IdealSeparationDeg = 90.0;

        /// <summary>Below this minimum separation a third sight adds no
        /// geometry: the crossing to one of the recorded sights is too
        /// shallow to trust.</summary>
        public const double MinThirdSeparationDeg = 15.0;

        public struct Candidate
        {
            public string Name;
            public double AltDeg;
            public double AzDeg;
            /// <summary>Catalog HIP (0 for the Sun/Moon and no-HIP rows), so a
            /// recommendation can exclude an already-recorded body.</summary>
            public int Hip;
        }

        public struct Result
        {
            public bool Found;
            public string Name;
            public double SeparationDeg;
        }

        /// <summary>Best second body for the given first-sight azimuth.
        /// Found=false when no candidate is high enough. <paramref name="excludeName"/>
        /// drops the first sight's own body (it would score ΔAz 0°).</summary>
        public static Result Recommend(double firstAzDeg, IList<Candidate> candidates,
            string excludeName = null)
        {
            Result best = new Result();
            best.Found = false;
            best.Name = null;
            best.SeparationDeg = 0.0;
            if (candidates == null)
                return best;

            double bestScore = double.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (excludeName != null && c.Name == excludeName)
                    continue;
                if (c.AltDeg < MinAltitudeDeg)
                    continue;
                double sep = SeparationDeg(firstAzDeg, c.AzDeg);
                double score = Math.Abs(sep - IdealSeparationDeg);
                if (score < bestScore)
                {
                    bestScore = score;
                    best.Found = true;
                    best.Name = c.Name;
                    best.SeparationDeg = sep;
                }
            }
            return best;
        }

        /// <summary>Best third body for a three-sight series: maximizes the
        /// SMALLEST separation to the two recorded azimuths, so the new line
        /// crosses both existing lines well. Recorded bodies are skipped by
        /// their HIP (a non-zero exclude; 0 never matches). Found=false when
        /// the best minimum stays below MinThirdSeparationDeg.</summary>
        public static Result RecommendThird(double firstAzDeg, double secondAzDeg,
            IList<Candidate> candidates, int excludeHipA, int excludeHipB)
        {
            Result best = new Result();
            best.Found = false;
            best.Name = null;
            best.SeparationDeg = 0.0;
            if (candidates == null)
                return best;

            double bestMin = -1.0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (c.Hip != 0 && (c.Hip == excludeHipA || c.Hip == excludeHipB))
                    continue;
                double min = Math.Min(
                    SeparationDeg(firstAzDeg, c.AzDeg),
                    SeparationDeg(secondAzDeg, c.AzDeg));
                if (min > bestMin)
                {
                    bestMin = min;
                    best.Name = c.Name;
                    best.SeparationDeg = min;
                }
            }
            if (bestMin < MinThirdSeparationDeg)
            {
                best.Name = null;
                best.SeparationDeg = 0.0;
                return best;
            }
            best.Found = true;
            return best;
        }

        /// <summary>Undirected 0..90° angle between two azimuths — the same
        /// crossing-angle convention as the fix geometry warning.</summary>
        public static double SeparationDeg(double az1Deg, double az2Deg)
        {
            double d = Math.Abs(az1Deg - az2Deg) % 360.0;
            if (d > 180.0)
                d = 360.0 - d;
            if (d > 90.0)
                d = 180.0 - d;
            return d;
        }
    }
}
