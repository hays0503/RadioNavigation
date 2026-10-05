using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// StatusLineText contract: the Toggle/Ground/Zone status one-liners
    /// behind InformationPanelHandler. Extracted so the label assembly
    /// runs offline.
    /// </summary>
    public sealed class StatusLineTextTests
    {
        [Fact]
        public void Toggle_OnOff()
        {
            Assert.Equal("Toggle: ON", StatusLineText.Toggle(true));
            Assert.Equal("Toggle: OFF", StatusLineText.Toggle(false));
        }

        [Fact]
        public void Ground_ActiveDimmed()
        {
            Assert.Equal("Ground: Active", StatusLineText.Ground(true));
            Assert.Equal("Ground: Dimmed", StatusLineText.Ground(false));
        }

        [Fact]
        public void Zone_IsOneBased()
        {
            Assert.Equal("Zone: 1", StatusLineText.Zone(0));
            Assert.Equal("Zone: 2", StatusLineText.Zone(1));
        }
    }
}
