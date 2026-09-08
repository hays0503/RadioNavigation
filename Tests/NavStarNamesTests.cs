using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class NavStarNamesTests
    {
        [Fact]
        public void TryGetName_KnownHip_ReturnsName()
        {
            string name;

            Assert.True(NavStarNames.TryGetName(677, out name));
            Assert.Equal("Alpheratz", name);
        }

        [Fact]
        public void TryGetName_UnknownHip_False()
        {
            string name;

            Assert.False(NavStarNames.TryGetName(-1, out name));
            Assert.Null(name);
        }

        [Fact]
        public void Names_NonEmpty()
        {
            Assert.NotEmpty(NavStarNames.Names);
        }
    }
}
