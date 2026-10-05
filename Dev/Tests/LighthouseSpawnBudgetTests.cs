using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class LighthouseSpawnBudgetTests
    {
        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 1)]
        [InlineData(false, true, 1)] // F222: a pending grant occupies a slot
        [InlineData(true, true, 1)]
        public void Consumed_Cases(bool spawned, bool wantsSpawn, int expected)
        {
            Assert.Equal(expected, LighthouseSpawnBudget.Consumed(spawned, wantsSpawn));
        }

        [Fact]
        public void CanGrant_BelowCap_True()
        {
            Assert.True(LighthouseSpawnBudget.CanGrant(0, 64));
            Assert.True(LighthouseSpawnBudget.CanGrant(63, 64));
        }

        [Fact]
        public void CanGrant_AtOrAboveCap_False()
        {
            Assert.False(LighthouseSpawnBudget.CanGrant(64, 64));
            Assert.False(LighthouseSpawnBudget.CanGrant(65, 64));
        }
    }
}
