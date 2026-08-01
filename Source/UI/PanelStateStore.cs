using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // F15: persist the operator's panel state (volume, feedback, zone, cursor
    // frequency) across game sessions via PlayerPrefs so the radio doesn't
    // reset every time the panel is opened. Pure key/codec logic is testable
    // without Unity (PanelPersistenceLogic); this class only wires PlayerPrefs.
    public static class PanelStateStore
    {
        private const string KeyVolume = "RadioNavigation.VolumeAngle";
        private const string KeyFeedback = "RadioNavigation.FeedbackAngle";
        private const string KeyZone = "RadioNavigation.ZoneIndex";
        private const string KeyCursor = "RadioNavigation.CursorAngle";

        public static void Save(float volumeAngle, float feedbackAngle, int zoneIndex, float cursorAngle)
        {
            PlayerPrefs.SetFloat(KeyVolume, volumeAngle);
            PlayerPrefs.SetFloat(KeyFeedback, feedbackAngle);
            PlayerPrefs.SetInt(KeyZone, zoneIndex);
            PlayerPrefs.SetFloat(KeyCursor, cursorAngle);
            PlayerPrefs.Save();
        }

        public static void Load(out float volumeAngle, out float feedbackAngle, out int zoneIndex, out float cursorAngle)
        {
            volumeAngle = PlayerPrefs.GetFloat(KeyVolume, 0f);
            feedbackAngle = PlayerPrefs.GetFloat(KeyFeedback, 0f);
            zoneIndex = PlayerPrefs.GetInt(KeyZone, 0);
            cursorAngle = PlayerPrefs.GetFloat(KeyCursor, 0f);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KeyVolume);
            PlayerPrefs.DeleteKey(KeyFeedback);
            PlayerPrefs.DeleteKey(KeyZone);
            PlayerPrefs.DeleteKey(KeyCursor);
            PlayerPrefs.Save();
        }
    }
}
