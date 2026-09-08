namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Native chronometer open-state edge tracking + F7 fallback decision
    /// behind ChronometerOverlayHandler.LateUpdate (P5.3 slice, CH). The
    /// handler reads hardware state and applies Text/toggle; the
    /// suppress-on-native-change rule lives here so the matrix runs
    /// offline in ChronometerOpenTrackerTests. Pure C#.
    /// </summary>
    public static class ChronometerOpenTracker
    {
        public struct Frame
        {
            public readonly bool EffectiveOpen;
            public readonly bool ToggledByFallback;

            public Frame(bool effectiveOpen, bool toggledByFallback)
            {
                EffectiveOpen = effectiveOpen;
                ToggledByFallback = toggledByFallback;
            }
        }

        public static Frame Track(bool hasPrevious, bool previousOpen, bool isOpen,
            bool f7Pressed, bool buttonReady)
        {
            bool nativeChanged = hasPrevious && isOpen != previousOpen;
            bool toggle = f7Pressed && !nativeChanged && buttonReady;
            return new Frame(toggle ? !isOpen : isOpen, toggle);
        }
    }
}
