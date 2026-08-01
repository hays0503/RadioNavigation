using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ComparisonExtensionsTests
    {
        [Fact]
        public void Int_Comparisons()
        {
            Assert.True(0.IsZero());
            Assert.True(1.IsNotZero());
            Assert.True(1.IsPositive());
            Assert.True((-1).IsNegative());
            Assert.True(0.IsNonNegative());
            Assert.True(0.IsNonPositive());
            Assert.True((-5).IsNonPositive());
        }

        [Fact]
        public void Float_Comparisons()
        {
            Assert.True(0f.IsZero());
            Assert.True(1.5f.IsPositive());
            Assert.True((-0.5f).IsNegative());
        }

        [Fact]
        public void Double_Comparisons()
        {
            Assert.True(0.0.IsZero());
            Assert.True(0.0001.IsPositive());
        }

        [Fact]
        public void Significant_Negligible_Thresholds()
        {
            Assert.True(0.01f.IsSignificant());
            Assert.True(1e-10f.IsNegligible());
            Assert.False(0.0005f.IsSignificant());
        }

        [Fact]
        public void Object_NullChecks()
        {
            object nullObj = null;
            object obj = new object();
            Assert.True(nullObj.IsNull());
            Assert.False(nullObj.IsNotNull());
            Assert.True(obj.IsNotNull());
            Assert.False(obj.IsNull());
        }
    }
}
