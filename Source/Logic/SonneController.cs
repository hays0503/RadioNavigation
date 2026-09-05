using System.IO;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    [RequireComponent(typeof(AudioSource))]
    public class SonneController : MonoBehaviour
    {
        private const int SonneZoneIndex = 1;
        private const float TimeCompressionThreshold = 1.01f;

        private static readonly AudioClip silentClip = AudioClip.Create("SonneSilent", AudioSynthesisEngine.SampleRate * 2, 1, AudioSynthesisEngine.SampleRate, false);

        // Station fleet state + tuning/navigation live in StationManager
        // (extracted, P5.6 slice SC-2); the fleet object stays here for
        // Awake build / OnDestroy unwire. Array instances are shared by
        // reference — the audio thread reads the same instances.
        private StationFleet fleet;
        private StationManager stationManager;
        private AudioSynthesisEngine audioEngine;

        private float currentFrequencyKHz;
        // Main-thread writes (UpdateTuning), audio-thread read
        // (OnAudioFilterRead) — volatile.
        private volatile int currentZoneIndex;
        private bool positionProviderFailureLogged;
        private IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        // F178: cached so OnDestroy does not re-query the component graph.
        private AudioSource cachedAudioSource;
        // Fast-forward must not amplify a live radio signal into noise. Pause
        // intentionally leaves this false: the current tone keeps sounding
        // while scheduler ticks stop with Time.timeScale == 0.
        private volatile bool suppressAudioForTimeCompression;
        // F89/F115 race guard: set before OnDestroy tears the fleet down so a
        // concurrently running OnAudioFilterRead bails out instead of
        // touching half-destroyed arrays.
        private volatile bool tearingDown;

        // Dominant-station decoding lives in SignalDecoder (extracted,
        // P5.6 slice SC-1); the controller keeps the gate inputs.
        private readonly SignalDecoder decoder = new SignalDecoder();

        private float navUpdateTimer;
        private float tuningUpdateTimer;
        private const float NavUpdateInterval = 0.2f;
        private const float TuningUpdateInterval = 0.016f;
        private const float PositionLogInterval = 10f;
        private float positionLogTimer;

        private void Awake()
        {
            suppressAudioForTimeCompression = IsTimeCompressed(Time.timeScale);
            audioEngine = new AudioSynthesisEngine();

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;

            string tableDirectory = Path.Combine(
                Application.persistentDataPath,
                "Mods",
                "RadioNavigation",
                "Source",
                "Data");
            if (!SonneBeam.Initialize(tableDirectory))
                Debug.LogError("[RadioNav][SONNE] Failed to load bearing tables: " + SonneBeam.LastLoadError);

            fleet = new StationFleet(OnStationElementCompleted, OnStationSequenceFinished);
            stationManager = new StationManager(fleet);

            cachedAudioSource = GetComponent<AudioSource>();
            var audio = cachedAudioSource;
            audio.clip = silentClip;
            audio.loop = true;
            audio.Play();

            volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);

            if (volumeSpinner.IsNotNull())
            {
                audioEngine.OutputVolume = SpinnerScale.ToUnit(volumeSpinner.Angle);
                volumeSpinner.OnValueChanged += OnVolumeChanged;
            }

            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.OnValueChanged += OnFeedbackChanged;
        }

        private void OnDestroy()
        {
            // Flag first: the audio thread may be inside OnAudioFilterRead
            // right now (F89/F115).
            tearingDown = true;

            // F89: stop the audio source so OnAudioFilterRead cannot run on a
            // partially-destroyed object during teardown.
            if (cachedAudioSource.IsNotNull())
                cachedAudioSource.Stop();

            // F115: clear accumulated DSP/Atmosphere state so a recreated
            // controller does not inherit QSB phase / AGC gain / filter state.
            if (audioEngine.IsNotNull())
                audioEngine.Reset();

            RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
            if (volumeSpinner.IsNotNull())
                volumeSpinner.OnValueChanged -= OnVolumeChanged;
            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.OnValueChanged -= OnFeedbackChanged;
            if (fleet.IsNotNull())
                fleet.Unwire();
        }

        private void OnStationElementCompleted(int stationIndex, NavigationElementType elemType, bool isCallsign)
        {
            decoder.AddElement(elemType, isCallsign, BeaconData.Stations[stationIndex],
                stationManager.IsDominantAudible(stationIndex, IsInSonneZone));
        }

        private void OnStationSequenceFinished(int stationIndex)
        {
            decoder.FinishSequence(BeaconData.Stations[stationIndex],
                stationManager.IsDominantAudible(stationIndex, IsInSonneZone));
        }

        public string GetSignalRichText()
        {
            return decoder.RichText;
        }

        public void ClearSignalBuffer()
        {
            decoder.Clear();
        }

        public string GetDecodedText()
        {
            return decoder.DecodedText;
        }

        /// <summary>
        /// Counts dots in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDotCount()
        {
            return decoder.CountDots();
        }

        /// <summary>
        /// Counts dashes in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDashCount()
        {
            return decoder.CountDashes();
        }

        private void OnFrequencyChanged(float frequencyKHz, int zoneIndex)
        {
            ClearSignalBuffer();
            UpdateTuning(frequencyKHz, zoneIndex);
        }

        private void OnVolumeChanged(float angle)
        {
            audioEngine.OutputVolume = SpinnerScale.ToUnit(angle);
        }

        private void OnFeedbackChanged(float angle)
        {
            audioEngine.ApplyFeedback(angle);
            UpdateStationGains();
        }

        public void UpdateTuning(float frequencyKHz, int zoneIndex)
        {
            currentFrequencyKHz = frequencyKHz;
            currentZoneIndex = zoneIndex;
            UpdateStationGains();
        }

        public int StationCount => stationManager != null ? stationManager.StationCount : 0;
        public int DominantStationIndex => stationManager != null ? stationManager.DominantStationIndex : -1;

        public StationManager StationManager => stationManager;

        private void Update()
        {
            // Campaign sandbox not loaded (menu / early port load): the ship's
            // SandboxEntity.Position is still the new-game default and GameTime
            // is uninitialized — polling here would log garbage positions.
            if (Sandbox.Instance == null)
                return;

            suppressAudioForTimeCompression = IsTimeCompressed(Time.timeScale);

            // Poll the position feed; UpdatePosition is invoked on significant
            // movement (threshold ~11 m). Without a feed diagnostics stay stale.
            float posLat, posLon;
            if (positionProvider.IsNotNull() && positionProvider.TryGetPosition(out posLat, out posLon))
            {
                positionLogTimer += Time.deltaTime;
                if (positionLogTimer >= PositionLogInterval)
                {
                    positionLogTimer = 0f;
                    Debug.Log(PositionFeed.FormatLogLine(posLat, posLon, GameTimeResolver.GetUtcNow()));
                }

                if (!stationManager.HasPosition
                    || PositionFeed.HasMoved(posLat, posLon, stationManager.PlayerLatitude, stationManager.PlayerLongitude))
                {
                    stationManager.UpdatePosition(posLat, posLon);
                }
            }
            else if (!positionProviderFailureLogged)
            {
                Debug.LogWarning("[RadioNav][POSITION] player position unavailable; bearing diagnostics will remain stale");
                positionProviderFailureLogged = true;
            }

            tuningUpdateTimer += Time.deltaTime;
            if (tuningUpdateTimer >= TuningUpdateInterval)
            {
                tuningUpdateTimer = 0f;
                UpdateStationGains();
            }

            navUpdateTimer += Time.deltaTime;
            if (navUpdateTimer >= NavUpdateInterval)
            {
                navUpdateTimer = 0f;
                stationManager.UpdateNavigation();
            }

            // F01/F29: the scheduler/keyer state machine runs on the main
            // thread; the audio thread only samples the volatile KeyOn flag.
            audioEngine.UpdateStations(Time.deltaTime, Time.time, stationManager.Stations,
                stationManager.Schedulers, stationManager.Keyers, stationManager.Carriers,
                RadioPowerBus.IsRadioOn);
        }

        private void UpdateStationGains()
        {
            stationManager.UpdateGains(IsInSonneZone, currentFrequencyKHz,
                audioEngine.QualityFactor, decoder.Clear);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Audio-thread entry guards: any throw here kills the whole
            // audio graph. During teardown the fleet arrays may already be
            // gone (tearingDown) — bail out before touching them.
            if (tearingDown || data.IsNull() || channels.IsNonPositive())
                return;

            bool audioEnabled = RadioPowerBus.IsRadioOn && !suppressAudioForTimeCompression;
            audioEngine.ProcessAudio(data, channels, stationManager.Stations, stationManager.Keyers,
                stationManager.Carriers, audioEnabled, currentZoneIndex, SonneZoneIndex);
        }

        public bool IsInSonneZone => currentZoneIndex == SonneZoneIndex;

        private static bool IsTimeCompressed(float timeScale) =>
            timeScale > TimeCompressionThreshold;
    }
}
