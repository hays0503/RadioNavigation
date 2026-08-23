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
            // ADR-019: clamp чтобы битый вызов не затёр Prefs нулями/мусором (best practice)
            volumeAngle = Mathf.Clamp(volumeAngle, 0f, 140f);
            feedbackAngle = Mathf.Clamp(feedbackAngle, 0f, 140f);
            zoneIndex = zoneIndex < 0 ? 0 : zoneIndex > 4 ? 4 : zoneIndex;
            cursorAngle = Mathf.Clamp(cursorAngle, -90f, 90f);
            PlayerPrefs.SetFloat(KeyVolume, volumeAngle);
            PlayerPrefs.SetFloat(KeyFeedback, feedbackAngle);
            PlayerPrefs.SetInt(KeyZone, zoneIndex);
            PlayerPrefs.SetFloat(KeyCursor, cursorAngle);
            PlayerPrefs.Save();
        }

        public static void Load(out float volumeAngle, out float feedbackAngle, out int zoneIndex, out float cursorAngle)
        {
            volumeAngle = Mathf.Clamp(PlayerPrefs.GetFloat(KeyVolume, 0f), 0f, 140f);
            feedbackAngle = Mathf.Clamp(PlayerPrefs.GetFloat(KeyFeedback, 0f), 0f, 140f);
            int zi = PlayerPrefs.GetInt(KeyZone, 0);
            zoneIndex = zi < 0 ? 0 : zi > 4 ? 4 : zi;
            cursorAngle = Mathf.Clamp(PlayerPrefs.GetFloat(KeyCursor, 0f), -90f, 90f);
        }
    }
}
