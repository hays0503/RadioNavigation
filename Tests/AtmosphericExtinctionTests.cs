using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P4 atmospheric extinction / horizon fade.</summary>
    public sealed class AtmosphericExtinctionTests
    {
        [Fact]
        public void Airmass_Zenith_IsOne()
        {
            Assert.InRange(AtmosphericExtinction.Airmass(90.0), 0.999, 1.001);
        }

        [Fact]
        public void Airmass_MatchesYoungTable()
        {
            Assert.InRange(AtmosphericExtinction.Airmass(10.0), 5.4, 5.8);
            Assert.InRange(AtmosphericExtinction.Airmass(5.0), 9.8, 10.8);
        }

        [Fact]
        public void Airmass_IsMonotonicWithAltitude()
        {
            double previous = AtmosphericExtinction.Airmass(0.5);
            for (double alt = 1.0; alt <= 90.0; alt += 1.0)
            {
                double x = AtmosphericExtinction.Airmass(alt);
                Assert.True(x <= previous + 1e-9, "airmass must not grow with altitude");
                previous = x;
            }
        }

        [Fact]
        public void MagnitudeLoss_ZenithIsZero_AndGrowsTowardHorizon()
        {
            Assert.InRange(AtmosphericExtinction.MagnitudeLoss(90.0), -1e-6, 1e-6);
            Assert.InRange(AtmosphericExtinction.MagnitudeLoss(10.0), 0.87, 0.97);
            Assert.True(AtmosphericExtinction.MagnitudeLoss(5.0)
                > AtmosphericExtinction.MagnitudeLoss(10.0));
        }

        [Fact]
        public void BvShift_ReddensTowardHorizon()
        {
            Assert.InRange(AtmosphericExtinction.BvShift(90.0), -1e-6, 1e-6);
            Assert.True(AtmosphericExtinction.BvShift(5.0) > AtmosphericExtinction.BvShift(30.0));
        }

        [Fact]
        public void HorizonFade_RampsToFullAtTenDegrees()
        {
            Assert.Equal(0f, AtmosphericExtinction.HorizonFade(-1.0));
            Assert.Equal(0f, AtmosphericExtinction.HorizonFade(0.0));
            Assert.InRange(AtmosphericExtinction.HorizonFade(5.0), 0.49f, 0.51f);
            Assert.Equal(1f, AtmosphericExtinction.HorizonFade(10.0));
            Assert.Equal(1f, AtmosphericExtinction.HorizonFade(45.0));
        }

        [Fact]
        public void CullAltitude_IsBelowTheHorizon()
        {
            // The fade reaches zero at 0°, so the cull must sit slightly below
            // it (popping guard) — never above, or stars would vanish early.
            Assert.InRange(AtmosphericExtinction.CullAltitudeDeg, -1.0, 0.0);
        }
    }
}
