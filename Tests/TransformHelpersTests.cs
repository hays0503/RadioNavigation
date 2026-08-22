using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Behavior of the shared FindChildRecursive helper (consolidated from
    /// LighthouseBeamGlow and LighthousePlacer — identical recursion in both).
    /// </summary>
    public sealed class TransformHelpersTests
    {
        [Fact]
        public void FindsChildByNameAtDepth()
        {
            Transform root = new Transform("root");
            Transform a = root.AddChild("A");
            Transform b = a.AddChild("B");
            Transform target = b.AddChild("Target");

            Assert.Same(target, TransformHelpers.FindChildRecursive(root, "Target"));
        }

        [Fact]
        public void RootItselfMatches()
        {
            Transform root = new Transform("root");
            Assert.Same(root, TransformHelpers.FindChildRecursive(root, "root"));
        }

        [Fact]
        public void Missing_ReturnsNull()
        {
            Transform root = new Transform("root");
            root.AddChild("A");
            Assert.Null(TransformHelpers.FindChildRecursive(root, "Missing"));
        }

        [Fact]
        public void NullRoot_ReturnsNull()
        {
            Assert.Null(TransformHelpers.FindChildRecursive(null, "Anything"));
        }

        [Fact]
        public void EmptyName_ReturnsNull()
        {
            Transform root = new Transform("root");
            Assert.Null(TransformHelpers.FindChildRecursive(root, null));
            Assert.Null(TransformHelpers.FindChildRecursive(root, ""));
        }

        [Fact]
        public void FirstMatchWins_OnDuplicates()
        {
            Transform root = new Transform("root");
            Transform first = root.AddChild("Dup");
            root.AddChild("Dup");

            Assert.Same(first, TransformHelpers.FindChildRecursive(root, "Dup"));
        }
    }
}
