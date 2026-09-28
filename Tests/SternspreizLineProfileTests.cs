using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the line-profile math and the angular/pixel/world conversions that
    /// keep the Sternspreiz streak zoom-independent. The uniform flat-top shape
    /// is the documented mathematical baseline, not a measured optical model.
    /// </summary>
    public sealed class SternspreizLineProfileTests
    {
        [Fact]
        public void FlatTopRow_IsNormalizedAndSymmetric()
        {
            float[] row = SternspreizLineProfile.BuildFlatTopRow(64, 0.2f);

            Assert.Equal(64, row.Length);
            double sum = 0d;
            for (int i = 0; i < row.Length; i++)
                sum += row[i];
            Assert.Equal(1d, sum, 6);
            for (int i = 0; i < row.Length; i++)
                Assert.Equal(row[i], row[row.Length - 1 - i], 6);
        }

        [Fact]
        public void FlatTopRow_ZeroEdge_IsUniform()
        {
            float[] row = SternspreizLineProfile.BuildFlatTopRow(8, 0f);

            for (int i = 0; i < row.Length; i++)
                Assert.Equal(0.125f, row[i], 6);
        }

        [Fact]
        public void FlatTopRow_EdgesAreDimmerThanCentre()
        {
            float[] row = SternspreizLineProfile.BuildFlatTopRow(32, 0.3f);

            Assert.True(row[0] < row[16]);
            Assert.True(row[31] < row[16]);
            Assert.Equal(row[16], row[15], 6);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-4)]
        public void FlatTopRow_RejectsTooFewSamples(int samples)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.BuildFlatTopRow(samples, 0.1f));
        }

        [Theory]
        [InlineData(-0.1f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(float.NaN)]
        public void FlatTopRow_RejectsInvalidEdgeFraction(float edge)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.BuildFlatTopRow(16, edge));
        }

        [Fact]
        public void ResampleKernel_PreservesUnitEnergyAndImpulseLimit()
        {
            float[] flat = SternspreizLineProfile.ResampleKernel(new[] { 1f }, 16);
            for (int i = 0; i < flat.Length; i++)
                Assert.Equal(0.0625f, flat[i], 6);

            float[] kernel = SternspreizPsfMath.BuildHorizontalLineSpreadKernel(4f, 8);
            float[] row = SternspreizLineProfile.ResampleKernel(kernel, 32);
            double sum = 0d;
            for (int i = 0; i < row.Length; i++)
                sum += row[i];
            Assert.Equal(1d, sum, 6);
            Assert.True(row[16] > row[0]);
        }

        [Fact]
        public void ResampleKernel_RejectsBadInput()
        {
            Assert.Throws<ArgumentNullException>(
                () => SternspreizLineProfile.ResampleKernel(null, 8));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.ResampleKernel(new[] { 1f }, 1));
            Assert.Throws<ArgumentException>(
                () => SternspreizLineProfile.ResampleKernel(new float[0], 8));
        }

        [Fact]
        public void AngularWidth_UsesSmallAngleTangent()
        {
            // 0.1 deg at 3825 m is ~6.68 m; 1 deg is 2*3825*tan(0.5deg) = ~66.76 m.
            Assert.Equal(6.68f, SternspreizLineProfile.AngularWidthMeters(0.1d, 3825d), 2);
            Assert.Equal(66.76f, SternspreizLineProfile.AngularWidthMeters(1d, 3825d), 2);
            Assert.Equal(0f, SternspreizLineProfile.AngularWidthMeters(0d, 3825d), 6);
        }

        [Fact]
        public void AngularWidth_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.AngularWidthMeters(-1d, 100d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.AngularWidthMeters(1d, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.AngularWidthMeters(double.NaN, 100d));
        }

        [Fact]
        public void PixelsForAngle_SixTimesZoomScalesByTangentNotBySix()
        {
            // Perspective projects through tan: the pixel ratio between two
            // zoom steps is tan(fov1/2)/tan(fov2/2), which for the sextant's
            // 65 deg -> 65/6 deg steps is ~6.72, not exactly 6. The invariant
            // is the ANGLE; the pixel size follows the tangent.
            double atOne = SternspreizLineProfile.PixelsForAngle(1d, 65d, 1267);
            double atSix = SternspreizLineProfile.PixelsForAngle(1d, 65d / 6d, 1267);

            Assert.Equal(17.3559d, atOne, 3);
            Assert.Equal(6.7192d, atSix / atOne, 3);
        }

        [Fact]
        public void PixelsForAngle_MatchesOneArcminuteAtOnePower()
        {
            // 1 arcmin at x1 / 65 deg / 1267 px: tan(0.5')/tan(32.5deg)*1267 = 0.289 px.
            double px = SternspreizLineProfile.PixelsForAngle(1d / 60d, 65d, 1267);
            Assert.Equal(0.2893d, px, 4);
        }

        [Fact]
        public void AngleForPixels_RoundTrips()
        {
            double angle = 0.37d;
            double px = SternspreizLineProfile.PixelsForAngle(angle, 42d, 1024);
            double back = SternspreizLineProfile.AngleForPixels(px, 42d, 1024);
            Assert.Equal(angle, back, 9);
        }

        [Fact]
        public void PixelsForAngle_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.PixelsForAngle(-1d, 65d, 1267));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.PixelsForAngle(1d, 0d, 1267));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.PixelsForAngle(1d, 65d, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SternspreizLineProfile.AngleForPixels(double.NaN, 65d, 1267));
        }

        [Fact]
        public void StreakAlphaMap_CentreColumnIsTheBrightest()
        {
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] profile = SternspreizLineProfile.BuildFlatTopRow(
                columns, SternspreizLineProfile.DefaultEdgeFraction);
            float[] map = BuildDefaultMap(columns, rows);

            Assert.Equal(columns * rows, map.Length);
            int centreColumn = columns / 2;
            int centreRow = rows / 2;
            float centre = map[centreRow * columns + centreColumn];
            // The flat core reaches the row centre at full brightness, so the
            // baked alpha there is exactly the (normalized) line profile value.
            Assert.Equal(profile[centreColumn], centre, 6);

            for (int i = 0; i < map.Length; i++)
                Assert.True(map[i] <= centre + 1e-6f, "alpha exceeds the centre value");
        }

        /// <summary>
        /// Strict narrowing centre -> tip on the NEW 2026-11-07 defaults
        /// (widthTaper 2.2, minWidth 0.05, 96x48 texture). The visible extent
        /// (rows above half the local profile value) runs 34 -> 20 -> 8 -> 2; the
        /// last sample sits on the minWidth floor and is pinned separately by
        /// <see cref="StreakAlphaMap_TipWidthBoundedByMinWidth"/>.
        /// </summary>
        [Fact]
        public void StreakAlphaMap_WidthTaperNarrowsMonotonicallyToTheTips()
        {
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] map = BuildDefaultMap(columns, rows);

            // The taper must not disappear: the energy of every column strictly
            // falls from the centre column to the tip.
            int centreColumn = columns / 2;
            double previous = ColumnEnergy(map, columns, rows, centreColumn);
            for (int x = centreColumn + 1; x < columns; x++)
            {
                double energy = ColumnEnergy(map, columns, rows, x);
                Assert.True(energy < previous,
                    "column energy must strictly fall from the centre to the tip at x=" + x);
                previous = energy;
            }

            // The visible extent (rows above half the local profile value) also
            // shrinks strictly on the sampled columns.
            float[] profile = SternspreizLineProfile.BuildFlatTopRow(
                columns, SternspreizLineProfile.DefaultEdgeFraction);
            int previousExtent = RowsAboveHalfBand(map, profile, columns, rows, centreColumn);
            Assert.Equal(34, previousExtent);
            foreach (int x in new[] { 36, 24, 12 })
            {
                int extent = RowsAboveHalfBand(map, profile, columns, rows, x);
                Assert.True(extent < previousExtent,
                    "sampled extent must strictly fall at x=" + x);
                previousExtent = extent;
            }
            Assert.Equal(2, previousExtent);
        }

        /// <summary>
        /// The 2026-11-07 request: the rhombus tips must be sharper than the old
        /// linear taper (widthTaper = 1). The local half-width is
        /// (1 - along)^widthTaper, so at the line midpoint (along ~ 0.5) it is
        /// 0.5^2.2 ~ 0.22 against ~0.5 for the linear taper. The band's 50%
        /// crossing scales with the local width, so the visible-extent ratio
        /// centre -> midpoint mirrors that half-width ratio.
        /// </summary>
        [Fact]
        public void StreakAlphaMap_DefaultTaperIsSharperThanTheOldLinearOne()
        {
            Assert.True(Math.Pow(0.5f, SternspreizLineProfile.DefaultWidthTaper) < 0.3f,
                "the default taper must be sharper than the old linear one");

            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] map = BuildDefaultMap(columns, rows);
            float[] profile = SternspreizLineProfile.BuildFlatTopRow(
                columns, SternspreizLineProfile.DefaultEdgeFraction);

            int centreColumn = columns / 2;
            int midColumn = columns / 4; // along ~ 0.49 - the line midpoint sample
            int centreExtent = RowsAboveHalfBand(map, profile, columns, rows, centreColumn);
            int midExtent = RowsAboveHalfBand(map, profile, columns, rows, midColumn);

            Assert.True(midExtent > 0, "the midpoint must still show a band");
            Assert.True(midExtent < 0.3 * centreExtent,
                "midpoint extent " + midExtent + " of centre " + centreExtent
                + " is not sharper than the old linear taper (~0.52)");
        }

        [Fact]
        public void StreakAlphaMap_TipWidthBoundedByMinWidth()
        {
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float minWidth = SternspreizLineProfile.DefaultMinWidth;
            float[] map = BuildDefaultMap(columns, rows);

            int tipExtent = 0;
            for (int y = 0; y < rows; y++)
                if (map[y * columns] > 0f)
                    tipExtent++;

            int bound = (int)Math.Ceiling(minWidth * rows) + 1;
            Assert.True(tipExtent > 0, "the tip must still show a thin line");
            Assert.True(tipExtent <= bound,
                "tip extent " + tipExtent + " exceeds the minWidth bound " + bound);
        }

        [Fact]
        public void StreakAlphaMap_ZeroWidthTaperIsConstantWidth()
        {
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] map = SternspreizLineProfile.BuildStreakAlphaMap(columns, rows,
                SternspreizLineProfile.DefaultEdgeFraction,
                0f,
                SternspreizLineProfile.DefaultMinWidth,
                SternspreizLineProfile.DefaultCoreFraction);
            float[] profile = SternspreizLineProfile.BuildFlatTopRow(
                columns, SternspreizLineProfile.DefaultEdgeFraction);

            int centreExtent = RowsAboveHalfBand(map, profile, columns, rows, columns / 2);
            for (int x = 0; x < columns; x++)
                Assert.Equal(centreExtent, RowsAboveHalfBand(map, profile, columns, rows, x));
        }

        [Fact]
        public void StreakAlphaMap_IsColumnSymmetric()
        {
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] map = BuildDefaultMap(columns, rows);

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                    Assert.Equal(map[y * columns + x], map[y * columns + (columns - 1 - x)], 6);
            }
        }

        [Fact]
        public void StreakAlphaMap_DegenerateSizesAreClamped()
        {
            float[] zero = SternspreizLineProfile.BuildStreakAlphaMap(0, 0,
                SternspreizLineProfile.DefaultEdgeFraction, 1f,
                SternspreizLineProfile.DefaultMinWidth,
                SternspreizLineProfile.DefaultCoreFraction);
            Assert.Equal(4, zero.Length);

            float[] one = SternspreizLineProfile.BuildStreakAlphaMap(1, 5,
                SternspreizLineProfile.DefaultEdgeFraction, 1f,
                SternspreizLineProfile.DefaultMinWidth,
                SternspreizLineProfile.DefaultCoreFraction);
            Assert.Equal(10, one.Length);

            foreach (float value in one)
                Assert.True(value >= 0f && value <= 1f, "alpha out of range");
        }

        [Fact]
        public void StreakAlphaMap_RejectsInvalidParameters()
        {
            float minWidth = SternspreizLineProfile.DefaultMinWidth;
            float coreFraction = SternspreizLineProfile.DefaultCoreFraction;
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizLineProfile.BuildStreakAlphaMap(8, 8, 0.2f, -1f, minWidth, coreFraction));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizLineProfile.BuildStreakAlphaMap(8, 8, 0.2f, 1f, 0f, coreFraction));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizLineProfile.BuildStreakAlphaMap(8, 8, 0.2f, 1f, 1.5f, coreFraction));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizLineProfile.BuildStreakAlphaMap(8, 8, 0.2f, 1f, minWidth, 1f));
            // edgeFraction is delegated to BuildFlatTopRow.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizLineProfile.BuildStreakAlphaMap(8, 8, 0.6f, 1f, minWidth, coreFraction));
        }

        private static float[] BuildDefaultMap(int columns, int rows)
        {
            return SternspreizLineProfile.BuildStreakAlphaMap(columns, rows,
                SternspreizLineProfile.DefaultEdgeFraction,
                SternspreizLineProfile.DefaultWidthTaper,
                SternspreizLineProfile.DefaultMinWidth,
                SternspreizLineProfile.DefaultCoreFraction);
        }

        private static double ColumnEnergy(float[] map, int columns, int rows, int column)
        {
            double sum = 0d;
            for (int y = 0; y < rows; y++)
                sum += map[y * columns + column];
            return sum;
        }

        private static int RowsAboveHalfBand(float[] map, float[] profile,
            int columns, int rows, int column)
        {
            float threshold = 0.5f * profile[column];
            int count = 0;
            for (int y = 0; y < rows; y++)
            {
                if (map[y * columns + column] > threshold)
                    count++;
            }
            return count;
        }
    }
}
