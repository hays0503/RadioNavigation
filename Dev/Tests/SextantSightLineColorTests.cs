using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 sight-line colour: black by day, green luminous paint at night.</summary>
    public sealed class SextantSightLineColorTests
    {
        [Fact]
        public void Day_IsThinBlack()
        {
            Color day = SextantSightLineColor.For(0f);
            Assert.Equal(0f, day.r, 6);
            Assert.Equal(0f, day.g, 6);
            Assert.Equal(0f, day.b, 6);
            Assert.InRange(day.a, 0.5f, 0.6f);
        }

        [Fact]
        public void Night_IsGreenLuminous()
        {
            Color night = SextantSightLineColor.For(1f);
            Assert.InRange(night.g, 0.95f, 1.0f);
            Assert.True(night.g > night.r, "green must dominate at night");
            Assert.True(night.g > night.b, "green must dominate at night");
            Assert.InRange(night.a, 0.8f, 0.9f);
        }

        [Fact]
        public void Midpoint_IsHalfway()
        {
            Color mid = SextantSightLineColor.For(0.5f);
            Assert.InRange(mid.r, 0.27f, 0.28f);
            Assert.InRange(mid.g, 0.49f, 0.51f);
            Assert.InRange(mid.b, 0.30f, 0.32f);
            Assert.InRange(mid.a, 0.69f, 0.71f);
        }

        [Fact]
        public void Factor_IsClamped()
        {
            Assert.Equal(SextantSightLineColor.For(0f), SextantSightLineColor.For(-3f));
            Assert.Equal(SextantSightLineColor.For(1f), SextantSightLineColor.For(9f));
        }
    }
}
