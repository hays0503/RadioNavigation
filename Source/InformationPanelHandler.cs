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

        private float currentAngle;
        private int currentZoneIndex;

        private ToggleHandler toggleHandler;
        private GroundHandler groundHandler;
        private ZoneHandler zoneHandler;
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        private RadioHandler radioHandler;

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

            FindHandlers();

            Subscribe();

            RefreshAll();
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

        private void FindHandlers()
        {
            toggleHandler = GetComponentInChildren<ToggleHandler>(true);
            groundHandler = GetComponentInChildren<GroundHandler>(true);
            zoneHandler = GetComponentInChildren<ZoneHandler>(true);
            volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);
            radioHandler = GetComponent<RadioHandler>();

            if (toggleHandler == null) Debug.LogError("[TestUI][InfoPanel] ToggleHandler not found");
            if (groundHandler == null) Debug.LogError("[TestUI][InfoPanel] GroundHandler not found");
            if (zoneHandler == null) Debug.LogError("[TestUI][InfoPanel] ZoneHandler not found");
            if (volumeSpinner == null) Debug.LogError("[TestUI][InfoPanel] VolumeSpinnerHandler not found");
            if (feedbackSpinner == null) Debug.LogError("[TestUI][InfoPanel] FeedbackSpinnerHandler not found");
            if (radioHandler == null) Debug.LogError("[TestUI][InfoPanel] RadioHandler not found");
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
            if (radioHandler != null)
                radioHandler.OnCursorAngleChanged += OnDialChanged;
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
            if (radioHandler != null)
                radioHandler.OnCursorAngleChanged -= OnDialChanged;
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
                OnDialChanged(radioHandler.CursorAngle);
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
            currentZoneIndex = index;
            if (zoneText != null)
                zoneText.text = "Zone: " + (index + 1);
            UpdateFrequencyDisplay();
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

        private void OnDialChanged(float angle)
        {
            currentAngle = angle + 90f;
            UpdateFrequencyDisplay();
        }

        private void UpdateFrequencyDisplay()
        {
            if (frequencyText == null)
                return;

            float freq = DialConfig.AngleToFrequency(currentAngle, currentZoneIndex);
            string formatted = DialConfig.FormatFrequency(freq, currentZoneIndex);
            string bandId = currentZoneIndex >= 0 && currentZoneIndex < DialConfig.Bands.Length
                ? DialConfig.Bands[currentZoneIndex].Id : "?";
            frequencyText.text = "Freq: " + formatted + " (Band " + bandId + ")  Angle: " + currentAngle.ToString("F1") + "°";
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
