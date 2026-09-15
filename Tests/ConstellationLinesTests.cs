using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Data integrity for the generated constellation line table: every index
    /// must point into StarCatalog.Stars, and each polyline needs at least two
    /// points. Catches a regenerated table drifting from the catalog.
    /// </summary>
    public sealed class ConstellationLinesTests
    {
        [Fact]
        public void Polylines_NonEmpty()
        {
            Assert.NotEmpty(ConstellationLines.Polylines);
        }

        [Fact]
        public void Polylines_IndicesWithinStarCatalog()
        {
            int max = StarCatalog.Stars.Length - 1;
            for (int i = 0; i < ConstellationLines.Polylines.Length; i++)
            {
                int[] line = ConstellationLines.Polylines[i];
                Assert.True(line.Length >= 2,
                    "polyline " + i + " has fewer than 2 points");
                for (int j = 0; j < line.Length; j++)
                {
                    Assert.InRange(line[j], 0, max);
                }
            }
        }
    }
}
