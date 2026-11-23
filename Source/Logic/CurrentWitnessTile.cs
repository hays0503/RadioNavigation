using System;
using System.IO;

namespace UBOAT.Mods.RadioNavigation
{
    // RNW1 offline interchange, not a shipping climatology or a tide model.
    // All 16 corners must be supported even if some weights are zero.
    internal sealed class CurrentWitnessTile
    {
        private const int MaxBytes = 2 * 1024 * 1024;
        private readonly double[][] axes;
        private readonly short[] vectors;
        private readonly int depthCount;
        private readonly int latitudeCount;
        private readonly int longitudeCount;

        private CurrentWitnessTile(double[][] axes, short[] vectors)
        {
            this.axes = axes;
            this.vectors = vectors;
            depthCount = axes[1].Length;
            latitudeCount = axes[2].Length;
            longitudeCount = axes[3].Length;
        }

        public static bool TryRead(byte[] bytes, out CurrentWitnessTile tile)
        {
            tile = null;
            if (bytes == null || bytes.Length < 12 || bytes.Length > MaxBytes)
                return false;
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x31574E52u) // RNW1
                    return false;
                int[] sizes = { reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16() };
                if (!ValidateSize(sizes, bytes.Length, out int count))
                    return false;
                var axes = ReadAxes(reader, sizes);
                if (axes == null)
                    return false;
                var vectors = ReadVectors(reader, count);
                if (vectors == null)
                    return false;
                tile = new CurrentWitnessTile(axes, vectors);
                return true;
            }
        }

        public bool HasAnyFlow()
        {
            for (int i = 0; i < vectors.Length; i += 2)
                if (vectors[i] != short.MinValue && (vectors[i] != 0 || vectors[i + 1] != 0))
                    return true;
            return false;
        }

        public double West => axes[3][0];
        public double East => axes[3][longitudeCount - 1];
        public double South => axes[2][0];
        public double North => axes[2][latitudeCount - 1];

        public bool TryGetSurfaceLandMask(double latitude, double longitude, out bool isLand)
        {
            isLand = false;
            if (!TryNearestAxis(axes[2], latitude, out int iy) || !TryNearestAxis(axes[3], longitude, out int ix))
                return false;
            int packed = PackedIndex(0, 0, iy, ix);
            isLand = vectors[packed] == short.MinValue;
            return true;
        }

        private static bool TryNearestAxis(double[] axis, double value, out int index)
        {
            index = 0;
            if (value < axis[0] - 0.5 || value > axis[axis.Length - 1] + 0.5)
                return false;
            int hit = Array.BinarySearch(axis, value);
            if (hit >= 0)
            {
                index = hit;
                return true;
            }
            int right = ~hit;
            if (right <= 0)
                index = 0;
            else if (right >= axis.Length)
                index = axis.Length - 1;
            else
                index = Math.Abs(value - axis[right - 1]) <= Math.Abs(axis[right] - value) ? right - 1 : right;
            return true;
        }

        // The bounded regional fixture format admits disjoint rectangles and
        // fully matched edges/corners, not overlapping interiors or T junctions.
        public bool IsCompatibleWith(CurrentWitnessTile other)
        {
            if (!AxisEquals(axes[0], other.axes[0]) || !AxisEquals(axes[1], other.axes[1]))
                return false;
            double south = Math.Max(South, other.South);
            double north = Math.Min(North, other.North);
            double west = Math.Max(West, other.West);
            double east = Math.Min(East, other.East);
            if (south > north || west > east)
                return true;
            if (south < north && west < east)
                return false;
            if (south < north && !AxisEquals(axes[2], other.axes[2]))
                return false;
            if (west < east && !AxisEquals(axes[3], other.axes[3]))
                return false;
            return SharesNodes(other, south, north, west, east);
        }

        private bool SharesNodes(CurrentWitnessTile other, double south, double north, double west, double east)
        {
            int thisY = Array.BinarySearch(axes[2], south);
            int thatY = Array.BinarySearch(other.axes[2], south);
            int thisX = Array.BinarySearch(axes[3], west);
            int thatX = Array.BinarySearch(other.axes[3], west);
            int rows = south == north ? 1 : latitudeCount;
            int columns = west == east ? 1 : longitudeCount;
            for (int t = 0; t < axes[0].Length; t++)
                for (int d = 0; d < depthCount; d++)
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < columns; x++)
                        {
                            int index = PackedIndex(t, d, thisY + y, thisX + x);
                            int otherIndex = other.PackedIndex(t, d, thatY + y, thatX + x);
                            if (vectors[index] != other.vectors[otherIndex]
                                || vectors[index + 1] != other.vectors[otherIndex + 1])
                                return false;
                        }
            return true;
        }

        private int PackedIndex(int t, int d, int y, int x) =>
            2 * (((t * depthCount + d) * latitudeCount + y) * longitudeCount + x);

        private static bool AxisEquals(double[] a, double[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        public bool TrySample(double utcSeconds, double depth, double latitude, double longitude,
            out CurrentVelocity velocity)
        {
            velocity = default(CurrentVelocity);
            if (!Locate(axes[0], utcSeconds, out Cell t) || !Locate(axes[1], depth, out Cell d)
                || !Locate(axes[2], latitude, out Cell y) || !Locate(axes[3], longitude, out Cell x))
                return false;
            double east = 0;
            double north = 0;
            for (int mask = 0; mask < 16; mask++)
            {
                int packed = PackedIndex(t.Index + (mask >> 3), d.Index + ((mask >> 2) & 1),
                    y.Index + ((mask >> 1) & 1), x.Index + (mask & 1));
                if (vectors[packed] == short.MinValue)
                    return false;
                double weight = t.Weight(mask & 8) * d.Weight(mask & 4) * y.Weight(mask & 2) * x.Weight(mask & 1);
                east += weight * vectors[packed];
                north += weight * vectors[packed + 1];
            }
            velocity = new CurrentVelocity((float)(east * 0.0001), (float)(north * 0.0001));
            return true;
        }

        private static bool ValidateSize(int[] sizes, int length, out int count)
        {
            int sum = 0;
            count = 1;
            foreach (int size in sizes)
            {
                if (size < 2 || size > 128)
                    return false;
                sum += size;
                count *= size;
            }
            return length == 12 + 8 * sum + 4 * count;
        }

        private static double[][] ReadAxes(BinaryReader reader, int[] sizes)
        {
            var axes = new double[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                var axis = new double[sizes[i]];
                double previous = double.NegativeInfinity;
                for (int j = 0; j < axis.Length; j++)
                {
                    double value = reader.ReadDouble();
                    if (!(value > previous) || double.IsInfinity(value))
                        return null;
                    axis[j] = value;
                    previous = value;
                }
                axes[i] = axis;
            }
            return axes;
        }

        private static short[] ReadVectors(BinaryReader reader, int count)
        {
            var vectors = new short[count * 2];
            for (int i = 0; i < vectors.Length; i += 2)
            {
                short east = reader.ReadInt16();
                short north = reader.ReadInt16();
                if ((east == short.MinValue) != (north == short.MinValue))
                    return null;
                vectors[i] = east;
                vectors[i + 1] = north;
            }
            return vectors;
        }

        private static bool Locate(double[] axis, double value, out Cell cell)
        {
            cell = default(Cell);
            if (!(value >= axis[0] && value <= axis[axis.Length - 1]))
                return false;
            int hit = Array.BinarySearch(axis, value);
            int index = Math.Min(hit >= 0 ? hit : ~hit - 1, axis.Length - 2);
            cell = new Cell(index, (value - axis[index]) / (axis[index + 1] - axis[index]));
            return true;
        }

        private struct Cell
        {
            public readonly int Index;
            private readonly double fraction;

            public Cell(int index, double fraction)
            {
                Index = index;
                this.fraction = fraction;
            }

            public double Weight(int high) => high == 0 ? 1 - fraction : fraction;
        }
    }
}
