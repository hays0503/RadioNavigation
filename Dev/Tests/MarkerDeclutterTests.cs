using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pure-decision tests for the map marker declutter (ADR-032): greedy
    /// min-gap selection, priority ranking, forced (pinned/hovered) items,
    /// gap boundary semantics and NaN handling.
    /// </summary>
    public class MarkerDeclutterTests
    {
        private static MarkerDeclutter.Item Item(
            int viewIndex, double x, double y, double priority = 0.0, bool forced = false)
        {
            return new MarkerDeclutter.Item
            {
                ViewIndex = viewIndex,
                X = x,
                Y = y,
                Priority = priority,
                Forced = forced
            };
        }

        private static bool[] Run(List<MarkerDeclutter.Item> items, double gap, int size)
        {
            bool[] shown = new bool[size];
            MarkerDeclutter.Select(items, gap, shown);
            return shown;
        }

        [Fact]
        public void Select_EmptyInput_ShowsNothing()
        {
            Assert.Equal(0, MarkerDeclutter.Select(
                new List<MarkerDeclutter.Item>(), 10.0, new bool[4]));
        }

        [Fact]
        public void Select_SingleItem_IsShown()
        {
            var items = new List<MarkerDeclutter.Item> { Item(0, 5.0, 5.0, 20.0) };
            Assert.Equal(1, MarkerDeclutter.Select(items, 10.0, new bool[1]));
        }

        [Fact]
        public void Select_TwoFarApart_BothShown()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0, 10.0),
                Item(1, 100.0, 100.0, 5.0)
            };
            bool[] shown = Run(items, 10.0, 2);
            Assert.True(shown[0]);
            Assert.True(shown[1]);
        }

        [Fact]
        public void Select_TwoWithinGap_HigherPriorityWins()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0, 5.0),
                Item(1, 3.0, 4.0, 30.0) // distance 5 < gap 10, priority 30 > 5
            };
            bool[] shown = Run(items, 10.0, 2);
            Assert.False(shown[0]);
            Assert.True(shown[1]);
        }

        [Fact]
        public void Select_EqualPriority_LowerViewIndexWins()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(7, 0.0, 0.0, 10.0),
                Item(2, 1.0, 0.0, 10.0) // same priority, closer index must win
            };
            bool[] shown = Run(items, 10.0, 8);
            Assert.True(shown[2]);
            Assert.False(shown[7]);
        }

        [Fact]
        public void Select_ChainOfPairs_FirstAndThirdSurvive()
        {
            // Priorities descend along the line so acceptance order follows
            // input order: A accepted, B (0.8×gap from A) suppressed, C
            // compared against the accepted set only — 1.6×gap from A —
            // and accepted.
            double gap = 10.0;
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0, 30.0),
                Item(1, 8.0, 0.0, 20.0),
                Item(2, 16.0, 0.0, 10.0)
            };
            bool[] shown = Run(items, gap, 3);
            Assert.True(shown[0]);
            Assert.False(shown[1]);
            Assert.True(shown[2]);
        }

        [Fact]
        public void Select_ForcedItem_ClaimsSpaceDespiteLowestPriority()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0, 100.0),
                Item(1, 2.0, 0.0, 1.0, forced: true)
            };
            bool[] shown = Run(items, 10.0, 2);
            Assert.True(shown[1]);
            Assert.False(shown[0]); // even the top priority yields to forced
        }

        [Theory]
        [InlineData(10.0, true)]  // distance exactly == gap: not suppressed
        [InlineData(9.999, false)]
        [InlineData(10.001, true)]
        public void Select_GapBoundary_IsStrict(double distance, bool expected)
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0, 10.0),
                Item(1, distance, 0.0, 5.0)
            };
            bool[] shown = Run(items, 10.0, 2);
            Assert.Equal(expected, shown[1]);
        }

        [Fact]
        public void Select_NanPosition_ShownButNeverClaimsSpace()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, double.NaN, 0.0, 50.0),
                Item(1, double.NaN, 0.0, 40.0), // also NaN: both must show
                Item(2, 0.1, 0.0, 10.0)         // near the NaNs: must not be suppressed
            };
            bool[] shown = Run(items, 10.0, 3);
            Assert.True(shown[0]);
            Assert.True(shown[1]);
            Assert.True(shown[2]);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Select_NonPositiveGap_ShowsEverything(double gap)
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(0, 0.0, 0.0),
                Item(1, 0.5, 0.0),
                Item(2, 1.0, 0.0)
            };
            bool[] shown = Run(items, gap, 3);
            Assert.True(shown[0]);
            Assert.True(shown[1]);
            Assert.True(shown[2]);
        }

        [Fact]
        public void Select_ViewIndexOutOfRange_IsIgnoredWithoutThrowing()
        {
            var items = new List<MarkerDeclutter.Item>
            {
                Item(42, 0.0, 0.0),  // beyond buffer
                Item(-1, 1.0, 0.0)   // negative
            };
            Assert.Equal(0, MarkerDeclutter.Select(items, 10.0, new bool[4]));
        }

        [Fact]
        public void Select_RepeatedCalls_ClearPreviousResults()
        {
            var items = new List<MarkerDeclutter.Item> { Item(1, 0.0, 0.0) };
            bool[] shown = new bool[2];

            Assert.Equal(1, MarkerDeclutter.Select(items, 10.0, shown));
            Assert.True(shown[1]);

            items.Clear();
            Assert.Equal(0, MarkerDeclutter.Select(items, 10.0, shown));
            Assert.False(shown[1]); // stale flag must not survive the call
        }

        [Fact]
        public void Select_DenseCluster_KeepsOnePerGapRadius()
        {
            // Five markers 1 unit apart on a line, gap 2.5, priorities
            // descending: greedy accepts x=0, suppresses x=1 and x=2 (both
            // within the gap of the accepted set), accepts x=3 (3 ≥ gap
            // from x=0), suppresses x=4 (1 from x=3). Result: 0 and 3.
            var items = new List<MarkerDeclutter.Item>();
            for (int i = 0; i < 5; i++)
            {
                items.Add(Item(i, i, 0.0, 10.0 - i));
            }

            bool[] shown = Run(items, 2.5, 5);
            Assert.True(shown[0]);
            Assert.False(shown[1]);
            Assert.False(shown[2]);
            Assert.True(shown[3]);
            Assert.False(shown[4]);
        }
    }
}
