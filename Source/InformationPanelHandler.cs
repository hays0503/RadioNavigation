using System;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
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

        private ToggleHandler toggleHandler;
        private GroundHandler groundHandler;
        private ZoneHandler zoneHandler;
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        private RadioHandler radioHandler;
        private SonneController sonneController;

        private float _currentFreqKHz;
        private int _currentZoneIndex;
        private float _patternUpdateTimer;
        private const float PatternUpdateInterval = 0.5f;
        private float _signalUpdateTimer;
        private const float SignalUpdateInterval = 0.15f;

        private void Start()
        {
            Transform content = transform.Find("InformationPanel/ScrollView/Viewport/Content");
            if (content == null)
            {
                Debug.LogError("[TestUI][InfoPanel] Content not found at InformationPanel/ScrollView/Viewport/Content");
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

            CreateSeparator(content, "Sep2");
            CreateLabel(content, "PatternsHeader", "--- Signal Patterns ---");

            patternTexts = new Text[6];
            for (int i = 0; i < patternTexts.Length; i++)
                patternTexts[i] = CreateLabel(content, "Pattern" + i, "");

            FindHandlers();
            Subscribe();
            RefreshAll();
        }

        private void Update()
        {
            _patternUpdateTimer += Time.deltaTime;
            if (_patternUpdateTimer >= PatternUpdateInterval)
            {
                _patternUpdateTimer = 0f;
                UpdatePatterns();
            }

            _signalUpdateTimer += Time.deltaTime;
            if (_signalUpdateTimer >= SignalUpdateInterval)
            {
                _signalUpdateTimer = 0f;
                UpdateSignalDisplay();
            }
        }

        private static void EnsureLayout(Transform content)
        {
            if (content.GetComponent<VerticalLayoutGroup>() == null)
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

            if (content.GetComponent<ContentSizeFitter>() == null)
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

            if (toggleHandler == null) Debug.LogError("[TestUI][InfoPanel] ToggleHandler not found");
            if (groundHandler == null) Debug.LogError("[TestUI][InfoPanel] GroundHandler not found");
            if (zoneHandler == null) Debug.LogError("[TestUI][InfoPanel] ZoneHandler not found");
            if (volumeSpinner == null) Debug.LogError("[TestUI][InfoPanel] VolumeSpinnerHandler not found");
            if (feedbackSpinner == null) Debug.LogError("[TestUI][InfoPanel] FeedbackSpinnerHandler not found");
            if (radioHandler == null) Debug.LogError("[TestUI][InfoPanel] RadioHandler not found");
            if (sonneController == null) Debug.LogError("[TestUI][InfoPanel] SonneController not found");
        }

        private void Subscribe()
        {
            if (toggleHandler != null)
                toggleHandler.OnValueChanged += OnToggleChanged;
            if (groundHandler != null)
                groundHandler.OnValueChanged += OnGroundChanged;
            if (zoneHandler != null)
                zoneHandler.OnZoneChanged += OnZoneChanged;
            if (volumeSpinner != null)
                volumeSpinner.OnValueChanged += OnVolumeChanged;
            if (feedbackSpinner != null)
                feedbackSpinner.OnValueChanged += OnFeedbackChanged;

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;
        }

        private void Unsubscribe()
        {
            if (toggleHandler != null)
                toggleHandler.OnValueChanged -= OnToggleChanged;
            if (groundHandler != null)
                groundHandler.OnValueChanged -= OnGroundChanged;
            if (zoneHandler != null)
                zoneHandler.OnZoneChanged -= OnZoneChanged;
            if (volumeSpinner != null)
                volumeSpinner.OnValueChanged -= OnVolumeChanged;
            if (feedbackSpinner != null)
                feedbackSpinner.OnValueChanged -= OnFeedbackChanged;

            RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
        }

        private void RefreshAll()
        {
            if (toggleHandler != null)
                OnToggleChanged(toggleHandler.GetComponent<Toggle>().isOn);
            if (groundHandler != null)
                OnGroundChanged(IsGroundActive());
            if (zoneHandler != null)
                OnZoneChanged(GetZoneIndex());
            if (volumeSpinner != null)
                OnVolumeChanged(volumeSpinner.Angle);
            if (feedbackSpinner != null)
                OnFeedbackChanged(feedbackSpinner.Angle);
            if (radioHandler != null)
                OnFrequencyChanged(DialConfig.AngleToFrequency(radioHandler.CursorAngle + 90f, GetZoneIndex()), GetZoneIndex());
        }

        private bool IsGroundActive()
        {
            Transform bgT = transform.Find("Ground/Background");
            if (bgT == null) return false;
            Image bgImage = bgT.GetComponent<Image>();
            return bgImage != null && bgImage.color.a > GroundHandler.ActiveAlphaThreshold;
        }

        private int GetZoneIndex()
        {
            Transform radioZone = transform.Find("RadioZone");
            if (radioZone == null) return 0;
            for (int i = 0; i < 5; i++)
            {
                Transform t = radioZone.Find("Zone" + (i + 1));
                if (t == null) continue;
                Image img = t.GetComponent<Image>();
                if (img != null && img.color.a > 0.5f)
                    return i;
            }
            return 0;
        }

        private void OnToggleChanged(bool isOn)
        {
            if (toggleText != null)
                toggleText.text = "Toggle: " + (isOn ? "ON" : "OFF");
        }

        private void OnGroundChanged(bool active)
        {
            if (groundText != null)
                groundText.text = "Ground: " + (active ? "Active" : "Dimmed");
        }

        private void OnZoneChanged(int index)
        {
            if (zoneText != null)
                zoneText.text = "Zone: " + (index + 1);
        }

        private void OnVolumeChanged(float angle)
        {
            if (volumeText != null)
                volumeText.text = "Volume: " + Mathf.RoundToInt(angle / 140f * 100f) + "%";
        }

        private void OnFeedbackChanged(float angle)
        {
            if (feedbackText != null)
                feedbackText.text = "Feedback: " + Mathf.RoundToInt(angle / 140f * 100f) + "%";
        }

        private void OnFrequencyChanged(float freqKHz, int zoneIndex)
        {
            _currentFreqKHz = freqKHz;
            _currentZoneIndex = zoneIndex;

            string formatted = DialConfig.FormatFrequency(freqKHz, zoneIndex);
            string bandId = zoneIndex >= 0 && zoneIndex < DialConfig.Bands.Length
                ? DialConfig.Bands[zoneIndex].Id : "?";
            if (frequencyText != null)
                frequencyText.text = "Freq: " + formatted + " (Band " + bandId + ")";

            string stationName = FindClosestStation(freqKHz, zoneIndex);
            if (stationText != null)
                stationText.text = stationName;

            UpdatePatterns();
        }

        private void UpdatePatterns()
        {
            if (sonneController == null || patternTexts == null)
                return;

            if (_currentZoneIndex != 1)
            {
                for (int i = 0; i < patternTexts.Length; i++)
                    if (patternTexts[i] != null)
                        patternTexts[i].text = "";
                return;
            }

            int displayIndex = 0;
            for (int i = 0; i < sonneController.StationCount && displayIndex < patternTexts.Length; i++)
            {
                var diag = sonneController.GetStationDiagnostics(i);
                if (diag.Gain < 0.001f)
                    continue;

                string name = diag.Name ?? "?";
                if (name.Length > 12)
                    name = name.Substring(0, 12);

                string beam = diag.InBeam ? "IN BEAM" : "OUTSIDE";
                string bfo = diag.BeatFrequency.ToString("F0") + "Hz";
                string gain = Mathf.RoundToInt(diag.Gain * 100f) + "%";
                string phase = diag.SchedulerPhase.ToString();

                string line = name + " [" + beam + "] " + gain + " BFO:" + bfo + " " + phase;
                if (diag.InBeam)
                    line += "\n  Dots:" + diag.DotCount + " Bearing:" + diag.CurrentBearing + "°";

                patternTexts[displayIndex].text = line;

                if (diag.InBeam)
                    patternTexts[displayIndex].color = Color.green;
                else
                    patternTexts[displayIndex].color = new Color(0.6f, 0.6f, 0.6f);

                displayIndex++;
            }

            for (int i = displayIndex; i < patternTexts.Length; i++)
                if (patternTexts[i] != null)
                    patternTexts[i].text = "";
        }

        private void UpdateSignalDisplay()
        {
            if (sonneController == null || signalText == null)
                return;

            if (_currentZoneIndex != 1)
            {
                signalText.text = "--- Signal: ";
                signalText.color = Color.gray;
                return;
            }

            string rich = sonneController.GetSignalRichText();
            if (string.IsNullOrEmpty(rich))
            {
                signalText.text = "--- Signal: ";
                signalText.color = Color.gray;
                return;
            }

            signalText.text = "--- Signal: " + rich;
            signalText.color = Color.white;
        }

        private string FindClosestStation(float freqKHz, int zoneIndex)
        {
            if (zoneIndex != 1)
                return "";

            float bestDelta = float.MaxValue;
            float bestFreq = 0f;
            string bestName = null;

            var stations = BeaconData.Stations;
            for (int i = 0; i < stations.Length; i++)
            {
                float? stationFreq = stations[i].History?[0].FrequencyKHz;
                if (stationFreq == null) continue;

                float delta = Math.Abs(freqKHz - stationFreq.Value);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFreq = stationFreq.Value;
                    bestName = stations[i].Name;
                }
            }

            if (bestName == null || bestDelta > 20f)
                return "";

            float gain = Mathf.Exp(-(bestDelta * bestDelta) / (2f * 1.5f * 1.5f));
            return bestName + " " + DialConfig.FormatFrequency(bestFreq, zoneIndex) + " " + Mathf.RoundToInt(gain * 100f) + "%";
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
