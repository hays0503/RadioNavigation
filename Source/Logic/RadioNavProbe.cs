using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Read-write test probe for external agents (MCP bridge): panel tuning
    /// state, decoded text and per-station diagnostics as JSON, plus cursor
    /// angle setter (same path as the UI spinner). Pull model, main thread.
    /// </summary>
    public static class RadioNavProbe
    {
        private static bool subscribed;
        private static float lastFrequencyKHz = -1f;
        private static int lastZoneIndex = -1;

        public static string Snapshot()
        {
            try
            {
                EnsureSubscribed();
                RadioHandler panel = RadioHandler.Instance;
                if (panel == null)
                {
                    // Null instance while the subsystem visibly runs: report
                    // how many RadioHandler components exist anywhere (even
                    // inactive) and how many copies of this assembly are
                    // loaded — distinguishes "no panel" from "bundle bound
                    // to a different assembly copy".
                    int handlers = 0;
                    try { handlers = UnityEngine.Resources.FindObjectsOfTypeAll<RadioHandler>().Length; }
                    catch (Exception ex)
                    {
                        if (WarnOnce.ShouldLog("RadioNavProbe.HandlerComponentScan"))
                            UnityEngine.Debug.LogWarning("[RadioNav][Probe] handler component scan failed: " + ex.GetType().Name);
                    }
                    int copies = 0;
                    string selfName = typeof(RadioNavProbe).Assembly.GetName().Name;
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(a.GetName().Name, selfName, StringComparison.OrdinalIgnoreCase))
                            copies++;
                    }
                    return "{\"loaded\":false,\"handlerComponents\":" + handlers + ",\"asmCopies\":" + copies + "}";
                }
                SonneController controller = panel.GetComponent<SonneController>();
                if (controller == null || controller.StationManager == null)
                {
                    return RadioNavProbeJson.Build(
                        panel.IsVisible, panel.CursorAngle,
                        lastFrequencyKHz, lastZoneIndex,
                        false, 0f, 0f, -1, null, null,
                        RadioPowerBus.IsRadioOn, 0, -1, false,
                        false, -1, MorseKeyer.CallsignFlaggedElements,
                        SignalDecoder.CallsignSeenTotal, SignalDecoder.CallsignSeenProcessed,
                    SignalDecoder.ClearCount, SignalDecoder.FirstClearStack,
                    SignalTranscript.DecodeAttempts, SignalTranscript.DecodeSuccess,
                    SignalTranscript.LastBufferLen, SignalTranscript.LastExpectedLen);
                }
                StationManager manager = controller.StationManager;
                bool hasPos = manager.HasPosition;
                StationDiagnostics[] diags = CollectDiagnostics(manager);
                int csElemCount = -1;
                bool keyerActive = false;
                bool keyerHasSequence = false;
                int keyerElementIndex = -1;
                if (manager.DominantStationIndex >= 0 && manager.DominantStationIndex < manager.StationCount)
                {
                    csElemCount = manager.Schedulers[manager.DominantStationIndex].CallsignElementCount;
                    MorseKeyer dominantKeyer = manager.Keyers[manager.DominantStationIndex];
                    keyerActive = dominantKeyer.IsActive;
                    keyerHasSequence = dominantKeyer.HasSequence;
                    keyerElementIndex = dominantKeyer.ElementIndex;
                }
                return RadioNavProbeJson.Build(
                    panel.IsVisible, panel.CursorAngle,
                    lastFrequencyKHz, lastZoneIndex,
                    hasPos, hasPos ? manager.PlayerLatitude : 0f,
                    hasPos ? manager.PlayerLongitude : 0f,
                    manager.DominantStationIndex, diags,
                    controller.GetDecodedText(),
                    RadioPowerBus.IsRadioOn,
                    controller.DecoderCallsignChars(),
                    csElemCount, keyerActive, keyerHasSequence, keyerElementIndex,
                    MorseKeyer.CallsignFlaggedElements,
                    SignalDecoder.CallsignSeenTotal, SignalDecoder.CallsignSeenProcessed,
                    SignalDecoder.ClearCount, SignalDecoder.FirstClearStack,
                    SignalTranscript.DecodeAttempts, SignalTranscript.DecodeSuccess,
                    SignalTranscript.LastBufferLen, SignalTranscript.LastExpectedLen);
            }
            catch (Exception ex)
            {
                return "{\"loaded\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        public static string SetAngle(float angleDeg)
        {
            try
            {
                RadioHandler panel = RadioHandler.Instance;
                if (panel == null)
                    return "{\"ok\":false,\"error\":\"no-panel\"}";
                if (float.IsNaN(angleDeg) || float.IsInfinity(angleDeg))
                    return "{\"ok\":false,\"error\":\"bad-angle\"}";
                panel.CursorAngle = angleDeg;
                return "{\"ok\":true,\"angleDeg\":" + RadioNavProbeJson.Float(angleDeg) + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        // Test-API setters for the MCP bridge (POST /radionav/set). All three
        // drive the same paths the UI/story use: ZoneHandler.SetZone is the
        // public restore entry (bypasses the interactable gate), the power
        // Toggle flip fires ToggleHandler.OnToggleChanged -> RadioPowerBus,
        // and IsVisible is the story-decision property.
        public static string SetZone(int zoneIndex)
        {
            try
            {
                RadioHandler panel = RadioHandler.Instance;
                if (panel == null)
                    return "{\"ok\":false,\"error\":\"no-panel\"}";
                ZoneHandler zone = panel.GetComponentInChildren<ZoneHandler>(true);
                if (zone == null)
                    return "{\"ok\":false,\"error\":\"no-zone-handler\"}";
                zone.SetZone(zoneIndex);
                return "{\"ok\":true,\"zone\":" + zoneIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        public static string SetRadio(bool on)
        {
            try
            {
                RadioHandler panel = RadioHandler.Instance;
                if (panel == null)
                    return "{\"ok\":false,\"error\":\"no-panel\"}";
                Toggle toggle = FindPowerToggle(panel);
                if (toggle == null)
                    return "{\"ok\":false,\"error\":\"no-toggle\"}";
                if (toggle.isOn != on)
                    toggle.isOn = on;
                return "{\"ok\":true,\"radio\":" + (on ? "true" : "false") + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        public static string SetVisible(bool visible)
        {
            try
            {
                RadioHandler panel = RadioHandler.Instance;
                if (panel == null)
                    return "{\"ok\":false,\"error\":\"no-panel\"}";
                panel.IsVisible = visible;
                return "{\"ok\":true,\"visible\":" + (visible ? "true" : "false") + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        private static Toggle FindPowerToggle(RadioHandler panel)
        {
            Transform named = panel.transform.Find(PrefabNames.Toggle);
            if (named != null)
            {
                Toggle byName = named.GetComponent<Toggle>();
                if (byName != null)
                    return byName;
            }
            ToggleHandler[] handlers = panel.GetComponentsInChildren<ToggleHandler>(true);
            for (int i = 0; i < handlers.Length; i++)
            {
                Toggle candidate = handlers[i].GetComponent<Toggle>();
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        private static StationDiagnostics[] CollectDiagnostics(StationManager manager)
        {
            int count = manager.StationCount;
            StationDiagnostics[] diags = new StationDiagnostics[count];
            for (int i = 0; i < count; i++)
            {
                try
                {
                    diags[i] = manager.GetStationDiagnostics(i);
                }
                catch (Exception)
                {
                    diags[i] = default(StationDiagnostics);
                }
            }
            return diags;
        }

        private static void EnsureSubscribed()
        {
            if (subscribed)
                return;
            subscribed = true;
            RadioHandler.OnFrequencyChanged += delegate(float freq, int zone)
            {
                lastFrequencyKHz = freq;
                lastZoneIndex = zone;
            };
        }
    }
}
