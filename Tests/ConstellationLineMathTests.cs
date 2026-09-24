using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 constellation line math: horizon clip and minimum width.</summary>
    public sealed class ConstellationLineMathTests
    {
        [Fact]
        public void Clip_WholeSegmentAboveHorizon_IsUntouched()
        {
            Assert.True(ConstellationLineMath.ClipToHorizon(30.0, 10.0,
                out double t0, out double t1));
            Assert.Equal(0.0, t0, 9);
            Assert.Equal(1.0, t1, 9);
        }

        [Fact]
        public void Clip_WholeSegmentBelowHorizon_IsDropped()
        {
            Assert.False(ConstellationLineMath.ClipToHorizon(-5.0, -1.0, out _, out _));
            Assert.False(ConstellationLineMath.ClipToHorizon(0.0, -1.0, out _, out _));
        }

        [Fact]
        public void Clip_CrossingSegment_EndsAtTheHorizon()
        {
            // Rising: alt 0 at t = 0.25.
            Assert.True(ConstellationLineMath.ClipToHorizon(-5.0, 15.0,
                out double t0, out double t1));
            Assert.InRange(t0, 0.249, 0.251);
            Assert.Equal(1.0, t1, 9);

            // Setting: alt 0 at t = 0.75.
            Assert.True(ConstellationLineMath.ClipToHorizon(30.0, -10.0,
                out double t0b, out double t1b));
            Assert.Equal(0.0, t0b, 9);
            Assert.InRange(t1b, 0.749, 0.751);
        }

        [Fact]
        public void HorizonFade_RampsFromZeroToFull()
        {
            Assert.Equal(0f, ConstellationLineMath.HorizonFade(-1.0, 3.0));
            Assert.Equal(0f, ConstellationLineMath.HorizonFade(0.0, 3.0));
            Assert.InRange(ConstellationLineMath.HorizonFade(1.5, 3.0), 0.49f, 0.51f);
            Assert.Equal(1f, ConstellationLineMath.HorizonFade(3.0, 3.0));
            Assert.Equal(1f, ConstellationLineMath.HorizonFade(45.0, 3.0));
        }

        [Fact]
        public void WorldWidth_UsesTheLargerOfAngularAndMinimumPixels()
        {
            // 2′ at 3900 m: 0.00227 rad * 3900 ≈ 8.9 m — far below a 1.5 px
            // floor in a 512 px / 65° ocular (~0.0077 rad/px → ~45 m/px).
            double angularRad = 2.0 / 60.0 * System.Math.PI / 180.0;
            float width = ConstellationLineMath.WorldWidth(angularRad, 3900.0,
                65.0 * System.Math.PI / 180.0, 512.0, 1.5);
            Assert.True(width > (float)(angularRad * 3900.0),
                "the pixel floor must win in the ocular");

            // Wide angular width wins on a high-resolution main view.
            float wide = ConstellationLineMath.WorldWidth(angularRad, 3900.0,
                65.0 * System.Math.PI / 180.0, 4000.0, 1.5);
            Assert.InRange(wide, (float)(angularRad * 3900.0) - 0.01f,
                (float)(angularRad * 3900.0) + 0.01f);
        }

        [Fact]
        public void WorldWidth_DegenerateInputs_FallBackToAngular()
        {
            double angularRad = 0.001;
            Assert.Equal((float)(angularRad * 100.0),
                ConstellationLineMath.WorldWidth(angularRad, 100.0, 0.0, 0.0, 1.5));
        }
    }
}
