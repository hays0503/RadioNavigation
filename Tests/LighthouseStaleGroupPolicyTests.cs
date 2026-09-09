using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Stale-group cleanup decision: only the mod's own "Lighthouse "
    /// namespace is eligible, only when the group is not part of the
    /// current fleet. Game and player groups without the prefix are never
    /// touched.
    /// </summary>
    public class LighthouseStaleGroupPolicyTests
    {
        private static readonly string[] Fleet =
        {
            "Lighthouse Wangerooge",
            "Lighthouse B1054 Leuchtturm Norderney"
        };

        [Fact]
        public void FleetGroup_IsKept()
        {
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Lighthouse Wangerooge", new HashSet<string>(Fleet)));
        }

        [Fact]
        public void ExcludedBeaconGroup_IsDestroyed()
        {
            Assert.True(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Lighthouse B9999 Leuchtturm Auswasser", new HashSet<string>(Fleet)));
        }

        [Fact]
        public void PrefixedButNotInFleet_IsDestroyed()
        {
            Assert.True(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Lighthouse SomethingElse", new HashSet<string>(Fleet)));
        }

        [Fact]
        public void ForeignGroup_IsKept()
        {
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Convoy Alpha", new HashSet<string>(Fleet)));
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Lighthouses", new HashSet<string>(Fleet)));
        }

        [Fact]
        public void DegenerateInputs_AreKept()
        {
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy(null, new HashSet<string>(Fleet)));
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy("", new HashSet<string>(Fleet)));
            Assert.False(LighthouseStaleGroupPolicy.ShouldDestroy("Lighthouse X", null));
        }

        [Fact]
        public void EmptyFleet_AnyPrefixedGroup_IsStale()
        {
            // With no fleet at all every mod-prefixed group is a leftover.
            Assert.True(LighthouseStaleGroupPolicy.ShouldDestroy(
                "Lighthouse X", new HashSet<string>()));
        }
    }
}
