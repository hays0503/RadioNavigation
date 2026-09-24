using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P2 lunar phase disc: phase angle, bright limb, terminator field.</summary>
    public sealed class MoonPhaseDiscTests
    {
        private const double Deg2Rad = Math.PI / 180.0;

        private static float Center(float[] field, int size)
        {
            return field[(size / 2) * size + size / 2];
        }

        [Fact]
        public void PhaseAngle_FullWhenOppositeTheSun_NewWhenConjunct()
        {
            double full = MoonPhaseDisc.PhaseAngle(0.0, 0.0, Math.PI, 0.0);
            Assert.InRange(full, 0.0, 0.001);

            double newMoon = MoonPhaseDisc.PhaseAngle(0.0, 0.0, 0.0, 0.0);
            Assert.InRange(newMoon, Math.PI - 0.001, Math.PI);
        }

        [Fact]
        public void IlluminatedFraction_MatchesCosineLaw()
        {
            Assert.InRange(MoonPhaseDisc.IlluminatedFraction(0.0), 0.999, 1.0);
            Assert.InRange(MoonPhaseDisc.IlluminatedFraction(Math.PI), 0.0, 0.001);
            Assert.InRange(MoonPhaseDisc.IlluminatedFraction(Math.PI / 2), 0.499, 0.501);
        }

        [Fact]
        public void BrightLimbAngle_PointsFromTheMoonToTheSun()
        {
            // Sun due east of the Moon (same dec, +90° RA): the bright limb is
            // at position angle 90° (east).
            double angle = MoonPhaseDisc.BrightLimbAngle(0.0, 0.0, -Math.PI / 2, 0.0);
            Assert.InRange(angle / Deg2Rad, 89.9, 90.1);

            // Sun due north: position angle 0.
            double north = MoonPhaseDisc.BrightLimbAngle(0.0, 0.5, 0.0, 0.0);
            Assert.InRange(north / Deg2Rad, -0.1, 0.1);
        }

        [Fact]
        public void AlphaField_FullMoon_IsLitAcrossTheDisc()
        {
            int size = 64;
            float[] field = MoonPhaseDisc.BuildAlphaField(size, 0.0, 0.0);
            Assert.InRange(Center(field, size), 0.99f, 1.0f);
            // Left edge of the disc is lit too (no terminator at full moon).
            int row = size / 2;
            Assert.True(field[row * size + size / 2 - 20] > 0.9f);
        }

        [Fact]
        public void AlphaField_NewMoon_IsDark()
        {
            int size = 64;
            float[] field = MoonPhaseDisc.BuildAlphaField(size, Math.PI, 0.0);
            Assert.InRange(Center(field, size), 0.0f, 0.01f);
        }

        [Fact]
        public void AlphaField_QuarterMoon_LightsTheBrightHalfOnly()
        {
            int size = 64;
            float[] field = MoonPhaseDisc.BuildAlphaField(size, Math.PI / 2, 0.0);
            int row = size / 2;
            float right = field[row * size + size / 2 + 20]; // toward +x
            float left = field[row * size + size / 2 - 20];  // away from +x
            Assert.True(right > 0.9f, "bright half must be lit, got " + right);
            Assert.True(left < 0.1f, "dark half must be dark, got " + left);
        }

        [Fact]
        public void AlphaField_RotatesWithTheBrightLimb()
        {
            int size = 64;
            // Bright limb at position angle 90° (east): the +x direction on
            // screen is north, so the lit half is the TOP of the image.
            float[] field = MoonPhaseDisc.BuildAlphaField(size, Math.PI / 2, Math.PI / 2);
            int col = size / 2;
            float top = field[(size / 2 - 20) * size + col];
            float bottom = field[(size / 2 + 20) * size + col];
            Assert.True(top > 0.9f, "top half must be lit, got " + top);
            Assert.True(bottom < 0.1f, "bottom half must be dark, got " + bottom);
        }

        [Fact]
        public void AlphaField_IsZeroOutsideTheDisc()
        {
            int size = 64;
            float[] field = MoonPhaseDisc.BuildAlphaField(size, 0.0, 0.0);
            Assert.Equal(0f, field[0]);
            Assert.Equal(0f, field[size - 1]);
        }
    }
}
