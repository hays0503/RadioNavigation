using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the B-V → RGB map that the point-star mesh and the Sternspreiz
    /// streak field now share (one source of truth).
    /// </summary>
    public sealed class StarColorTests
    {
        private static void FromBv(float bv, out float r, out float g, out float b)
        {
            StarColor.FromBv(bv, out r, out g, out b);
        }

        [Fact]
        public void BlueEnd_UsesFixedBlueTint()
        {
            FromBv(-0.5f, out float r, out float g, out float b);
            Assert.Equal(0.6f, r, 6);
            Assert.Equal(0.7f, g, 6);
            Assert.Equal(1.0f, b, 6);
        }

        [Fact]
        public void ZeroBv_IsWhite()
        {
            FromBv(0f, out float r, out float g, out float b);
            Assert.Equal(1f, r, 6);
            Assert.Equal(1f, g, 6);
            Assert.Equal(1f, b, 6);
        }

        [Fact]
        public void DeepRed_IsClampedAtTheTopSegment()
        {
            FromBv(2.5f, out float r, out float g, out float b);
            Assert.Equal(1f, r, 6);
            Assert.Equal(0.3f, g, 6);
            Assert.Equal(0.1f, b, 6);
        }

        [Fact]
        public void MiddleSegments_AreContinuousAtTheirSeams()
        {
            FromBv(0.5f, out float r50, out float g50, out float b50);
            FromBv(0.4999f, out float r49, out float g49, out float b49);
            Assert.Equal(r50, r49, 3);
            Assert.Equal(g50, g49, 3);
            Assert.Equal(b50, b49, 3);

            FromBv(1.0f, out float r10, out float g10, out float b10);
            FromBv(0.9999f, out float r09, out float g09, out float b09);
            Assert.Equal(r10, r09, 3);
            Assert.Equal(g10, g09, 3);
            Assert.Equal(b10, b09, 3);
        }

        [Fact]
        public void WarmSegments_LoseBlueMonotonically()
        {
            FromBv(0.5f, out _, out _, out float bLow);
            FromBv(0.75f, out _, out _, out float bMid);
            FromBv(1.4f, out _, out _, out float bHigh);

            Assert.True(bMid < bLow);
            Assert.True(bHigh < bMid);
        }

        [Fact]
        public void Channels_StayInRange()
        {
            for (float bv = -1f; bv <= 3f; bv += 0.05f)
            {
                FromBv(bv, out float r, out float g, out float b);
                Assert.InRange(r, 0f, 1f);
                Assert.InRange(g, 0f, 1f);
                Assert.InRange(b, 0f, 1f);
            }
        }
    }
}
