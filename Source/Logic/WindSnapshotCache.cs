using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    internal struct WindSample
    {
        public bool Available;
        public float EastMetersPerSecond;
        public float NorthMetersPerSecond;
    }

    // Point-sample cache for the scene wind. Reference implementation for the
    // shipping path: a warm cell never calls the weather evaluator, and a
    // missing anchor is "unknown", never still air.
    internal sealed class WindSnapshotCache
    {
        private struct Key : IEquatable<Key>
        {
            public int X;
            public int Y;
            public int T;

            public bool Equals(Key other)
            {
                return X == other.X && Y == other.Y && T == other.T;
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((X * 397) ^ Y) * 397 ^ T;
                }
            }
        }

        private struct Corner
        {
            public int LowEast;
            public int LowNorth;
            public int LowTime;
            public double WeightEast;
            public double WeightNorth;
            public double WeightTime;
        }

        private readonly Dictionary<Key, WindSample> anchors;
        private readonly int revision;
        private readonly double spacingEast;
        private readonly double spacingNorth;
        private readonly double spacingTime;

        private WindSnapshotCache(int revision, double spacingEast, double spacingNorth,
            double spacingTime, Dictionary<Key, WindSample> anchors)
        {
            this.revision = revision;
            this.spacingEast = spacingEast;
            this.spacingNorth = spacingNorth;
            this.spacingTime = spacingTime;
            this.anchors = anchors;
        }

        public int Revision => revision;
        public int AnchorCount => anchors.Count;

        public static WindSnapshotCache Build(WindSnapshotCache previous, int revision,
            double spacingEast, double spacingNorth, double spacingTime,
            IList<WindPoint> points, Func<double, double, double, WindSample> evaluate,
            out int evaluations)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (evaluate == null)
                throw new ArgumentNullException(nameof(evaluate));
            if (!IsSpacing(spacingEast) || !IsSpacing(spacingNorth) || !IsSpacing(spacingTime))
                throw new ArgumentOutOfRangeException(nameof(spacingEast));

            var built = new Dictionary<Key, WindSample>();
            evaluations = 0;
            WindSnapshotCache reusable = CanReuse(previous, revision, spacingEast, spacingNorth,
                spacingTime) ? previous : null;
            for (int i = 0; i < points.Count; i++)
                AddCell(built, points[i], reusable, spacingEast, spacingNorth, spacingTime,
                    evaluate, ref evaluations);
            return new WindSnapshotCache(revision, spacingEast, spacingNorth, spacingTime, built);
        }

        public WindSample Sample(double east, double north, double time, int expectedRevision)
        {
            Corner cell;
            if (expectedRevision != revision || !TryCell(east, north, time, out cell))
                return default(WindSample);

            double sumEast = 0;
            double sumNorth = 0;
            for (int t = 0; t < 2; t++)
            {
                double weightT = t == 0 ? 1 - cell.WeightTime : cell.WeightTime;
                for (int n = 0; n < 2; n++)
                {
                    double weightN = n == 0 ? 1 - cell.WeightNorth : cell.WeightNorth;
                    for (int e = 0; e < 2; e++)
                    {
                        WindSample value;
                        if (!TryCorner(cell, e, n, t, out value))
                            return default(WindSample);
                        double weight = weightT * weightN * (e == 0 ? 1 - cell.WeightEast : cell.WeightEast);
                        sumEast += weight * value.EastMetersPerSecond;
                        sumNorth += weight * value.NorthMetersPerSecond;
                    }
                }
            }
            return new WindSample
            {
                Available = true,
                EastMetersPerSecond = (float)sumEast,
                NorthMetersPerSecond = (float)sumNorth
            };
        }

        private bool TryCorner(Corner cell, int east, int north, int time, out WindSample value)
        {
            var key = new Key
            {
                X = cell.LowEast + east,
                Y = cell.LowNorth + north,
                T = cell.LowTime + time
            };
            return anchors.TryGetValue(key, out value) && value.Available;
        }

        private bool TryCell(double east, double north, double time, out Corner cell)
        {
            cell = default(Corner);
            if (!IsFinite(east) || !IsFinite(north) || !IsFinite(time))
                return false;
            double cellEast = east / spacingEast;
            double cellNorth = north / spacingNorth;
            double cellTime = time / spacingTime;
            if (!IsFinite(cellEast) || !IsFinite(cellNorth) || !IsFinite(cellTime))
                return false;
            cell.LowEast = Floor(cellEast);
            cell.LowNorth = Floor(cellNorth);
            cell.LowTime = Floor(cellTime);
            cell.WeightEast = cellEast - cell.LowEast;
            cell.WeightNorth = cellNorth - cell.LowNorth;
            cell.WeightTime = cellTime - cell.LowTime;
            return true;
        }

        private static bool CanReuse(WindSnapshotCache previous, int revision,
            double spacingEast, double spacingNorth, double spacingTime)
        {
            return previous != null && previous.revision == revision
                && previous.spacingEast == spacingEast
                && previous.spacingNorth == spacingNorth
                && previous.spacingTime == spacingTime;
        }

        private static void AddCell(Dictionary<Key, WindSample> built, WindPoint point,
            WindSnapshotCache reusable, double spacingEast, double spacingNorth, double spacingTime,
            Func<double, double, double, WindSample> evaluate, ref int evaluations)
        {
            if (!IsFinite(point.East) || !IsFinite(point.North) || !IsFinite(point.Time))
                return;
            int lowEast = Floor(point.East / spacingEast);
            int lowNorth = Floor(point.North / spacingNorth);
            int lowTime = Floor(point.Time / spacingTime);
            for (int t = 0; t < 2; t++)
                for (int n = 0; n < 2; n++)
                    for (int e = 0; e < 2; e++)
                        AddAnchor(built,
                            new Key { X = lowEast + e, Y = lowNorth + n, T = lowTime + t },
                            reusable, spacingEast, spacingNorth, spacingTime, evaluate,
                            ref evaluations);
        }

        private static void AddAnchor(Dictionary<Key, WindSample> built, Key key,
            WindSnapshotCache reusable, double spacingEast, double spacingNorth, double spacingTime,
            Func<double, double, double, WindSample> evaluate, ref int evaluations)
        {
            if (built.ContainsKey(key))
                return;
            WindSample value;
            if (reusable != null && reusable.anchors.TryGetValue(key, out value))
            {
                built.Add(key, value);
                return;
            }
            value = evaluate(key.X * spacingEast, key.Y * spacingNorth, key.T * spacingTime);
            evaluations++;
            built.Add(key, Normalize(value));
        }

        // The evaluator contract is "finite components", not "sets Available".
        private static WindSample Normalize(WindSample value)
        {
            if (!IsFinite(value.EastMetersPerSecond) || !IsFinite(value.NorthMetersPerSecond))
                return default(WindSample);
            value.Available = true;
            return value;
        }

        private static int Floor(double value)
        {
            return (int)Math.Floor(value);
        }

        private static bool IsSpacing(double value)
        {
            return IsFinite(value) && value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal struct WindPoint
    {
        public double East { get; set; }
        public double North { get; set; }
        public double Time { get; set; }
    }
}
