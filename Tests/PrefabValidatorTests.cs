using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class PrefabValidatorTests
    {
        [Fact]
        public void FindMissing_AllPresent_ReturnsEmpty()
        {
            var present = new HashSet<string>(PrefabValidator.RequiredChildren);
            var missing = PrefabValidator.FindMissing(present.Contains);

            Assert.Empty(missing);
        }

        [Fact]
        public void FindMissing_AbsentPath_IsReported()
        {
            var missing = PrefabValidator.FindMissing(_ => false);

            Assert.Equal(PrefabValidator.RequiredChildren.Length, missing.Count);
            Assert.Contains(PrefabNames.Ground, missing);
        }

        [Fact]
        public void FindMissing_Partial_OnlyAbsentReturned()
        {
            var missing = PrefabValidator.FindMissing(
                path => path == PrefabNames.Cursor || path == PrefabNames.Toggle);

            Assert.DoesNotContain(PrefabNames.Cursor, missing);
            Assert.DoesNotContain(PrefabNames.Toggle, missing);
            Assert.Contains(PrefabNames.Ground, missing);
        }

        [Fact]
        public void RequiredChildren_AreNonEmptyAndDistinct()
        {
            Assert.NotEmpty(PrefabValidator.RequiredChildren);
            var set = new HashSet<string>(PrefabValidator.RequiredChildren);
            Assert.Equal(PrefabValidator.RequiredChildren.Length, set.Count);
        }
    }
}
