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

        [Fact]
        public void StarDomeMaterial_MatchesTheDomeTokenCaseInsensitively()
        {
            Assert.True(StarLayerSuppression.IsStarDomeMaterial("Stars"));
            Assert.True(StarLayerSuppression.IsStarDomeMaterial("Material:Stars"));
            Assert.True(StarLayerSuppression.IsStarDomeMaterial("unistorm stars"));
            Assert.True(StarLayerSuppression.IsStarDomeMaterial("STARS_02"));
            Assert.False(StarLayerSuppression.IsStarDomeMaterial("Star4"));
            Assert.False(StarLayerSuppression.IsStarDomeMaterial("RadioNavStarField"));
            Assert.False(StarLayerSuppression.IsStarDomeMaterial(null));
            Assert.False(StarLayerSuppression.IsStarDomeMaterial(""));
        }

        [Fact]
        public void StarDomeExtent_RejectsFlaresAndAcceptsTheDome()
        {
            // Live dome extents ~4417 x 6130 x 6142; flares are sub-metre.
            Assert.True(StarLayerSuppression.HasDomeExtent(4416.8f, 6130.27f, 6141.74f));
            Assert.True(StarLayerSuppression.HasDomeExtent(1001f, 0f, 0f));
            Assert.False(StarLayerSuppression.HasDomeExtent(1000f, 1000f, 1000f));
            Assert.False(StarLayerSuppression.HasDomeExtent(0.4f, 0.3f, 0.2f));
        }

        [Fact]
        public void StarDomeCandidate_RequiresActiveMaterialAndExtent()
        {
            Assert.True(StarLayerSuppression.IsStarDomeCandidate(
                true, "Stars", 4416.8f, 6130.27f, 6141.74f));

            // Inactive (the `UniStorm Stars` renderer has zero bounds).
            Assert.False(StarLayerSuppression.IsStarDomeCandidate(
                false, "Stars", 4416.8f, 6130.27f, 6141.74f));
            // Small flare with a star-ish material name.
            Assert.False(StarLayerSuppression.IsStarDomeCandidate(
                true, "Star4", 0.4f, 0.3f, 0.2f));
            // Star material but not dome sized.
            Assert.False(StarLayerSuppression.IsStarDomeCandidate(
                true, "Stars", 2f, 2f, 2f));
            // Dome sized but not a star material.
            Assert.False(StarLayerSuppression.IsStarDomeCandidate(
                true, "Moon", 4416.8f, 6130.27f, 6141.74f));
        }
    }
}
