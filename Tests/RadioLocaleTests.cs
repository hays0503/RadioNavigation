using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class RadioLocaleTests
    {
        private const string DefaultLabel = "T3PLL38 on-board radio receiver for very long and long waves";

        [Fact]
        public void MatchesLocaleKey_BareMatch()
        {
            Assert.True(RadioLocale.MatchesLocaleKey("Radio/OpenT3PLL38"));
        }

        [Fact]
        public void MatchesLocaleKey_PrefixedMatch()
        {
            Assert.True(RadioLocale.MatchesLocaleKey("Stories/Radio/OpenT3PLL38"));
        }

        [Fact]
        public void MatchesLocaleKey_OtherKey_NoMatch()
        {
            Assert.False(RadioLocale.MatchesLocaleKey("Radio/LeaveStation"));
            Assert.False(RadioLocale.MatchesLocaleKey(""));
            Assert.False(RadioLocale.MatchesLocaleKey(null));
        }

        [Fact]
        public void GetLabel_Russian_NotDefault()
        {
            string label = RadioLocale.GetLabel("russian");

            Assert.Contains("T3PLL38", label);
            Assert.NotEqual(DefaultLabel, label);
        }

        [Fact]
        public void GetLabel_Unknown_ReturnsDefault()
        {
            Assert.Equal(DefaultLabel, RadioLocale.GetLabel("klingon"));
            Assert.Equal(DefaultLabel, RadioLocale.GetLabel(null));
        }

        [Fact]
        public void GetLocalizedLabel_Offline_ReturnsDefault()
        {
            RadioLocale.Reset();

            Assert.Equal(DefaultLabel, RadioLocale.GetLocalizedLabel());
        }
    }
}
