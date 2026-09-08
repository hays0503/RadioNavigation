using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class PanelPersistenceLogicTests
    {
        [Fact]
        public void ClampSpinnerAngle_Bounds()
        {
            Assert.Equal(0f, PanelPersistenceLogic.ClampSpinnerAngle(-5f));
            Assert.Equal(0f, PanelPersistenceLogic.ClampSpinnerAngle(0f));
            Assert.Equal(70f, PanelPersistenceLogic.ClampSpinnerAngle(70f));
            Assert.Equal(140f, PanelPersistenceLogic.ClampSpinnerAngle(140f));
            Assert.Equal(140f, PanelPersistenceLogic.ClampSpinnerAngle(999f));
        }

        [Fact]
        public void ClampZoneIndex_Bounds()
        {
            Assert.Equal(0, PanelPersistenceLogic.ClampZoneIndex(-1));
            Assert.Equal(0, PanelPersistenceLogic.ClampZoneIndex(0));
            Assert.Equal(2, PanelPersistenceLogic.ClampZoneIndex(2));
            Assert.Equal(4, PanelPersistenceLogic.ClampZoneIndex(4));
            Assert.Equal(4, PanelPersistenceLogic.ClampZoneIndex(99));
        }

        [Fact]
        public void ClampCursorAngle_Bounds()
        {
            Assert.Equal(-90f, PanelPersistenceLogic.ClampCursorAngle(-100f));
            Assert.Equal(-90f, PanelPersistenceLogic.ClampCursorAngle(-90f));
            Assert.Equal(10f, PanelPersistenceLogic.ClampCursorAngle(10f));
            Assert.Equal(90f, PanelPersistenceLogic.ClampCursorAngle(90f));
            Assert.Equal(90f, PanelPersistenceLogic.ClampCursorAngle(100f));
        }

        [Fact]
        public void Clamps_NaN_ReturnsZero()
        {
            Assert.Equal(0f, PanelPersistenceLogic.ClampSpinnerAngle(float.NaN));
            Assert.Equal(0f, PanelPersistenceLogic.ClampCursorAngle(float.NaN));
        }
    }
}
