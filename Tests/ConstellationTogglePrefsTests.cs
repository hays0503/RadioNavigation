using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Persistence codec for the constellation lines toggle
    /// (STAR_SKY_PLAN §13.6): default ON when no pref exists, roundtrip via
    /// PlayerPrefs stub, key constant stability.
    /// </summary>
    public sealed class ConstellationTogglePrefsTests
    {
        [Fact]
        public void Key_HasStableName()
        {
            Assert.Equal("RadioNav.ConstellationsEnabled", ConstellationTogglePrefs.Key);
        }

        [Fact]
        public void Load_NoPrefSaved_DefaultsToOn()
        {
            Assert.True(ConstellationTogglePrefs.Load());
        }

        [Fact]
        public void SaveOff_LoadReturnsFalse()
        {
            ConstellationTogglePrefs.Save(false);
            Assert.False(ConstellationTogglePrefs.Load());
        }

        [Fact]
        public void SaveOn_LoadReturnsTrue()
        {
            ConstellationTogglePrefs.Save(false);
            ConstellationTogglePrefs.Save(true);
            Assert.True(ConstellationTogglePrefs.Load());
        }
    }
}
