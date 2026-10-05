using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    public class SextantPipRenderPolicyTests
    {
        [Theory]
        [InlineData(true, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, false, true)]
        public void DefaultRouteRendersExactlyOnce(bool attached, bool rootRendering,
            bool expectedStandalone)
        {
            bool standalone = SextantPipRenderPolicy.UseStandaloneCamera(
                attached, rootRendering, true);
            Assert.Equal(expectedStandalone, standalone);
            int renderCount = (attached && rootRendering ? 1 : 0) + (standalone ? 1 : 0);
            Assert.Equal(1, renderCount);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void DiagnosticOffRestoresTheOldStandaloneRoute(bool attached, bool rootRendering)
        {
            Assert.True(SextantPipRenderPolicy.UseStandaloneCamera(attached, rootRendering, false));
        }
    }
}
