using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Sticky body selection (2026-11-04): a confirmed body survives leaving
    /// the cone until O / the sextant reset / another confirmation; the
    /// "вне прицела" suffix marks the out-of-cone state.
    /// </summary>
    public sealed class BodySelectionTests
    {
        [Fact]
        public void Fresh_HasNoSelection()
        {
            var selection = new BodySelection();
            Assert.True(selection.Selected.IsNone);
            Assert.False(selection.HasSelection);
        }

        [Fact]
        public void Select_KeepsTheBody()
        {
            var selection = new BodySelection();
            selection.Select(CelestialBody.Star(3697, "Capella"));

            Assert.True(selection.HasSelection);
            Assert.Equal(CelestialBodyKind.Star, selection.Selected.Kind);
            Assert.Equal(3697, selection.Selected.Hip);
            Assert.Equal("Capella", selection.Selected.DisplayName);
        }

        [Fact]
        public void Select_None_DoesNotClearTheSelection()
        {
            // A failed hold (None) must never drop the previous selection.
            var selection = new BodySelection();
            selection.Select(CelestialBody.Moon());
            selection.Select(CelestialBody.None());

            Assert.True(selection.HasSelection);
            Assert.Equal(CelestialBodyKind.Moon, selection.Selected.Kind);
        }

        [Fact]
        public void Select_AnotherBody_ReplacesTheSelection()
        {
            var selection = new BodySelection();
            selection.Select(CelestialBody.Moon());
            selection.Select(CelestialBody.Sun());

            Assert.Equal(CelestialBodyKind.Sun, selection.Selected.Kind);
        }

        [Fact]
        public void Clear_DropsTheSelection()
        {
            var selection = new BodySelection();
            selection.Select(CelestialBody.Sun());
            selection.Clear();

            Assert.False(selection.HasSelection);
            Assert.True(selection.Selected.IsNone);
        }

        [Fact]
        public void DisplayName_PlainInCone_SuffixedOutside()
        {
            Assert.Equal("Capella", BodySelection.DisplayName("Capella", true));
            Assert.Equal("Capella" + BodySelection.OutOfConeSuffix,
                BodySelection.DisplayName("Capella", false));
        }

        [Fact]
        public void DisplayName_EmptyStaysEmpty()
        {
            Assert.Equal("", BodySelection.DisplayName("", false));
            Assert.Equal("", BodySelection.DisplayName(null, true));
        }

        [Fact]
        public void OutOfConeSuffix_IsTheExpectedPhrase()
        {
            Assert.Equal(" \u00B7 \u0432\u043D\u0435 \u043F\u0440\u0438\u0446\u0435\u043B\u0430",
                BodySelection.OutOfConeSuffix);
        }

        [Fact]
        public void AdvanceLost_CountsWhileOutOfTheCone()
        {
            float lost = BodySelection.AdvanceLost(0f, false, 0.2f);
            Assert.Equal(0.2f, lost, 5);
            lost = BodySelection.AdvanceLost(lost, false, 0.2f);
            Assert.Equal(0.4f, lost, 5);
        }

        [Fact]
        public void AdvanceLost_ResetsWhenBackInTheCone()
        {
            Assert.Equal(0f, BodySelection.AdvanceLost(3f, true, 0.2f));
        }

        [Fact]
        public void AdvanceLost_Guards()
        {
            Assert.Equal(0.5f, BodySelection.AdvanceLost(0.5f, false, 0f), 5);
            Assert.Equal(0.5f, BodySelection.AdvanceLost(0.5f, false, float.NaN), 5);
            // NaN/negative lost starts from zero and keeps counting.
            Assert.Equal(0.2f, BodySelection.AdvanceLost(float.NaN, false, 0.2f), 5);
            Assert.Equal(0.2f, BodySelection.AdvanceLost(-1f, false, 0.2f), 5);
        }

        [Fact]
        public void TimedOut_AfterTheTimeoutWindow()
        {
            Assert.False(BodySelection.TimedOut(0f));
            Assert.False(BodySelection.TimedOut(BodySelection.LostTimeoutSeconds - 0.01f));
            Assert.True(BodySelection.TimedOut(BodySelection.LostTimeoutSeconds));
            Assert.True(BodySelection.TimedOut(BodySelection.LostTimeoutSeconds + 1f));
            Assert.False(BodySelection.TimedOut(float.NaN));
        }

        [Fact]
        public void LostTimeout_IsAShortHoldWindow()
        {
            // The drum is set within a few seconds (author 2026-11-04).
            Assert.True(BodySelection.LostTimeoutSeconds >= 3f);
            Assert.True(BodySelection.LostTimeoutSeconds <= 15f);
        }
    }
}
