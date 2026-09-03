using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure layout math for the sextant overlay worksheet (F-Tafel) column:
    /// on narrow aspects the column stacks below the reading column, on
    /// wide aspects it sits side-by-side to its left. Extracted from
    /// SextantOverlay.Build so the layout DECISION is unit-testable without
    /// Unity (screen size arrives as plain floats — Screen.width/height are
    /// not stubbed). Single source of truth for the ~740-unit side-zone
    /// threshold; the builder consumes the result verbatim.
    /// </summary>
    internal static class SextantOverlayLayout
    {
        // Shared §13.3 geometry (1080-space units). Single source of truth
        // for the builders; the FIXLAYOUT threshold below derives from these.
        internal const float OcularSizeUnits = 600f;
        internal const float ColumnGapUnits = 40f;
        internal const float ColumnWidthUnits = 320f;
        internal const float FixColumnWidthUnits = 380f;
        internal const float HintYUnits = 60f;

        public struct FixColumnPlacement
        {
            public bool stacked;
            public float pivotX;
            public float pivotY;
            public float anchorX;
            public float anchorY;
        }

        /// <summary>
        /// Computes the fix-column placement for the given screen size in
        /// pixels. Mirrors the §13.3 overlay geometry: 1080-unit reference
        /// height with the shared constants above.
        /// </summary>
        public static FixColumnPlacement ComputeFixColumnPlacement(float screenWidth, float screenHeight)
        {
            float refWidthUnits = 1080f * screenWidth / Mathf.Max(1f, screenHeight);
            float sideUnits = (refWidthUnits - OcularSizeUnits) * 0.5f;
            var placement = new FixColumnPlacement();
            if (sideUnits < ColumnWidthUnits + FixColumnWidthUnits + 3f * ColumnGapUnits)
            {
                placement.stacked = true;
                placement.pivotX = 1f;
                placement.pivotY = 1f;
                placement.anchorX = -OcularSizeUnits * 0.5f - ColumnGapUnits;
                placement.anchorY = -110f;
            }
            else
            {
                placement.stacked = false;
                placement.pivotX = 1f;
                placement.pivotY = 0.5f;
                placement.anchorX = -OcularSizeUnits * 0.5f - ColumnGapUnits - ColumnWidthUnits - ColumnGapUnits;
                placement.anchorY = 0f;
            }
            return placement;
        }
    }
}
