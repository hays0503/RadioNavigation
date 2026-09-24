using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure layout math for the sextant overlay worksheet (F-Tafel) column.
    /// Wide unit spaces (unitWidth >= TopLeftMinWidthUnits) pin the card to
    /// the screen's top-left corner (pivot (0,1), 24-unit side margin, 80-unit
    /// top margin), so it never sits over the game's bottom UI; narrower ones
    /// keep the legacy stacked fallback below the reading column, with the
    /// card's left edge clamped on screen. Extracted from SextantOverlay.Build
    /// so the layout DECISION is unit-testable without Unity (screen size
    /// arrives as plain floats — Screen.width/height are not stubbed). The
    /// builder consumes the result verbatim.
    ///
    /// Units follow the game's own convention (2026-09-22): the overlay
    /// canvas scales with the screen size divided by the game's UI scale
    /// (userSettings.ControlSettings.uiScale, PgUp/PgDn, 0.5…4), exactly like
    /// the HUD canvases (FullscreenRect, CanvasUserSettings). So the layout
    /// space is (screenWidth, screenHeight) / uiScale — at 1080p and uiScale 1
    /// that is the familiar 1920x1080 unit space, and a larger UI scale
    /// leaves fewer units, hence a smaller usable card height.
    /// </summary>
    internal static class SextantOverlayLayout
    {
        // Shared §13.3 geometry (game units). Single source of truth for the
        // builders. The ocular side is NOT a constant any more (2026-11-04):
        // it is a fraction of the unit height (SextantOcularSize), passed into
        // the placement so the stacked fallback and the columns follow it.
        internal const float ColumnGapUnits = 30f;
        internal const float ColumnWidthUnits = 200f;
        /// <summary>Sun verification column width (right of the ocular). The
        /// widest block line (the body readout) measures ~231 units, so 240
        /// keeps a small margin; the separate wide sun line was folded into
        /// the block (2026-11-04), so the column shrank from 440 to 240.</summary>
        internal const float SunColumnWidthUnits = 240f;
        /// <summary>Horizontal budget the ocular must leave for the two side
        /// columns and their gaps (both sides): the ocular width clamp uses it
        /// (SextantOcularSize), so every column — and the text it can render —
        /// stays on screen. The widest column wins: the reading column's text
        /// is right-aligned at the ocular edge, the sun column's is
        /// left-aligned, so both need their full width outside the circle.
        /// (2026-11-04: the separate wide sun line was folded into the bodies
        /// block, so the sun column shrank 440 → 240 and the budget 840 → 540;
        /// the old sum-of-columns form under-budgeted the wider side.)</summary>
        internal const float SideColumnsUnits =
            2f * (ColumnGapUnits + (ColumnWidthUnits > SunColumnWidthUnits
                ? ColumnWidthUnits : SunColumnWidthUnits));
        /// <summary>Worksheet card width (longest line ~321 units).</summary>
        internal const float FixColumnWidthUnits = 342f;
        /// <summary>Y of the glare-pain warning line (above the game's bottom
        /// button row, whose tops end at y=193). Was HintYUnits + 155 while
        /// the bottom hint line existed (removed 2026-11-04: the keys live in
        /// the help panel).</summary>
        internal const float PainYUnits = 215f;
        /// <summary>Y of the bodies block (the label's rect centre) inside the
        /// sun column; its text flows down from here.</summary>
        internal const float BodiesYUnits = 150f;
        /// <summary>Base font of the bodies block (the star rows shrink below
        /// it when the uncapped list would run into the game's bottom UI).</summary>
        internal const int BodiesFontSize = 11;
        /// <summary>Top edge of the game's bottom button row, measured from
        /// the screen's bottom edge (the bodies text must stay above it).</summary>
        internal const float BottomUiTopUnits = 193f;

        /// <summary>Vertical room for the bodies block (canvas units): from
        /// its text top down to the game's bottom button row. The row list is
        /// uncapped (2026-11-04), so at high UI scales the block shrinks its
        /// font to fit this (SextantBodiesReadout auto-fit). Degenerate inputs
        /// return 0 — the fitter then keeps the base font.</summary>
        public static float BodiesTextAvailableUnits(float screenHeight, float uiScale)
        {
            if (screenHeight <= 0f || float.IsNaN(screenHeight))
                return 0f;
            if (uiScale <= 0f || float.IsNaN(uiScale))
                uiScale = 1f;
            float halfHeight = 0.5f * screenHeight / uiScale;
            float textTop = BodiesYUnits + BodiesFontSize * 1.8f * 0.5f;
            float available = textTop + halfHeight - BottomUiTopUnits;
            return available > 0f ? available : 0f;
        }
        /// <summary>Bottom margin the card must not cross.</summary>
        internal const float BottomMarginUnits = 20f;
        /// <summary>Top-left branch: screen top edge to the card's top edge.</summary>
        internal const float TopMarginUnits = 80f;
        /// <summary>Minimum unit width for the top-left branch; narrower
        /// aspects fall back to the stacked placement below the reading
        /// column.</summary>
        internal const float TopLeftMinWidthUnits = 1650f;
        /// <summary>Side margin the card keeps from the screen edge.</summary>
        private const float SideMarginUnits = 24f;

        public struct FixColumnPlacement
        {
            public bool stacked;
            public float pivotX;
            public float pivotY;
            public float anchorX;
            public float anchorY;
            /// <summary>Usable column height in canvas units: from the anchored
            /// top edge down to the bottom margin. The card auto-font fits its
            /// text into this (2026-09-22).</summary>
            public float maxHeightUnits;
        }

        /// <summary>
        /// Computes the fix-column placement for the given screen size in
        /// pixels and the game's UI scale (1 = default). The layout space is
        /// the screen divided by the UI scale. Wide spaces get the top-left
        /// placement (pivot = the card's top-left corner; anchors stay
        /// screen-centred); narrow ones the stacked fallback whose right edge
        /// hugs the reading column and whose left edge is clamped on screen.
        /// <paramref name="ocularSizeUnits"/> is the live ocular side
        /// (SextantOcularSize) the stacked fallback hangs below.
        /// </summary>
        public static FixColumnPlacement ComputeFixColumnPlacement(float screenWidth,
            float screenHeight, float uiScale, float ocularSizeUnits)
        {
            if (uiScale <= 0f || float.IsNaN(uiScale))
                uiScale = 1f;
            if (ocularSizeUnits <= 0f || float.IsNaN(ocularSizeUnits))
                ocularSizeUnits = 600f;
            float unitWidth = screenWidth / uiScale;
            float unitHeight = screenHeight / uiScale;
            if (unitHeight <= 0f)
                unitHeight = 1f;

            var placement = new FixColumnPlacement();
            if (unitWidth >= TopLeftMinWidthUnits)
            {
                // Top-left corner: pivot (0,1) is the card's top-left corner,
                // SideMarginUnits from the screen's left edge and
                // TopMarginUnits below its top edge; hangs down to the
                // bottom margin.
                placement.stacked = false;
                placement.pivotX = 0f;
                placement.pivotY = 1f;
                placement.anchorX = SideMarginUnits - unitWidth * 0.5f;
                placement.anchorY = unitHeight * 0.5f - TopMarginUnits;
                placement.maxHeightUnits = unitHeight - TopMarginUnits
                    - BottomMarginUnits;
            }
            else
            {
                // Legacy fallback: top-anchored below the reading column,
                // pivot (1,1). The right edge keeps the reading column's x
                // (-ocular/2 - gap); on very narrow unit widths the left edge
                // is clamped SideMarginUnits inside the screen's left edge.
                placement.stacked = true;
                placement.pivotX = 1f;
                placement.pivotY = 1f;
                placement.anchorX = Mathf.Max(
                    -ocularSizeUnits * 0.5f - ColumnGapUnits,
                    SideMarginUnits - unitWidth * 0.5f + FixColumnWidthUnits);
                placement.anchorY = -110f;
                placement.maxHeightUnits = placement.anchorY + unitHeight * 0.5f
                    - BottomMarginUnits;
            }
            if (placement.maxHeightUnits < 0f)
                placement.maxHeightUnits = 0f;
            return placement;
        }

        /// <summary>Legacy overload (fixed 600-unit ocular): the pre-2026-11-04
        /// behaviour, kept for the offline tests that pin the placement rules.</summary>
        public static FixColumnPlacement ComputeFixColumnPlacement(float screenWidth,
            float screenHeight, float uiScale)
        {
            return ComputeFixColumnPlacement(screenWidth, screenHeight, uiScale, 600f);
        }

        /// <summary>Default-scale overload (uiScale = 1): the pre-2026-09-22
        /// 1080-unit reference behaviour.</summary>
        public static FixColumnPlacement ComputeFixColumnPlacement(float screenWidth, float screenHeight)
        {
            return ComputeFixColumnPlacement(screenWidth, screenHeight, 1f, 600f);
        }
    }
}
