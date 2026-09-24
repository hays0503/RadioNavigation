using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Fits the F-Tafel card into the height the fix column actually has
    /// (2026-09-22). The column is top-anchored below the reading column in
    /// the stacked layout, so only part of the 1080-unit reference is usable;
    /// with three observations the card (~41 lines) ran off the bottom edge.
    /// Unity's legacy Text scales its height approximately linearly with the
    /// font size (measured within a few percent, in the safe direction), so
    /// one measurement at the base size picks the largest size that fits.
    /// Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class FixCardLayout
    {
        /// <summary>Never shrink below this (readability floor, any mode).</summary>
        public const int MinFontSize = 8;

        /// <summary>Below this the full F-Tafel blocks are dropped in favour of
        /// the compact one-liners — a 9-unit font is the smallest the full
        /// chain stays readable at.</summary>
        public const int FullModeMinFontSize = 9;

        /// <summary>Exit trial font for the mode hysteresis (2026-09-22): a
        /// compact card returns to the full one only when it fits at this
        /// larger size, so the enter (9) and exit (10) edges are mutually
        /// exclusive and the mode cannot oscillate while the UI scale changes
        /// in 0.05 steps.</summary>
        public const int FullModeReturnFontSize = 10;

        /// <summary>Fit tolerance (units) shared by the mode decision.</summary>
        public const float FitToleranceUnits = 0.5f;

        /// <summary>Headroom factor for the status-line reservation: the mode
        /// decision and the font fit are measured from the card WITHOUT the
        /// 3-second transient status line, so one base-font line is reserved.
        /// The measured legacy-Text line is ~1.06-1.13 em (lineSpacing 1), so
        /// this factor is a slight UNDER-reserve: the residual (at most ~1.4
        /// units at base 11) stays inside the column's bottom margin. Kept at
        /// 1.0 on purpose — raising it to ~1.2 would cost a whole font step on
        /// tight layouts (2026-09-22, swarm review).</summary>
        public const float StatusHeadroomFactor = 1.0f;

        /// <summary>Reserved status-line height at the given base font.</summary>
        public static float StatusHeadroomUnits(int baseFontSize)
        {
            if (baseFontSize <= 0)
                return 0f;
            return baseFontSize * StatusHeadroomFactor;
        }

        /// <summary>
        /// Hysteretic full/compact decision (2026-09-22). The single-threshold
        /// rule flipped the whole card when the UI scale crossed a boundary and
        /// when the 3-second status line appeared/disappeared. Rules:
        ///  - enter compact when the card (plus the reserved status line) does
        ///    not fit at <see cref="FullModeMinFontSize"/>;
        ///  - return to full only when it fits at
        ///    <see cref="FullModeReturnFontSize"/> (the larger trial font makes
        ///    the two edges mutually exclusive).
        /// Unknown inputs preserve the current mode — never guess.
        /// </summary>
        public static bool DecideMode(bool currentlyCompact, float heightAtBase,
            float availableUnits, int baseFontSize)
        {
            if (heightAtBase <= 0f || float.IsNaN(heightAtBase)
                || availableUnits <= 0f || float.IsNaN(availableUnits)
                || baseFontSize <= 0)
            {
                return currentlyCompact;
            }

            double decision = heightAtBase + StatusHeadroomUnits(baseFontSize);
            int trial = currentlyCompact ? FullModeReturnFontSize : FullModeMinFontSize;
            return decision * (trial / (double)baseFontSize) > availableUnits + FitToleranceUnits;
        }

        /// <summary>
        /// Largest font size in [minFontSize, baseFontSize] whose text still
        /// fits <paramref name="availableUnits"/>. Unknown inputs (zero or
        /// NaN height/space) keep the base size — never guess a shrink.
        /// Precondition 1 &lt;= minFontSize &lt;= baseFontSize; a min above the
        /// base is clamped to the min (empty range — keep the text readable).
        /// </summary>
        public static int FontSize(float preferredHeightAtBase, float availableUnits,
            int baseFontSize, int minFontSize)
        {
            if (baseFontSize <= 0)
                return minFontSize;
            if (preferredHeightAtBase <= 0f || float.IsNaN(preferredHeightAtBase)
                || availableUnits <= 0f || float.IsNaN(availableUnits))
            {
                return baseFontSize;
            }

            double scale = availableUnits / preferredHeightAtBase;
            if (scale >= 1.0)
                return baseFontSize;

            int fitted = (int)Math.Floor(baseFontSize * scale);
            if (fitted < minFontSize)
                return minFontSize;
            if (fitted > baseFontSize)
                return baseFontSize;
            return fitted;
        }

        /// <summary>
        /// Card font from the height measured at the BASE size, reserving the
        /// transient status line exactly as <see cref="DecideMode"/> does.
        /// The fit must never see a measurement taken at an already-fitted
        /// size: the text height scales with the font, so feeding the live
        /// preferredHeight back in flips the font between the floor and the
        /// base every refresh (live 2026-09-22, uiScale 1.59: 9/11 at 1 Hz,
        /// "текст пляшет"). The reservation also keeps the font constant while
        /// the 3-second status appears and expires. Unknown height keeps the
        /// base size — never guess a shrink.
        /// </summary>
        public static int CardFontSize(float heightAtBase, float availableUnits,
            int baseFontSize, int minFontSize)
        {
            if (baseFontSize <= 0)
                return minFontSize;
            if (heightAtBase <= 0f || float.IsNaN(heightAtBase))
                return baseFontSize;
            return FontSize(heightAtBase + StatusHeadroomUnits(baseFontSize),
                availableUnits, baseFontSize, minFontSize);
        }

        /// <summary>Readability floor for the mode: the compact one-liners may
        /// go to the absolute floor, the full F-Tafel blocks keep the higher
        /// full-mode minimum. Pure so the mode-to-floor wiring is unit-tested
        /// (a swapped ternary used to be invisible to the offline suite).</summary>
        public static int MinFontFor(bool compact)
        {
            return compact ? MinFontSize : FullModeMinFontSize;
        }

        /// <summary>Base-size height of the form actually rendered: the fit
        /// must consume the COMPACT height while the card is compact, or the
        /// ~1.5x-shorter one-liner card is pinned to the floor (2026-09-22,
        /// swarm review: the live compact card fits at 11 but was fitted at 8).
        /// Returns 0 when the requested form was never measured — the caller
        /// keeps the current font instead of guessing.</summary>
        public static float RenderedHeightAtBase(bool compact, float fullHeightAtBase,
            float compactHeightAtBase)
        {
            return compact ? compactHeightAtBase : fullHeightAtBase;
        }
    }
}
