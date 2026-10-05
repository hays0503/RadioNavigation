using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// EntityTypeRowValues contract: the 18-column Entities/Types row
    /// behind LighthousePlacer.CreateEntityType. Extracted so the layout
    /// runs offline; the placer keeps buffer ownership (P5.1) and game
    /// row construction.
    /// </summary>
    public sealed class EntityTypeRowValuesTests
    {
        private static List<string> Build()
        {
            var values = new List<string>();
            EntityTypeRowValues.BuildValues("Lighthouse Test", "prefab/path", values);
            return values;
        }

        [Fact]
        public void BuildValues_Has18Columns()
        {
            Assert.Equal(18, Build().Count);
        }

        [Fact]
        public void BuildValues_KeyColumns()
        {
            List<string> values = Build();

            Assert.Equal("Lighthouse Test", values[0]);
            Assert.Equal("Structure", values[1]);
            Assert.Equal("prefab/path", values[9]);
            Assert.Equal("False", values[13]);
            Assert.Equal("False", values[14]);
        }

        [Fact]
        public void BuildValues_FillerColumnsAreZero()
        {
            List<string> values = Build();

            foreach (int i in new[] { 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 15, 16, 17 })
                Assert.Equal("0", values[i]);
        }

        [Fact]
        public void BuildValues_ClearsExistingContent()
        {
            var values = new List<string> { "stale" };

            EntityTypeRowValues.BuildValues("N", "P", values);

            Assert.Equal(18, values.Count);
            Assert.Equal("N", values[0]);
        }
    }
}
