using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single source of truth for the 140° spinner range: knob angle to
    /// unit gain and to display percent. Unifies the scattered angle/140f
    /// divisions (SonneController, InformationPanelHandler,
    /// AudioSynthesisEngine). PanelStateStore keeps its own storage-range
    /// clamps. Tested offline in SpinnerScaleTests (Mathf stubbed).
    /// </summary>
    public static class SpinnerScale
    {
        public const float MaxAngle = 140f;

        public static float ToUnit(float angle)
        {
            // NaN comparisons are always false, so a bare Clamp01 would pass
            // NaN through into gain/Q-factor/DSP state. Sanitize at the entry.
            if (float.IsNaN(angle))
                return 0f;
            return Mathf.Clamp01(angle / MaxAngle);
        }

        public static int ToPercent(float angle)
        {
            return Mathf.RoundToInt(ToUnit(angle) * 100f);
        }
    }
}
