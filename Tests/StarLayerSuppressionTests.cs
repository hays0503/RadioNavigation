using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the game-star suppression decision: the game's decorative star
    /// sphere may only be hidden when the mod's own catalog sky is actually
    /// drawn, so a shader/loading failure can never leave an empty night sky.
    /// </summary>
    public sealed class StarLayerSuppressionTests
    {
        [Fact]
        public void Hide_RequiresBothThePreferenceAndOurSky()
        {
            Assert.True(StarLayerSuppression.ShouldHide(true, true));
            Assert.False(StarLayerSuppression.ShouldHide(true, false));
            Assert.False(StarLayerSuppression.ShouldHide(false, true));
            Assert.False(StarLayerSuppression.ShouldHide(false, false));
        }

        [Fact]
        public void Hide_FailsSafeWhenOurSkyIsNotActive()
        {
            // The whole point of the guard: never blank the sky on our failure.
            Assert.False(StarLayerSuppression.ShouldHide(true, false));
        }

        [Fact]
        public void GlobalName_MatchesTheTodProperty()
        {
            Assert.Equal("TOD_StarVisibility", StarLayerSuppression.GlobalName);
        }
    }
}
