using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Debug aim readout (RadioNav.DebugAim): the angular offset between the
    /// line of sight and the identified body, split into the vertical (drum)
    /// and horizontal (pan) components plus the total. 1' = 1 NM of intercept.
    /// </summary>
    public sealed class AimOffsetFormatTests
    {
        [Fact]
        public void ToArcMin_ConvertsDegrees()
        {
            Assert.Equal(60.0, AimOffsetFormat.ToArcMin(1.0), 9);
            Assert.Equal(30.0, AimOffsetFormat.ToArcMin(0.5), 9);
            Assert.Equal(0.0, AimOffsetFormat.ToArcMin(0.0), 9);
        }

        [Fact]
        public void Total_SameDirection_IsZero()
        {
            Assert.Equal(0.0, AimOffsetFormat.TotalDeg(0, 0, 1, 0, 0, 1), 6);
        }

        [Fact]
        public void Total_Perpendicular_IsNinety()
        {
            Assert.Equal(90.0, AimOffsetFormat.TotalDeg(0, 0, 1, 1, 0, 0), 6);
        }

        [Fact]
        public void Total_DegenerateAim_IsNaN()
        {
            Assert.True(double.IsNaN(AimOffsetFormat.TotalDeg(0, 0, 0, 0, 0, 1)));
        }

        [Fact]
        public void Total_IgnoresVectorLength()
        {
            // Unnormalised inputs must not change the angle.
            Assert.Equal(0.0, AimOffsetFormat.TotalDeg(0, 0, 10, 0, 0, 1), 6);
        }

        [Fact]
        public void Horizontal_BodyRightOfAim_IsPositive()
        {
            double five = 5.0 * System.Math.PI / 180.0;
            double bx = System.Math.Sin(five);
            double bz = System.Math.Cos(five);
            Assert.Equal(5.0, AimOffsetFormat.HorizontalDeg(0, 0, 1, bx, 0, bz), 4);
        }

        [Fact]
        public void Horizontal_BodyLeftOfAim_IsNegative()
        {
            double five = 5.0 * System.Math.PI / 180.0;
            double bx = -System.Math.Sin(five);
            double bz = System.Math.Cos(five);
            Assert.Equal(-5.0, AimOffsetFormat.HorizontalDeg(0, 0, 1, bx, 0, bz), 4);
        }

        [Fact]
        public void Horizontal_VerticalAim_IsNaN()
        {
            // No defined right axis when the aim is straight up.
            Assert.True(double.IsNaN(AimOffsetFormat.HorizontalDeg(0, 1, 0, 1, 0, 0)));
        }

        [Fact]
        public void Horizontal_BodyStraightUp_IsZero()
        {
            Assert.Equal(0.0, AimOffsetFormat.HorizontalDeg(0, 0, 1, 0, 1, 0), 6);
        }

        [Fact]
        public void Format_AllThreeParts()
        {
            Assert.Equal("  \u03944.8\u2032 \u0432+4.6\u2032 \u0433\u22121.2\u2032",
                AimOffsetFormat.Format(4.8, 4.6, -1.2));
        }

        [Fact]
        public void Format_NaNTotal_IsEmpty()
        {
            Assert.Equal("", AimOffsetFormat.Format(double.NaN, 1.0, 1.0));
        }

        [Fact]
        public void Format_NaNParts_ShowDash()
        {
            Assert.Equal("  \u03944.8\u2032 \u0432\u2014 \u0433\u2014",
                AimOffsetFormat.Format(4.8, double.NaN, double.NaN));
        }

        [Fact]
        public void Signed_NegativeUsesMinusSign()
        {
            Assert.Equal("\u22121.2", AimOffsetFormat.Signed(-1.2));
        }

        [Fact]
        public void Signed_PositiveUsesPlus()
        {
            Assert.Equal("+4.6", AimOffsetFormat.Signed(4.6));
        }

        [Fact]
        public void Signed_NaN_IsDash()
        {
            Assert.Equal("\u2014", AimOffsetFormat.Signed(double.NaN));
        }

        [Fact]
        public void Unsigned_DropsSign()
        {
            Assert.Equal("4.8", AimOffsetFormat.Unsigned(-4.8));
        }
    }
}
