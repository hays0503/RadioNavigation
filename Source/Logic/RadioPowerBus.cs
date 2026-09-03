namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Process-wide radio power state: on/off from the panel toggle plus the
    /// ground/active condition (panel interactable only when both hold).
    /// Extracted from ToggleHandler (SRP — a uGUI widget must not own global
    /// audio-gating state) so it is unit-testable offline in
    /// RadioPowerBusTests. Pure C#, no Unity dependency.
    /// Threading: IsRadioOn is read from the audio DSP thread, hence volatile.
    /// </summary>
    public static class RadioPowerBus
    {
        private static volatile bool radioOn;
        private static bool interactable;
        private static bool groundActive;

        public static bool IsRadioOn
        {
            get { return radioOn; }
        }

        public static bool IsInteractable
        {
            get { return interactable; }
        }

        public static void SetRadioOn(bool on)
        {
            radioOn = on;
            UpdateInteractable();
        }

        public static void SetGroundActive(bool active)
        {
            groundActive = active;
            UpdateInteractable();
        }

        /// <summary>
        /// Scene teardown: clears the F61 leak (stale "on" across loads).
        /// </summary>
        public static void Reset()
        {
            radioOn = false;
            interactable = false;
            groundActive = false;
        }

        private static void UpdateInteractable()
        {
            interactable = radioOn && groundActive;
        }
    }
}
