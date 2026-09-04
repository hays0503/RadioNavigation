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

        private ToggleHandler toggleHandler;
        // F178: RefreshAll runs on a timer; cache the Toggle instead of
        // GetComponent on every refresh.
        private UnityEngine.UI.Toggle cachedToggle;
        private GroundHandler groundHandler;
        private ZoneHandler zoneHandler;
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        private RadioHandler radioHandler;
        private SonneController sonneController;

        private string stationBaseText = "";
        private readonly DecodedDisplayTracker decodedTracker = new DecodedDisplayTracker();
        private float patternUpdateTimer;
        private const float PatternUpdateInterval = 0.5f;
        private const float PatternLabelHeight = 60f;
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

            UIElementFactory.EnsureLayout(content);

            toggleText = UIElementFactory.CreateLabel(content, "ToggleStatus", "Toggle: OFF");
            groundText = UIElementFactory.CreateLabel(content, "GroundStatus", "Ground: Dimmed");
            zoneText = UIElementFactory.CreateLabel(content, "ZoneStatus", "Zone: 1");
            volumeText = UIElementFactory.CreateLabel(content, "VolumeStatus", "Volume: 0%");
            feedbackText = UIElementFactory.CreateLabel(content, "FeedbackStatus", "Feedback: 0%");
            frequencyText = UIElementFactory.CreateLabel(content, "FrequencyStatus", "Freq: ---");
            stationText = UIElementFactory.CreateLabel(content, "StationStatus", "");

            UIElementFactory.CreateSeparator(content, "Sep1");
            signalText = UIElementFactory.CreateLabel(content, "SignalStatus", "--- Signal: ");
            signalText.supportRichText = true;
            signalText.color = Color.cyan;

            decodedText = UIElementFactory.CreateLabel(content, "DecodedStatus", "Decoded: ");
            decodedText.color = new Color(0.8f, 0.8f, 0.5f);

            UIElementFactory.CreateSeparator(content, "Sep2");
            UIElementFactory.CreateLabel(content, "PatternsHeader", "--- Signal Patterns ---");

            // Keep panel rows aligned with the active historical station set.
            patternTexts = new Text[BeaconData.Stations.Length];
            for (int i = 0; i < patternTexts.Length; i++)
            {
                patternTexts[i] = UIElementFactory.CreateLabel(content, "Pattern" + i, "");
                patternTexts[i].verticalOverflow = VerticalWrapMode.Overflow;
                patternTexts[i].GetComponent<RectTransform>().sizeDelta =
                    new Vector2(200f, PatternLabelHeight);
            }

            FindHandlers();
            Subscribe();
            RestorePanelState();
            RefreshAll();
        }

        // F15: apply persisted volume/feedback/zone/cursor so the radio reads
        // as it was left. Called after handlers are found/subscribed so the
        // restored values propagate through the normal pipeline.
        private void RestorePanelState()
        {
            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);
            if (volumeSpinner.IsNotNull())
                volumeSpinner.SetAngle(vol);
            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.SetAngle(fb);
            if (zoneHandler.IsNotNull())
                zoneHandler.SetZone(zone);
            if (radioHandler.IsNotNull())
                radioHandler.CursorAngle = cursor;
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

        private void FindHandlers()
        {
            toggleHandler = GetComponentInChildren<ToggleHandler>(true);
            // F178: cache once; RefreshAll reads it on a timer.
            cachedToggle = toggleHandler.IsNotNull() ? toggleHandler.GetComponent<UnityEngine.UI.Toggle>() : null;
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
            if (cachedToggle.IsNotNull())
                OnToggleChanged(cachedToggle.isOn);
            if (groundHandler.IsNotNull())
                OnGroundChanged(IsGroundActive());
            if (zoneHandler.IsNotNull())
                OnZoneChanged(GetZoneIndex());
            if (volumeSpinner.IsNotNull())
                OnVolumeChanged(volumeSpinner.Angle);
            if (feedbackSpinner.IsNotNull())
                OnFeedbackChanged(feedbackSpinner.Angle);
            if (radioHandler.IsNotNull())
                OnFrequencyChanged(DialConfig.AngleToFrequency(radioHandler.CursorAngle + Constants.CursorOffsetDeg, GetZoneIndex()), GetZoneIndex());
        }

        // F160: read the handler state instead of duplicating alpha-based
        // heuristics with transform.Find string paths.
        private bool IsGroundActive() =>
            groundHandler.IsNotNull() && groundHandler.IsGroundEffectivelyActive();

        private int GetZoneIndex() =>
            zoneHandler.IsNotNull() ? zoneHandler.CurrentIndex : 0;

        private void OnToggleChanged(bool isOn)
        {
            if (toggleText.IsNotNull())
                toggleText.text = StatusLineText.Toggle(isOn);
        }

        private void OnGroundChanged(bool active)
        {
            if (groundText.IsNotNull())
                groundText.text = StatusLineText.Ground(active);
        }

        private void OnZoneChanged(int index)
        {
            if (zoneText.IsNotNull())
                zoneText.text = StatusLineText.Zone(index);
        }

        private void OnVolumeChanged(float angle)
        {
            if (volumeText.IsNotNull())
                volumeText.text = "Volume: " + SpinnerScale.ToPercent(angle) + "%";
        }

        private void OnFeedbackChanged(float angle)
        {
            if (feedbackText.IsNotNull())
                feedbackText.text = "Feedback: " + SpinnerScale.ToPercent(angle) + "%";
        }

        private void OnFrequencyChanged(float freqKHz, int zoneIndex)
        {
            decodedTracker.Reset();

            if (frequencyText.IsNotNull())
                frequencyText.text = FrequencyPanelText.FreqLine(freqKHz, zoneIndex);

            stationBaseText = FrequencyPanelText.StationName(freqKHz, zoneIndex);
            if (stationText.IsNotNull())
                stationText.text = stationBaseText;

            UpdatePatterns();
        }

        private void UpdatePatterns()
        {
            if (sonneController.IsNull() || patternTexts.IsNull())
                return;

            if (!sonneController.IsInSonneZone)
            {
                ClearPatterns();
                return;
            }

            int domIdx = sonneController.DominantStationIndex;
            UpdateStationText(domIdx);

            int count = sonneController.StationCount;
            var diags = new StationDiagnostics[count];
            for (int i = 0; i < count; i++)
                diags[i] = sonneController.GetStationDiagnostics(i);

            int displayIndex = 0;
            foreach (int i in PatternRowPacker.Pack(diags, patternTexts.Length))
            {
                patternTexts[displayIndex].text = BuildPatternLine(diags[i], i, domIdx);

                if (diags[i].InBeam)
                    patternTexts[displayIndex].color = Color.green;
                else
                    patternTexts[displayIndex].color = new Color(0.6f, 0.6f, 0.6f);

                displayIndex++;
            }

            ClearRemainingPatterns(displayIndex);
        }

        private void ClearPatterns()
        {
            if (stationText.IsNotNull())
                stationText.text = stationBaseText;
            for (int i = 0; i < patternTexts.Length; i++)
                if (patternTexts[i].IsNotNull())
                    patternTexts[i].text = "";
        }

        private void UpdateStationText(int domIdx)
        {
            if (stationText.IsNull() || !sonneController.StationCount.IsPositive())
                return;
            // F101: no dominant station (-1) must not show the default
            // diagnostics phase ("[Pause]") of a non-existent station.
            if (domIdx < 0)
            {
                stationText.text = stationBaseText;
                return;
            }
            var diag = sonneController.GetStationDiagnostics(domIdx);
            stationText.text = PatternLineFormatter.StationSuffix(
                stationBaseText, diag.SchedulerPhase, diag.NavigationElapsed);

#if DEBUG
            // F88: phase/diag are scoped to the else branch; the log
            // block used to live outside it and broke DEBUG builds.
            Debug.Log("[RadioNav][PANEL] domIdx=" + domIdx +
                " phase=" + diag.SchedulerPhase.ToString() +
                " navElapsed=" + diag.NavigationElapsed.ToString("F1") +
                " stationText=[" + stationText.text + "]");
#endif
        }

        private string BuildPatternLine(StationDiagnostics diag, int i, int domIdx)
        {
            int recvDots = 0;
            int recvDashes = 0;
            if (i == domIdx)
            {
                recvDots = sonneController.GetReceivedNavigationDotCount();
                recvDashes = sonneController.GetReceivedNavigationDashCount();
            }
            return PatternLineFormatter.BuildLine(diag, i, domIdx, recvDots, recvDashes);
        }

        private void ClearRemainingPatterns(int displayIndex)
        {
            for (int i = displayIndex; i < patternTexts.Length; i++)
                if (patternTexts[i].IsNotNull())
                    patternTexts[i].text = "";
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
                decodedText.text = DecodedDisplayTracker.DisplayText(decoded);

                if (decodedTracker.ShouldLog(decoded))
                {
                    Debug.Log("[RadioNav][PANEL] decoded=[" + decoded + "]");
                    decodedTracker.MarkLogged(decoded);
                }
            }
        }

        private void OnDestroy()
        {
            // F15: persist the current operator state before the panel goes away.
            PanelStateStore.Save(
                volumeSpinner.IsNotNull() ? volumeSpinner.Angle : 0f,
                feedbackSpinner.IsNotNull() ? feedbackSpinner.Angle : 0f,
                GetZoneIndex(),
                radioHandler.IsNotNull() ? radioHandler.CursorAngle : 0f);
            Unsubscribe();
        }
    }
}
