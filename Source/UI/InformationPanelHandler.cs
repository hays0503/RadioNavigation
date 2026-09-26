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

        // Last-known operator state, refreshed in Update while components
        // are alive; OnDestroy saves from this cache (teardown-order safe).
        private float cachedVolumeAngle = -1f;
        private float cachedFeedbackAngle = -1f;
        private int cachedZoneIndex = -1;
        private float cachedCursorAngle = -1f;
        // Cursor range is -90..90, so -1 is a VALID angle and cannot double as a
        // sentinel (a cursor at -45 would be discarded on teardown). Track
        // validity explicitly instead.
        private bool hasCachedCursor;
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

        private void RestorePanelState()
        {
            PanelState.Restore(
                volumeSpinner.IsNotNull() ? (Action<float>)volumeSpinner.SetAngle : null,
                feedbackSpinner.IsNotNull() ? (Action<float>)feedbackSpinner.SetAngle : null,
                zoneHandler.IsNotNull() ? (Action<int>)zoneHandler.SetZone : null,
                radioHandler.IsNotNull() ? (Action<float>)(v => radioHandler.CursorAngle = v) : null);
        }

        private void Update()
        {
            // Live cache for the OnDestroy save: scene teardown may destroy
            // RadioHandler/ZoneHandler BEFORE this component's OnDestroy, and
            // reading a destroyed component yields fake-null -> zeros written
            // to PlayerPrefs (persisted cursorAngle came back as ~0).
            if (volumeSpinner.IsNotNull())
                cachedVolumeAngle = volumeSpinner.Angle;
            if (feedbackSpinner.IsNotNull())
                cachedFeedbackAngle = feedbackSpinner.Angle;
            if (zoneHandler.IsNotNull())
                cachedZoneIndex = zoneHandler.CurrentIndex;
            if (radioHandler.IsNotNull())
            {
                cachedCursorAngle = radioHandler.CursorAngle;
                hasCachedCursor = true;
            }

            patternUpdateTimer += Time.unscaledDeltaTime;
            if (patternUpdateTimer >= PatternUpdateInterval)
            {
                patternUpdateTimer = 0f;
                UpdatePatterns();
            }

            signalUpdateTimer += Time.unscaledDeltaTime;
            if (signalUpdateTimer >= SignalUpdateInterval)
            {
                signalUpdateTimer = 0f;
                UpdateSignalDisplay();
            }
        }

        private void SaveCachedState()
        {
            PanelState.Save(
                cachedVolumeAngle >= 0f ? cachedVolumeAngle :
                    (volumeSpinner.IsNotNull() ? volumeSpinner.Angle : 0f),
                cachedFeedbackAngle >= 0f ? cachedFeedbackAngle :
                    (feedbackSpinner.IsNotNull() ? feedbackSpinner.Angle : 0f),
                cachedZoneIndex >= 0 ? cachedZoneIndex :
                    (zoneHandler.IsNotNull() ? zoneHandler.CurrentIndex : 0),
                hasCachedCursor ? cachedCursorAngle :
                    (radioHandler.IsNotNull() ? radioHandler.CursorAngle : 0f));
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
                diags[i] = sonneController.StationManager.GetStationDiagnostics(i);

            int recvDots = 0;
            int recvDashes = 0;
            if (domIdx >= 0)
            {
                recvDots = sonneController.GetReceivedNavigationDotCount();
                recvDashes = sonneController.GetReceivedNavigationDashCount();
            }

            PanelViewBinder.PatternRow[] rows = PanelViewBinder.BuildPatternRows(
                diags, domIdx, recvDots, recvDashes, patternTexts.Length);
            for (int r = 0; r < rows.Length; r++)
            {
                patternTexts[r].text = rows[r].Text;

                if (rows[r].InBeam)
                    patternTexts[r].color = Color.green;
                else
                    patternTexts[r].color = new Color(0.6f, 0.6f, 0.6f);
            }

            ClearRemainingPatterns(rows.Length);
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
            StationDiagnostics domDiag = default(StationDiagnostics);
            if (domIdx >= 0)
                domDiag = sonneController.StationManager.GetStationDiagnostics(domIdx);
            stationText.text = PanelViewBinder.BuildStationText(stationBaseText, domIdx, domDiag);

#if DEBUG
            // F88: phase/diag are scoped to the else branch; the log
            // block used to live outside it and broke DEBUG builds.
            Debug.Log("[RadioNav][PANEL] domIdx=" + domIdx +
                " phase=" + domDiag.SchedulerPhase.ToString() +
                " navElapsed=" + domDiag.NavigationElapsed.ToString("F1") +
                " stationText=[" + stationText.text + "]");
#endif
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

            bool inZone = sonneController.IsInSonneZone;
            string rich = inZone ? sonneController.GetSignalRichText() : null;
            string decoded = inZone ? sonneController.GetDecodedText() : null;

            SignalPanelView.State view = PanelViewBinder.BuildSignalView(rich, decoded);
            signalText.text = view.SignalText;
            signalText.color = view.Dimmed ? Color.gray : Color.white;

            if (decodedText.IsNotNull())
            {
                decodedText.text = view.DecodedText;

                if (decodedTracker.ShouldLog(decoded))
                {
                    Debug.Log("[RadioNav][PANEL] decoded=[" + decoded + "]");
                    decodedTracker.MarkLogged(decoded);
                }
            }
        }

        private void OnDestroy()
        {
            SaveCachedState();
            Unsubscribe();
        }
    }
}
