using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SceneEventHandlerPurgeTests
    {
        private sealed class Target
        {
            public int MatchCount;
            public int OtherCount;

            public void OnSceneStart(int value) { MatchCount++; }
            public void Other(int value) { OtherCount++; }
        }

        [Fact]
        public void RemoveAll_DropsMatching_KeepsOthers()
        {
            var target = new Target();
            Action<int> handlers = target.OnSceneStart;
            handlers += target.Other;

            Delegate kept = SceneEventHandlerPurge.RemoveAll(
                handlers, typeof(Target).FullName, "OnSceneStart");

            var remaining = (Action<int>)kept;
            remaining(1);

            Assert.Equal(0, target.MatchCount);
            Assert.Equal(1, target.OtherCount);
        }

        [Fact]
        public void RemoveAll_AllMatch_ReturnsNull()
        {
            var target = new Target();
            Action<int> handlers = target.OnSceneStart;
            handlers += target.OnSceneStart;

            Delegate kept = SceneEventHandlerPurge.RemoveAll(
                handlers, typeof(Target).FullName, "OnSceneStart");

            Assert.Null(kept);
        }

        [Fact]
        public void RemoveAll_Null_ReturnsNull()
        {
            Assert.Null(SceneEventHandlerPurge.RemoveAll(null, "X", "Y"));
        }

        [Fact]
        public void RemoveAll_NoMatch_KeepsEverything()
        {
            var target = new Target();
            Action<int> handlers = target.Other;

            Delegate kept = SceneEventHandlerPurge.RemoveAll(
                handlers, typeof(Target).FullName, "OnSceneStart");

            ((Action<int>)kept)(1);
            Assert.Equal(1, target.OtherCount);
        }
    }
}
