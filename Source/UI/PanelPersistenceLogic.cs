using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure validation for persisted operator state (F15/ADR-019): clamps
    /// live in one place so Save and Load cannot drift apart, and run
    /// offline in PanelPersistenceLogicTests. PanelStateStore only wires
    /// PlayerPrefs.
    /// </summary>
    public static class PanelPersistenceLogic
    {
        public const float MinSpinnerAngle = 0f;
        public const float MaxSpinnerAngle = 140f;
        public const int MinZoneIndex = 0;
        public const int MaxZoneIndex = 4;
        public const float MinCursorAngle = -90f;
        public const float MaxCursorAngle = 90f;

        public static float ClampSpinnerAngle(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return Mathf.Clamp(value, MinSpinnerAngle, MaxSpinnerAngle);
        }

        public static int ClampZoneIndex(int value)
        {
            if (value < MinZoneIndex)
                return MinZoneIndex;
            if (value > MaxZoneIndex)
                return MaxZoneIndex;
            return value;
        }

        public static float ClampCursorAngle(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return Mathf.Clamp(value, MinCursorAngle, MaxCursorAngle);
        }
    }
}
