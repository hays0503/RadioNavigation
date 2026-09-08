using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // F15: persist the operator's panel state (volume, feedback, zone, cursor
    // frequency) across game sessions via PlayerPrefs so the radio doesn't
    // reset every time the panel is opened. Pure key/codec logic is testable
    // without Unity (PanelPersistenceLogic); this class only wires PlayerPrefs.
    internal static class PanelStateStore
    {
        private const string KeyVolume = "RadioNavigation.VolumeAngle";
        private const string KeyFeedback = "RadioNavigation.FeedbackAngle";
        private const string KeyZone = "RadioNavigation.ZoneIndex";
        private const string KeyCursor = "RadioNavigation.CursorAngle";

        public static void Save(float volumeAngle, float feedbackAngle, int zoneIndex, float cursorAngle)
        {
            // ADR-019: clamp чтобы битый вызов не затёр Prefs нулями/мусором (best practice)
            PlayerPrefs.SetFloat(KeyVolume, PanelPersistenceLogic.ClampSpinnerAngle(volumeAngle));
            PlayerPrefs.SetFloat(KeyFeedback, PanelPersistenceLogic.ClampSpinnerAngle(feedbackAngle));
            PlayerPrefs.SetInt(KeyZone, PanelPersistenceLogic.ClampZoneIndex(zoneIndex));
            PlayerPrefs.SetFloat(KeyCursor, PanelPersistenceLogic.ClampCursorAngle(cursorAngle));
            PlayerPrefs.Save();
        }

        public static void Load(out float volumeAngle, out float feedbackAngle, out int zoneIndex, out float cursorAngle)
        {
            volumeAngle = PanelPersistenceLogic.ClampSpinnerAngle(PlayerPrefs.GetFloat(KeyVolume, 0f));
            feedbackAngle = PanelPersistenceLogic.ClampSpinnerAngle(PlayerPrefs.GetFloat(KeyFeedback, 0f));
            zoneIndex = PanelPersistenceLogic.ClampZoneIndex(PlayerPrefs.GetInt(KeyZone, 0));
            cursorAngle = PanelPersistenceLogic.ClampCursorAngle(PlayerPrefs.GetFloat(KeyCursor, 0f));
        }
    }
}
