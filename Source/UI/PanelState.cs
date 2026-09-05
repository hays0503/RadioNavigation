using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Operator-state persistence behind InformationPanelHandler (SRP):
    /// restore applies the stored volume/feedback/zone/cursor through the
    /// normal pipeline, save snapshots them before teardown. The panel
    /// supplies handler-bound sinks (null when the handler is missing);
    /// the store stays in PanelStateStore. Runs offline in
    /// PanelStateTests. Pure C# apart from the store.
    /// </summary>
    public static class PanelState
    {
        // F15: apply persisted state so the radio reads as it was left.
        // Called after handlers are found/subscribed so restored values
        // propagate through the normal pipeline.
        public static void Restore(Action<float> setVolumeAngle, Action<float> setFeedbackAngle,
            Action<int> setZone, Action<float> setCursorAngle)
        {
            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);
            if (setVolumeAngle != null)
                setVolumeAngle(vol);
            if (setFeedbackAngle != null)
                setFeedbackAngle(fb);
            if (setZone != null)
                setZone(zone);
            if (setCursorAngle != null)
                setCursorAngle(cursor);
        }

        // F15: persist the current operator state before the panel goes away.
        public static void Save(float volumeAngle, float feedbackAngle, int zoneIndex, float cursorAngle)
        {
            PanelStateStore.Save(volumeAngle, feedbackAngle, zoneIndex, cursorAngle);
        }
    }
}
