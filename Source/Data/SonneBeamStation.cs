using System;
using System.Collections.Generic;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One Sonne station's dot/dash table keyed by beam angle, with the
    /// circular-nearest signal lookup. Extracted from SonneBeam.cs (SRP:
    /// domain model is not a parser and not a registry).
    /// </summary>
    public sealed class SonneBeamStation
    {
        public string Name;
        public readonly Dictionary<double, int> Dashes = new Dictionary<double, int>();
        public readonly Dictionary<double, int> Dots = new Dictionary<double, int>();

        public static SonneBeamStation Parse(string json)
        {
            Dictionary<string, object> root = MiniJson.Parse(json);
            object stationValue;
            object dashesValue;
            object dotsValue;

            if (!root.TryGetValue("station", out stationValue)
                || !(stationValue is string)
                || string.IsNullOrEmpty((string)stationValue))
            {
                throw new FormatException("missing 'station'");
            }

            if (!root.TryGetValue("dashes", out dashesValue)
                || !(dashesValue is Dictionary<string, object>))
            {
                throw new FormatException("missing 'dashes'");
            }

            if (!root.TryGetValue("dots", out dotsValue)
                || !(dotsValue is Dictionary<string, object>))
            {
                throw new FormatException("missing 'dots'");
            }

            var station = new SonneBeamStation
            {
                Name = (string)stationValue
            };

            LoadEntries(station.Dashes, (Dictionary<string, object>)dashesValue);
            LoadEntries(station.Dots, (Dictionary<string, object>)dotsValue);
            return station;
        }

        public bool TryGetSignal(double angle, out SonneBeamSignal signal)
        {
            signal = default(SonneBeamSignal);
            if (Dashes.Count == 0 || Dots.Count == 0 || double.IsNaN(angle) || double.IsInfinity(angle))
                return false;

            double normalizedAngle = AstroMath.NormalizeDegrees(angle);
            double dashDistance;
            double dotDistance;
            double dashAngle;
            double dotAngle;
            int dashCount;
            int dotCount;

            if (!FindClosest(Dashes, normalizedAngle, out dashAngle, out dashCount, out dashDistance)
                || !FindClosest(Dots, normalizedAngle, out dotAngle, out dotCount, out dotDistance))
            {
                return false;
            }

            // Preserve SonneBeam.cs tie behaviour: an equal-distance tie uses dashes.
            bool matchedDots = dotDistance < dashDistance;
            int matchedCount = matchedDots ? dotCount : dashCount;
            if (matchedCount > SonneBeam.TotalElements)
                return false;

            int otherCount = SonneBeam.TotalElements - matchedCount;
            int dotsCount = matchedDots ? matchedCount : otherCount;
            int dashesCount = matchedDots ? otherCount : matchedCount;

            int dotLoss = Math.Min(2, dotsCount);
            int dashLoss = Math.Min(2, dashesCount);
            int equisignalCount = dotLoss + dashLoss;
            int dotsOutput = dotsCount - dotLoss;
            int dashesOutput = dashesCount - dashLoss;

            signal = new SonneBeamSignal
            {
                DotCount = dotsCount,
                DashCount = dashesCount,
                IsDashSector = !matchedDots,
                EquisignalCount = equisignalCount,
                LeadingCount = matchedDots ? dotsOutput : dashesOutput,
                TrailingCount = matchedDots ? dashesOutput : dotsOutput,
                MatchedAngle = matchedDots ? dotAngle : dashAngle
            };
            return true;
        }

        private static void LoadEntries(Dictionary<double, int> target, Dictionary<string, object> source)
        {
            foreach (KeyValuePair<string, object> entry in source)
            {
                double angle;
                if (!double.TryParse(entry.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out angle))
                    throw new FormatException("invalid angle '" + entry.Key + "'");

                int count = Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture);
                if (count < 0 || count > SonneBeam.TotalElements)
                    throw new FormatException("signal count out of range at '" + entry.Key + "'");

                target[AstroMath.NormalizeDegrees(angle)] = count;
            }
        }

        private static bool FindClosest(
            Dictionary<double, int> entries,
            double angle,
            out double matchedAngle,
            out int matchedCount,
            out double distance)
        {
            matchedAngle = 0.0;
            matchedCount = -1;
            distance = double.MaxValue;

            foreach (KeyValuePair<double, int> entry in entries)
            {
                double currentDistance = AngularDistance(entry.Key, angle);
                if (currentDistance < distance)
                {
                    matchedAngle = entry.Key;
                    matchedCount = entry.Value;
                    distance = currentDistance;
                }
            }

            return matchedCount >= 0;
        }

        private static double AngularDistance(double first, double second)
        {
            double distance = Math.Abs(first - second) % 360.0;
            return distance > 180.0 ? 360.0 - distance : distance;
        }
    }

    public struct SonneBeamSignal
    {
        public int DotCount;
        public int DashCount;
        public bool IsDashSector;
        public int EquisignalCount;
        public int LeadingCount;
        public int TrailingCount;
        public double MatchedAngle;
    }
}
