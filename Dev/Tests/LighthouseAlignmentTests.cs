using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the ship-anchored world alignment math that corrects the
    /// epoch-dependent spawn offset of mid-session sandbox groups
    /// (LighthousePlacer.AlignToWorldFrame).
    /// </summary>
    public class LighthouseAlignmentTests
    {
        private static void AssertShift(
            float expectedX, float expectedZ,
            float shipWorldX, float shipWorldZ,
            float shipKmX, float shipKmY,
            float lightKmX, float lightKmY,
            float rotatorWorldX, float rotatorWorldZ)
        {
            float shiftX;
            float shiftZ;
            LighthouseAlignment.ComputeShift(
                shipWorldX, shipWorldZ,
                shipKmX, shipKmY,
                lightKmX, lightKmY,
                rotatorWorldX, rotatorWorldZ,
                out shiftX, out shiftZ);

            Assert.Equal(expectedX, shiftX, 3);
            Assert.Equal(expectedZ, shiftZ, 3);
        }

        [Fact]
        public void AlreadyAligned_ZeroShift()
        {
            // Ship at encounter (843.0, 5975.375) km, world (10, 20) m; lamp
            // exactly 125 m north of the ship (the charted position). All km
            // fractions are binary-exact (multiples of 1/8) so the float
            // math is exact and the shift must be exactly zero.
            AssertShift(
                expectedX: 0f, expectedZ: 0f,
                shipWorldX: 10f, shipWorldZ: 20f,
                shipKmX: 843f, shipKmY: 5975.375f,
                lightKmX: 843f, lightKmY: 5975.5f,
                rotatorWorldX: 10f, rotatorWorldZ: 145f);
        }

        [Fact]
        public void EastOfShip_CorrectedToChartedSpot()
        {
            // Light 1 km east of the ship; rotator currently 344 m west and
            // 617 m south of the ship's world origin (epoch drift).
            AssertShift(
                expectedX: 1344f, expectedZ: 617f,
                shipWorldX: 10f, shipWorldZ: 20f,
                shipKmX: 843f, shipKmY: 5975.4f,
                lightKmX: 844f, lightKmY: 5975.4f,
                rotatorWorldX: -334f, rotatorWorldZ: -597f);
        }

        [Fact]
        public void NorthOfShip_KilometerDeltaScalesToMeters()
        {
            // Light 0.5 km north of the ship; rotator sits on the ship.
            AssertShift(
                expectedX: 0f, expectedZ: 500f,
                shipWorldX: 0f, shipWorldZ: 0f,
                shipKmX: 100f, shipKmY: 5000f,
                lightKmX: 100f, lightKmY: 5000.5f,
                rotatorWorldX: 0f, rotatorWorldZ: 0f);
        }

        [Fact]
        public void ObservedEpochDrift_BensersielCase_Corrected()
        {
            // Reproduces the in-game observation: ship near world origin,
            // charted lamp 125 m north of the ship, but the spawned rotator
            // landed at (-344, -617) m (floating-origin epoch drift).
            // Expected shift: (+344, +742) m.
            AssertShift(
                expectedX: 344f, expectedZ: 742f,
                shipWorldX: 0f, shipWorldZ: 0f,
                shipKmX: 843f, shipKmY: 5975.375f,
                lightKmX: 843f, lightKmY: 5975.5f,
                rotatorWorldX: -344f, rotatorWorldZ: -617f);
        }

        [Fact]
        public void NegativeCoordinates_WestOfGreenwich()
        {
            // Career start area (~48N, -15W): light west of the ship.
            AssertShift(
                expectedX: -2000f, expectedZ: 0f,
                shipWorldX: 5f, shipWorldZ: 5f,
                shipKmX: -1751f, shipKmY: 5374f,
                lightKmX: -1753f, lightKmY: 5374f,
                rotatorWorldX: 5f, rotatorWorldZ: 5f);
        }
    }
}
