using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Screen-space decluttering for map markers (ADR-032): greedy min-gap
    /// selection over a flat coordinate plane. Items are ranked (forced
    /// first, then priority descending, deterministic tie-break by view
    /// index) and accepted one by one; an item is suppressed when it sits
    /// closer than the gap to any already-accepted item. Because the gap is
    /// expressed in world units derived from the current zoom
    /// (gapPx × unitsPerPixel), suppression self-adapts to the map zoom:
    /// far zoom hides the dense majority and keeps the tallest lights,
    /// close zoom shows everything that has room.
    ///
    /// Pure C# (System only, no UnityEngine) so it runs in the offline test
    /// project, mirroring LighthouseMarkerSizing. Coordinates are
    /// caller-chosen flat units (the mod uses scene-space x/z); the gap must
    /// use the same units. O(n²) distance checks against the accepted set —
    /// n is the marker count (hundreds), trivially cheap per frame.
    /// </summary>
    public static class MarkerDeclutter
    {
        /// <summary>
        /// Suppression radius in icon diameters: 1.3 keeps the 80-unit
        /// backdrop (1.25× the 64-unit icon) of two neighbors from touching.
        /// </summary>
        public const float DefaultGapFactor = 1.3f;

        /// <summary>One candidate marker in flat plane coordinates.</summary>
        public struct Item
        {
            /// <summary>Caller-side identity (the view index).</summary>
            public int ViewIndex;
            public double X;
            public double Y;
            /// <summary>Higher wins the slot (the mod uses fire height).</summary>
            public double Priority;
            /// <summary>Pinned/hovered views are accepted first and never
            /// suppressed, so their tooltip can never dangle on a hidden
            /// root (deactivated objects get no PointerExit).</summary>
            public bool Forced;
        }

        // Scratch buffers reused across calls (single-threaded UI use, like
        // the placer's entity-type buffers). Cleared at the start of every
        // Select call, so repeated calls are safe.
        private static readonly List<double> acceptedX = new List<double>(256);
        private static readonly List<double> acceptedY = new List<double>(256);

        // Cached comparison delegate: List.Sort would allocate a new one
        // every frame otherwise.
        private static readonly Comparison<Item> rankOrder = RankItems;

        /// <summary>
        /// Selects the shown subset. <paramref name="shownByViewIndex"/> is
        /// indexed by ViewIndex, cleared here, then set true for every
        /// accepted item. Returns the number of shown items.
        /// Semantics: gap &lt;= 0 or empty input shows everything; NaN
        /// coordinates are always shown but never claim space (a broken
        /// position must not suppress its neighbors).
        /// </summary>
        public static int Select(List<Item> items, double gap, bool[] shownByViewIndex)
        {
            if (shownByViewIndex == null)
            {
                return 0;
            }

            Array.Clear(shownByViewIndex, 0, shownByViewIndex.Length);
            if (items == null || items.Count == 0)
            {
                return 0;
            }

            if (gap <= 0.0)
            {
                return ShowAll(items, shownByViewIndex);
            }

            items.Sort(rankOrder);
            acceptedX.Clear();
            acceptedY.Clear();

            double gapSq = gap * gap;
            int shownCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.ViewIndex < 0 || item.ViewIndex >= shownByViewIndex.Length)
                {
                    continue;
                }

                if (double.IsNaN(item.X) || double.IsNaN(item.Y))
                {
                    shownByViewIndex[item.ViewIndex] = true;
                    shownCount++;
                    continue;
                }

                if (item.Forced || !HasCloserNeighbor(item.X, item.Y, gapSq))
                {
                    shownByViewIndex[item.ViewIndex] = true;
                    acceptedX.Add(item.X);
                    acceptedY.Add(item.Y);
                    shownCount++;
                }
            }

            return shownCount;
        }

        /// <summary>True when any accepted item is strictly inside the gap.</summary>
        private static bool HasCloserNeighbor(double x, double y, double gapSq)
        {
            for (int i = 0; i < acceptedX.Count; i++)
            {
                double dx = acceptedX[i] - x;
                double dy = acceptedY[i] - y;
                if (dx * dx + dy * dy < gapSq)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Marks every in-range item shown (disabled/degenerate case).</summary>
        private static int ShowAll(List<Item> items, bool[] shownByViewIndex)
        {
            int shownCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int viewIndex = items[i].ViewIndex;
                if (viewIndex >= 0 && viewIndex < shownByViewIndex.Length)
                {
                    shownByViewIndex[viewIndex] = true;
                    shownCount++;
                }
            }

            return shownCount;
        }

        /// <summary>
        /// Ranking: forced first (they claim space before anyone else),
        /// then priority descending, then view index ascending so equal
        /// priorities resolve deterministically regardless of input order.
        /// </summary>
        private static int RankItems(Item a, Item b)
        {
            if (a.Forced != b.Forced)
            {
                return a.Forced ? -1 : 1;
            }

            int byPriority = b.Priority.CompareTo(a.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            return a.ViewIndex.CompareTo(b.ViewIndex);
        }
    }
}
