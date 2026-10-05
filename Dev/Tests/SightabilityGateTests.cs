using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P5 sightability gates and the world-fixed identification cone.</summary>
    public sealed class SightabilityGateTests
    {
        [Fact]
        public void Star_BelowTenDegrees_IsBlocked()
        {
            Assert.Equal(SightBlock.Low,
                SightabilityGate.Evaluate(CelestialBodyKind.Star, 9.9, -20.0, 0.0));
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Star, 10.0, -20.0, 0.0));
        }

        [Fact]
        public void Star_InDaylight_IsBlocked()
        {
            Assert.Equal(SightBlock.Daylight,
                SightabilityGate.Evaluate(CelestialBodyKind.Star, 40.0, 10.0, 0.0));
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Star, 40.0, -2.0, 0.0));
        }

        [Fact]
        public void FaintStars_AreNotBlockedByMagnitude()
        {
            // Author decision: faint stars stay recordable — the sky model
            // makes them hard, not a magnitude ban. The gate has no mag input.
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Star, 45.0, -20.0, 0.0));
        }

        [Fact]
        public void Moon_HasItsOwnGates_AndIgnoresTheStarFloor()
        {
            Assert.Equal(SightBlock.MoonNew,
                SightabilityGate.Evaluate(CelestialBodyKind.Moon, 30.0, -20.0, 0.04));
            Assert.Equal(SightBlock.MoonSet,
                SightabilityGate.Evaluate(CelestialBodyKind.Moon, 0.0, -20.0, 0.5));
            // Low but visible at night: the Moon is exempt from the 10° floor.
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Moon, 5.0, -20.0, 0.5));
            Assert.Equal(SightBlock.Daylight,
                SightabilityGate.Evaluate(CelestialBodyKind.Moon, 30.0, 5.0, 0.5));
        }

        [Fact]
        public void Sun_IsNeverBlockedByThisGate()
        {
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Sun, 5.0, 5.0, 0.0));
            Assert.Equal(SightBlock.None,
                SightabilityGate.Evaluate(CelestialBodyKind.Sun, -5.0, -5.0, 0.0));
        }

        [Fact]
        public void EffectiveCone_IsFixedAtOneDegree()
        {
            // Author decision 2026-11-04: FIXED 1.0° at every zoom (the old
            // 0.50 -> 0.10 shrink was impossible to aim; the recorded Ho comes
            // from the drum at the I press). Pin the value itself: a mutation
            // of both the constant and the getter must fail this test.
            Assert.Equal(1.0, SightabilityGate.FixedConeDeg);
            Assert.Equal(1.0, SightabilityGate.EffectiveConeDeg());
        }

        [Fact]
        public void Describe_ReturnsAsciiTokens()
        {
            Assert.Equal("ok", SightabilityGate.Describe(SightBlock.None));
            Assert.Equal("low", SightabilityGate.Describe(SightBlock.Low));
            Assert.Equal("daylight", SightabilityGate.Describe(SightBlock.Daylight));
            Assert.Equal("moon-new", SightabilityGate.Describe(SightBlock.MoonNew));
            Assert.Equal("moon-set", SightabilityGate.Describe(SightBlock.MoonSet));
        }
    }
}
