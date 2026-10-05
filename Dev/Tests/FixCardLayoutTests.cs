using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Card auto-font (2026-09-22): the F-Tafel card must fit the usable
    /// height of the fix column, otherwise the text runs off the bottom edge
    /// (three observations, stacked layout). The text height scales linearly
    /// with the font size, so one measurement at the base size decides.
    /// </summary>
    public sealed class FixCardLayoutTests
    {
        [Fact]
        public void FitsAtBase_KeepsBaseSize()
        {
            Assert.Equal(11, FixCardLayout.FontSize(400f, 410f, 11, 8));
        }

        [Fact]
        public void ExactlyFits_KeepsBaseSize()
        {
            Assert.Equal(11, FixCardLayout.FontSize(410f, 410f, 11, 8));
        }

        [Fact]
        public void SlightOverflow_ShrinksOneStep()
        {
            // 11 * 410/430 = 10.49 -> 10.
            Assert.Equal(10, FixCardLayout.FontSize(430f, 410f, 11, 8));
        }

        [Fact]
        public void Overflow_ShrinksToTheFloor()
        {
            // 11 * 410/514 = 8.77 -> 8, the floor.
            Assert.Equal(8, FixCardLayout.FontSize(514f, 410f, 11, 8));
        }

        [Fact]
        public void NeverBelowTheFloor()
        {
            Assert.Equal(8, FixCardLayout.FontSize(2000f, 410f, 11, 8));
        }

        [Fact]
        public void UnknownMeasurements_KeepBase()
        {
            // Never guess a shrink when the measurement is missing.
            Assert.Equal(11, FixCardLayout.FontSize(0f, 410f, 11, 8));
            Assert.Equal(11, FixCardLayout.FontSize(514f, 0f, 11, 8));
            Assert.Equal(11, FixCardLayout.FontSize(float.NaN, 410f, 11, 8));
            Assert.Equal(11, FixCardLayout.FontSize(514f, float.NaN, 11, 8));
        }

        [Fact]
        public void NoBaseSize_FallsBackToTheFloor()
        {
            Assert.Equal(8, FixCardLayout.FontSize(514f, 410f, 0, 8));
        }

        // ---- Base-size measurement + status reserve (2026-09-22) ----------

        [Fact]
        public void CardFontSize_ReservesTheStatusLine()
        {
            // 400 + 11 = 411 > 410 -> one step down; without the reserve the
            // same card would stay at the base size.
            Assert.Equal(10, FixCardLayout.CardFontSize(400f, 410f, 11, 8));
            Assert.Equal(11, FixCardLayout.FontSize(400f, 410f, 11, 8));
            // A short card still fits at the base size.
            Assert.Equal(11, FixCardLayout.CardFontSize(200f, 410f, 11, 8));
        }

        [Fact]
        public void CardFontSize_UnknownInputs_KeepBase()
        {
            Assert.Equal(11, FixCardLayout.CardFontSize(0f, 410f, 11, 8));
            Assert.Equal(11, FixCardLayout.CardFontSize(float.NaN, 410f, 11, 8));
            Assert.Equal(11, FixCardLayout.CardFontSize(400f, 0f, 11, 8));
        }

        [Fact]
        public void CardFontSize_AlwaysFitsWithTheReservedLine()
        {
            const float height = 243.68f;
            for (int available = 120; available <= 420; available += 7)
            {
                int font = FixCardLayout.CardFontSize(height, available, 11, 8);
                float needed = (height + FixCardLayout.StatusHeadroomUnits(11))
                    * font / 11f;
                Assert.True(needed <= available + FixCardLayout.FitToleranceUnits
                    || font == FixCardLayout.MinFontSize,
                    "font " + font + " needs " + needed.ToString("F1")
                    + " units, column has " + available);
            }
        }

        [Fact]
        public void Regression_LiveMeasurementFeedbackWouldDance()
        {
            // Live 2026-09-22 at uiScale 1.59: column 209.62 units; the full
            // card measures 199.37 at font 9 and 243.68 at font 11. The old
            // fit read the live preferredHeight and flipped 9 -> 11 -> 9 at
            // 1 Hz ("текст пляшет").
            const float available = 209.62f;
            const float heightAtBase = 243.68f;
            int first = FixCardLayout.FontSize(heightAtBase, available, 11, 8);
            Assert.Equal(9, first);
            float heightAtFitted = heightAtBase * first / 11f;
            Assert.Equal(11,
                FixCardLayout.FontSize(heightAtFitted, available, 11, 8));

            // The fixed call site always feeds the cached base measurement:
            // the fit result is independent of the font currently applied, so
            // no feedback loop can start.
            Assert.Equal(9,
                FixCardLayout.CardFontSize(heightAtBase, available, 11, 8));

            // Live compact pair (2026-09-22, swarm): the compact card at
            // font 11 measures 248.43 units — it fits the 1440p column
            // (322.83) at the full size and only shrinks to the floor on the
            // 1080p one (209.62).
            Assert.Equal(11, FixCardLayout.CardFontSize(248.4277f, 322.83f, 11, 8));
            Assert.Equal(8, FixCardLayout.CardFontSize(248.4277f, 209.62f, 11, 8));
        }

        [Fact]
        public void RenderedHeightAtBase_PicksTheFormActuallyDrawn()
        {
            Assert.Equal(243.68f,
                FixCardLayout.RenderedHeightAtBase(false, 243.68f, 248.43f));
            Assert.Equal(248.43f,
                FixCardLayout.RenderedHeightAtBase(true, 243.68f, 248.43f));
            // A form that was never measured stays zero — the caller keeps the
            // current font instead of fitting the wrong form.
            Assert.Equal(0f, FixCardLayout.RenderedHeightAtBase(true, 243.68f, 0f));
        }

        [Fact]
        public void MinFontFor_MatchesTheMode()
        {
            Assert.Equal(8, FixCardLayout.MinFontFor(true));
            Assert.Equal(9, FixCardLayout.MinFontFor(false));
        }

        [Fact]
        public void Constants_OrderedForTheHysteresis()
        {
            // The hysteresis band must be non-empty: a compact card can only
            // return to full when the exit trial font is above the entry one.
            Assert.True(FixCardLayout.MinFontSize <= FixCardLayout.FullModeMinFontSize);
            Assert.True(FixCardLayout.FullModeMinFontSize < FixCardLayout.FullModeReturnFontSize);
        }

        [Fact]
        public void FontSize_BaseNotPositive_FallsBackToTheFloor()
        {
            Assert.Equal(8, FixCardLayout.FontSize(514f, 410f, 0, 8));
            Assert.Equal(8, FixCardLayout.FontSize(514f, 0f, 0, 8));
            Assert.Equal(8, FixCardLayout.FontSize(0f, 410f, 0, 8));
        }

        [Fact]
        public void FontSize_MinAboveBase_ClampsToMin()
        {
            // Precondition violation: the range is empty — keep the text
            // readable at the requested floor rather than the base.
            Assert.Equal(9, FixCardLayout.FontSize(514f, 410f, 8, 9));
        }

        [Fact]
        public void CardFontSize_BaseNotPositive_FallsBackToTheFloor()
        {
            Assert.Equal(8, FixCardLayout.CardFontSize(400f, 410f, 0, 8));
        }

        // ---- Hysteretic mode decision (2026-09-22) ------------------------

        [Fact]
        public void UnknownInputs_PreserveMode()
        {
            Assert.True(FixCardLayout.DecideMode(true, 0f, 339f, 11));
            Assert.False(FixCardLayout.DecideMode(false, 439f, 0f, 11));
            Assert.True(FixCardLayout.DecideMode(true, float.NaN, 339f, 11));
            Assert.False(FixCardLayout.DecideMode(false, 439f, 339f, 0));
        }

        [Fact]
        public void FullFitsAt9_StaysFull()
        {
            Assert.False(FixCardLayout.DecideMode(false, 400f, 410f, 11));
        }

        [Fact]
        public void FullNotFitAt9_EntersCompact()
        {
            // D = 439 + 11 = 450; 450*9/11 = 368.2 > 339.5.
            Assert.True(FixCardLayout.DecideMode(false, 439f, 339f, 11));
        }

        [Fact]
        public void Hysteresis_CompactStaysInBand()
        {
            // Fits at 9 (368.2 <= 380.5) but not at 10 (409.1 > 380.5).
            Assert.True(FixCardLayout.DecideMode(true, 439f, 380f, 11));
        }

        [Fact]
        public void Hysteresis_FullStaysInBand()
        {
            // Asymmetric entry: a full card in the same band stays full.
            Assert.False(FixCardLayout.DecideMode(false, 439f, 380f, 11));
        }

        [Fact]
        public void Hysteresis_CompactReturnsFullAtExit()
        {
            // Exit edge with the reserved line: D*10/11 = 450*10/11 = 409.1.
            Assert.False(FixCardLayout.DecideMode(true, 439f, 410f, 11));
        }

        [Fact]
        public void StatusHeadroom_Reserved()
        {
            // Without the reserve 359.2 <= 361.5 would stay full; with the
            // reserved line (D=450) 368.2 > 361.5 -> compact.
            Assert.True(FixCardLayout.DecideMode(false, 439f, 361f, 11));
        }

        [Fact]
        public void StatusHeadroomUnits_ScalesWithBase()
        {
            Assert.Equal(11f, FixCardLayout.StatusHeadroomUnits(11));
            Assert.Equal(8f, FixCardLayout.StatusHeadroomUnits(8));
            Assert.Equal(0f, FixCardLayout.StatusHeadroomUnits(0));
            Assert.Equal(0f, FixCardLayout.StatusHeadroomUnits(-1));
        }

        [Fact]
        public void AntiOscillation_ModeIsAFixedPoint_AndSweepsMonotonically()
        {
            // The decision never sees the transient status line (the worksheet
            // measures the stable card), so for fixed inputs it is a fixed
            // point. A monotone sweep of the available height must therefore
            // flip the mode exactly once in each direction — the property that
            // stops the visible flapping when the UI scale changes.
            const float height = 439f;
            bool mode = false;
            int intoCompact = 0;
            int intoFull = 0;

            for (int i = 2100; i >= 0; i--)
            {
                bool next = FixCardLayout.DecideMode(mode, height, i, 11);
                if (next != mode)
                    intoCompact++;
                mode = next;
            }

            for (int i = 0; i <= 2100; i++)
            {
                bool next = FixCardLayout.DecideMode(mode, height, i, 11);
                if (next != mode)
                    intoFull++;
                mode = next;
            }

            Assert.Equal(1, intoCompact);
            Assert.Equal(1, intoFull);
        }

        [Fact]
        public void Regression_SingleThresholdWouldFlap()
        {
            // The old single-threshold predicate flips when the transient
            // status line appears/disappears; the hysteretic one does not.
            const float available = 359.6f;
            const float height = 438.7f;
            bool oldFull = height * 9f / 11f <= available + 0.5f;
            bool oldWithStatus = (height + 10.7f) * 9f / 11f <= available + 0.5f;
            Assert.NotEqual(oldFull, oldWithStatus);

            bool mode = FixCardLayout.DecideMode(false, height, available, 11);
            bool afterStatus = FixCardLayout.DecideMode(mode, height + 10.7f, available, 11);
            Assert.Equal(mode, afterStatus);
        }
    }
}
