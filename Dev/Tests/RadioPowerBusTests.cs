using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Radio power bus contract: the global on/off + ground/active state
    /// must live outside the ToggleHandler MonoBehaviour (SRP — a UI widget
    /// must not own process-wide audio gating state read from the audio
    /// thread). Pins the truth table and the F61 scene-load reset.
    /// </summary>
    public sealed class RadioPowerBusTests
    {
        [Fact]
        public void RadioOn_GroundActive_Interactable()
        {
            RadioPowerBus.Reset();
            RadioPowerBus.SetRadioOn(true);
            RadioPowerBus.SetGroundActive(true);

            Assert.True(RadioPowerBus.IsRadioOn);
            Assert.True(RadioPowerBus.IsInteractable);
        }

        [Fact]
        public void RadioOn_GroundInactive_NotInteractable()
        {
            RadioPowerBus.Reset();
            RadioPowerBus.SetRadioOn(true);
            RadioPowerBus.SetGroundActive(false);

            Assert.True(RadioPowerBus.IsRadioOn);
            Assert.False(RadioPowerBus.IsInteractable);
        }

        [Fact]
        public void RadioOff_NeverInteractable()
        {
            RadioPowerBus.Reset();
            RadioPowerBus.SetGroundActive(true);
            RadioPowerBus.SetRadioOn(false);

            Assert.False(RadioPowerBus.IsRadioOn);
            Assert.False(RadioPowerBus.IsInteractable);
        }

        [Fact]
        public void RadioOff_StaysOffWhenGroundToggles()
        {
            RadioPowerBus.Reset();
            RadioPowerBus.SetRadioOn(false);
            RadioPowerBus.SetGroundActive(true);
            RadioPowerBus.SetGroundActive(false);

            Assert.False(RadioPowerBus.IsRadioOn);
            Assert.False(RadioPowerBus.IsInteractable);
        }

        [Fact]
        public void Reset_ClearsAllState()
        {
            RadioPowerBus.SetRadioOn(true);
            RadioPowerBus.SetGroundActive(true);

            RadioPowerBus.Reset();

            Assert.False(RadioPowerBus.IsRadioOn);
            Assert.False(RadioPowerBus.IsInteractable);
        }
    }
}
