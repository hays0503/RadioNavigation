using System;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public class InformationPanelHandler : MonoBehaviour
    {
        private Text toggleText;
        private Text groundText;
        private Text zoneText;
        private Text volumeText;
        private Text feedbackText;
        private Text frequencyText;
        private Text stationText;
        private Text[] patternTexts;
        private Text signalText;
        private Text decodedText;
        private Text fixText;

        private ToggleHandler toggleHandler;
        private GroundHandler groundHandler;
        private ZoneHandler zoneHandler;
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        private RadioHandler radioHandler;
        private SonneController sonneController;

        private float currentFreqKHz;
        private int currentZoneIndex;
        private string stationBaseText = "";
        private float patternUpdateTimer;
        private const float PatternUpdateInterval = 0.5f;
        private float signalUpdateTimer;
        private const float SignalUpdateInterval = 0.15f;

        private void Start()
        {
            Transform content = transform.Find("InformationPanel/ScrollView/Viewport/Content");
            if (content.IsNull())
            {
                Debug.LogError("[RadioNav][InfoPanel] Content not found at InformationPanel/ScrollView/Viewport/Content");
                enabled = false;
                return;
            }

            EnsureLayout(content);

            toggleText = CreateLabel(content, "ToggleStatus", "Toggle: OFF");
            groundText = CreateLabel(content, "GroundStatus", "Ground: Dimmed");
            zoneText = CreateLabel(content, "ZoneStatus", "Zone: 1");
            volumeText = CreateLabel(content, "VolumeStatus", "Volume: 0%");
            feedbackText = CreateLabel(content, "FeedbackStatus", "Feedback: 0%");
            frequencyText = CreateLabel(content, "FrequencyStatus", "Freq: ---");
            stationText = CreateLabel(content, "StationStatus", "");

            CreateSeparator(content, "Sep1");
            signalText = CreateLabel(content, "SignalStatus", "--- Signal: ");
            signalText.supportRichText = true;
            signalText.color = Color.cyan;

            decodedText = CreateLabel(content, "DecodedStatus", "Decoded: ");
            decodedText.color = new Color(0.8f, 0.8f, 0.5f);

            CreateSeparator(content, "Sep2");
            CreateLabel(content, "PatternsHeader", "--- Signal Patterns ---");

            patternTexts = new Text[6];
            for (int i = 0; i < patternTexts.Length; i++)
                patternTexts[i] = CreateLabel(content, "Pattern" + i, "");

            CreateSeparator(content, "Sep3");
            fixText = CreateLabel(content, "FixStatus", "--- Position Fix: ---");
            fixText.supportRichText = true;
            fixText.color = Color.yellow;

            FindHandlers();
            Subscribe();
            RefreshAll();
        }

        private void Update()
        {
            patternUpdateTimer += Time.deltaTime;
            if (patternUpdateTimer >= PatternUpdateInterval)
            {
                patternUpdateTimer = 0f;
                UpdatePatterns();
            }

            signalUpdateTimer += Time.deltaTime;
            if (signalUpdateTimer >= SignalUpdateInterval)
            {
                signalUpdateTimer = 0f;
                UpdateSignalDisplay();
            }
        }

        private static void EnsureLayout(Transform content)
        {
            if (content.GetComponent<VerticalLayoutGroup>().IsNull())
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(8, 8, 8, 8);
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;
            }

            if (content.GetComponent<ContentSizeFitter>().IsNull())
            {
                var csf = content.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private static Text CreateLabel(Transform parent, string name, string defaultText)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(200, 20);

            var text = go.AddComponent<Text>();
            text.text = defaultText;
            text.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            return text;
        }

        private static void CreateSeparator(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 2);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.2f);
        }

        private void FindHandlers()
        {
            toggleHandler = GetComponentInChildren<ToggleHandler>(true);
            groundHandler = GetComponentInChildren<GroundHandler>(true);
            zoneHandler = GetComponentInChildren<ZoneHandler>(true);
            volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);
            radioHandler = GetComponent<RadioHandler>();
            sonneController = GetComponent<SonneController>();

            if (toggleHandler.IsNull()) Debug.LogError("[RadioNav][InfoPanel] ToggleHandler not found");
            if (groundHandler.IsNull()) Debug.LogError("[RadioNav][InfoPanel] GroundHandler not found");
            if (zoneHandler.IsNull()) Debug.LogError("[RadioNav][InfoPanel] ZoneHandler not found");
            if (volumeSpinner.IsNull()) Debug.LogError("[RadioNav][InfoPanel] VolumeSpinnerHandler not found");
            if (feedbackSpinner.IsNull()) Debug.LogError("[RadioNav][InfoPanel] FeedbackSpinnerHandler not found");
            if (radioHandler.IsNull()) Debug.LogError("[RadioNav][InfoPanel] RadioHandler not found");
            if (sonneController.IsNull()) Debug.LogError("[RadioNav][InfoPanel] SonneController not found");
        }

        private void Subscribe()
        {
            if (toggleHandler.IsNotNull())
                toggleHandler.OnValueChanged += OnToggleChanged;
            if (groundHandler.IsNotNull())
                groundHandler.OnValueChanged += OnGroundChanged;
            if (zoneHandler.IsNotNull())
                zoneHandler.OnZoneChanged += OnZoneChanged;
            if (volumeSpinner.IsNotNull())
                volumeSpinner.OnValueChanged += OnVolumeChanged;
            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.OnValueChanged += OnFeedbackChanged;

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;
        }

        private void Unsubscribe()
        {
            if (toggleHandler.IsNotNull())
                toggleHandler.OnValueChanged -= OnToggleChanged;
            if (groundHandler.IsNotNull())
                groundHandler.OnValueChanged -= OnGroundChanged;
            if (zoneHandler.IsNotNull())
                zoneHandler.OnZoneChanged -= OnZoneChanged;
            if (volumeSpinner.IsNotNull())
                volumeSpinner.OnValueChanged -= OnVolumeChanged;
            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.OnValueChanged -= OnFeedbackChanged;

            RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
        }

        private void RefreshAll()
        {
            if (toggleHandler.IsNotNull())
                OnToggleChanged(toggleHandler.GetComponent<Toggle>().isOn);
            if (groundHandler.IsNotNull())
                OnGroundChanged(IsGroundActive());
            if (zoneHandler.IsNotNull())
                OnZoneChanged(GetZoneIndex());
            if (volumeSpinner.IsNotNull())
                OnVolumeChanged(volumeSpinner.Angle);
            if (feedbackSpinner.IsNotNull())
                OnFeedbackChanged(feedbackSpinner.Angle);
            if (radioHandler.IsNotNull())
                OnFrequencyChanged(DialConfig.AngleToFrequency(radioHandler.CursorAngle + 90f, GetZoneIndex()), GetZoneIndex());
        }

        private bool IsGroundActive()
        {
            Transform bgT = transform.Find("Ground/Background");
            if (bgT.IsNull()) return false;
            Image bgImage = bgT.GetComponent<Image>();
            return bgImage.IsNotNull() && bgImage.color.a > GroundHandler.ActiveAlphaThreshold;
        }

        private int GetZoneIndex()
        {
            Transform radioZone = transform.Find("RadioZone");
            if (radioZone.IsNull()) return 0;
            for (int i = 0; i < 5; i++)
            {
                Transform t = radioZone.Find("Zone" + (i + 1));
                if (t.IsNull()) continue;
                Image img = t.GetComponent<Image>();
                if (img.IsNotNull() && img.color.a > 0.5f)
                    return i;
            }
            return 0;
        }

        private void OnToggleChanged(bool isOn)
        {
            if (toggleText.IsNotNull())
                toggleText.text = "Toggle: " + (isOn ? "ON" : "OFF");
        }

        private void OnGroundChanged(bool active)
        {
            if (groundText.IsNotNull())
                groundText.text = "Ground: " + (active ? "Active" : "Dimmed");
        }

        private void OnZoneChanged(int index)
        {
            if (zoneText.IsNotNull())
                zoneText.text = "Zone: " + (index + 1);
        }

        private void OnVolumeChanged(float angle)
        {
            if (volumeText.IsNotNull())
                volumeText.text = "Volume: " + Mathf.RoundToInt(angle / 140f * 100f) + "%";
        }

        private void OnFeedbackChanged(float angle)
        {
            if (feedbackText.IsNotNull())
                feedbackText.text = "Feedback: " + Mathf.RoundToInt(angle / 140f * 100f) + "%";
        }

        private void OnFrequencyChanged(float freqKHz, int zoneIndex)
        {
            currentFreqKHz = freqKHz;
            currentZoneIndex = zoneIndex;

            string formatted = DialConfig.FormatFrequency(freqKHz, zoneIndex);
            string bandId = zoneIndex.IsNonNegative() && zoneIndex < DialConfig.Bands.Length
                ? DialConfig.Bands[zoneIndex].Id : "?";
            if (frequencyText.IsNotNull())
                frequencyText.text = "Freq: " + formatted + " (Band " + bandId + ")";

            string stationName = FindClosestStation(freqKHz, zoneIndex);
            stationBaseText = stationName;
            if (stationText.IsNotNull())
                stationText.text = stationName;

            UpdatePatterns();
        }

        private void UpdatePatterns()
        {
            if (sonneController.IsNull() || patternTexts.IsNull())
                return;

            if (!sonneController.IsInSonneZone)
            {
                if (stationText.IsNotNull())
                    stationText.text = stationBaseText;
                for (int i = 0; i < patternTexts.Length; i++)
                    if (patternTexts[i].IsNotNull())
                        patternTexts[i].text = "";
                return;
            }

            if (stationText.IsNotNull() && sonneController.StationCount.IsPositive())
            {
                int domIdx = sonneController.DominantStationIndex;
                var diag = sonneController.GetStationDiagnostics(domIdx);
                string phase = diag.SchedulerPhase.ToString();
                string suffix = " [" + phase + "]";
                if (diag.NavigationElapsed.IsPositive())
                    suffix += " " + FormatTime(diag.NavigationElapsed);
                stationText.text = stationBaseText + suffix;

#if DEBUG
                Debug.Log("[RadioNav][PANEL] domIdx=" + domIdx +
                    " phase=" + phase +
                    " navElapsed=" + diag.NavigationElapsed.ToString("F1") +
                    " stationText=[" + stationText.text + "]");
#endif
            }

            int displayIndex = 0;
            for (int i = 0; i < sonneController.StationCount && displayIndex < patternTexts.Length; i++)
            {
                var diag = sonneController.GetStationDiagnostics(i);
                if (diag.Gain.IsNegligible(0.001f))
                    continue;

                string callsign = diag.Callsign ?? "?";
                string beam = diag.InBeam ? "IN BEAM" : "OUTSIDE";
                string bfo = diag.BeatFrequency.ToString("F0") + "Hz";
                string gain = Mathf.RoundToInt(diag.Gain * 100f) + "%";
                string phase = diag.SchedulerPhase.ToString();

                string line = callsign + " [" + beam + "] " + gain + " BFO:" + bfo + " " + phase;
                if (diag.NavigationElapsed.IsPositive())
                    line += " " + FormatTime(diag.NavigationElapsed);
                if (diag.InBeam)
                    line += "\n  Dots:" + diag.DotCount + " Bearing:" + diag.CurrentBearing + "В°";

                patternTexts[displayIndex].text = line;

                if (diag.InBeam)
                    patternTexts[displayIndex].color = Color.green;
                else
                    patternTexts[displayIndex].color = new Color(0.6f, 0.6f, 0.6f);

                displayIndex++;
            }

            for (int i = displayIndex; i < patternTexts.Length; i++)
                if (patternTexts[i].IsNotNull())
                    patternTexts[i].text = "";

            UpdateFixDisplay();
        }

        private void UpdateFixDisplay()
        {
            if (sonneController.IsNull() || fixText.IsNull())
                return;

            if (!sonneController.IsInSonneZone || !sonneController.HasFix)
            {
                fixText.text = "--- Position Fix: ---";
                fixText.color = Color.gray;
                return;
            }

            float lat = sonneController.FixLatitude;
            float lon = sonneController.FixLongitude;
            float err = sonneController.FixErrorKm;
            int meas = sonneController.MeasurementCount;

            string latDir = lat.IsNonNegative() ? "N" : "S";
            string lonDir = lon.IsNonNegative() ? "E" : "W";

            fixText.text = "--- Position Fix ---\n" +
                "Fix: " + Math.Abs(lat).ToString("F4") + "В° " + latDir +
                " " + Math.Abs(lon).ToString("F4") + "В° " + lonDir + "\n" +
                "Beacons: " + meas + " | Error: " + err.ToString("F1") + " km";
            fixText.color = err < 15f ? Color.green : (err < 35f ? Color.yellow : Color.red);
        }

        private void UpdateSignalDisplay()
        {
            if (sonneController.IsNull() || signalText.IsNull())
                return;

            if (!sonneController.IsInSonneZone)
            {
                signalText.text = "--- Signal: ";
                signalText.color = Color.gray;
                if (decodedText.IsNotNull())
                    decodedText.text = "Decoded: ";
                return;
            }

            string rich = sonneController.GetSignalRichText();
            if (string.IsNullOrEmpty(rich))
            {
                signalText.text = "--- Signal: ";
                signalText.color = Color.gray;
                if (decodedText.IsNotNull())
                    decodedText.text = "Decoded: ";
                return;
            }

            signalText.text = "--- Signal: " + rich;
            signalText.color = Color.white;

            if (decodedText.IsNotNull())
            {
                string decoded = sonneController.GetDecodedText();
                decodedText.text = string.IsNullOrEmpty(decoded) ? "Decoded: " : "Decoded: " + decoded;

                if (!string.IsNullOrEmpty(decoded))
                    Debug.Log("[RadioNav][PANEL] decoded=[" + decoded + "]");
            }
        }

        private string FormatTime(float seconds)
        {
            int s = Mathf.RoundToInt(seconds);
            int min = s / 60;
            int sec = s % 60;
            return min.ToString("D2") + ":" + sec.ToString("D2");
        }

        private string FindClosestStation(float freqKHz, int zoneIndex)
        {
            if (zoneIndex != 1)
                return "";

            float bestDelta = float.MaxValue;
            float bestFreq = 0f;
            string bestName = null;
            string bestCallsign = null;

            var stations = BeaconData.Stations;
            for (int i = 0; i < stations.Length; i++)
            {
                var history = stations[i].History;
                if (history.IsNull() || history.Length.IsZero()) continue;
                float? stationFreq = history[0].FrequencyKHz;
                if (stationFreq.IsNull()) continue;

                float delta = Math.Abs(freqKHz - stationFreq.Value);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFreq = stationFreq.Value;
                    bestName = stations[i].Name;
                    string cs = history[0].Callsign;
                    if (!string.IsNullOrEmpty(cs))
                        bestCallsign = cs;
                }
            }

            if (bestName.IsNull() || bestDelta > 20f)
                return "";

            float gain = Mathf.Exp(-(bestDelta * bestDelta) / (2f * 1.5f * 1.5f));
            string displayName = !string.IsNullOrEmpty(bestCallsign) ? bestCallsign : bestName;
            return displayName + " " + DialConfig.FormatFrequency(bestFreq, zoneIndex) + " " + Mathf.RoundToInt(gain * 100f) + "%";
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
