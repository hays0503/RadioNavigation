using System;
using System.Globalization;
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

        private int dominantStationIndex = -1;
        // Station object fleet (single backing store; built in Awake).
        private StationFleet fleet;
        private StationState[] stations;
        private StationScheduler[] schedulers;
        private MorseKeyer[] keyers;
        private CarrierGenerator[] carriers;
        private AudioSynthesisEngine audioEngine;

        private float currentFrequencyKHz;
        private int currentZoneIndex;
        private float playerLatitude;
        private float playerLongitude;
        private bool positionAvailable;
        private bool positionProviderFailureLogged;
        private bool bearingDiagnosticsLogged;
        private float lastBearingLogLatitude;
        private float lastBearingLogLongitude;
        private IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;
        // F178: cached so OnDestroy does not re-query the component graph.
        private AudioSource cachedAudioSource;
        // Fast-forward must not amplify a live radio signal into noise. Pause
        // intentionally leaves this false: the current tone keeps sounding
        // while scheduler ticks stop with Time.timeScale == 0.
        private volatile bool suppressAudioForTimeCompression;

        // Live signal transcript (entries, morse buffer, decoded callsign).
        private readonly SignalTranscript transcript = new SignalTranscript();

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
            stations = fleet.States;
            schedulers = fleet.Schedulers;
            keyers = fleet.Keyers;
            carriers = fleet.Carriers;

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
            if (!IsProcessingDominantStation(stationIndex))
                return;

            transcript.AddElement(elemType, isCallsign, BeaconData.Stations[stationIndex]);
        }

        private void OnStationSequenceFinished(int stationIndex)
        {
            if (!IsProcessingDominantStation(stationIndex))
                return;

            transcript.FinishSequence(BeaconData.Stations[stationIndex]);
        }

        public string GetSignalRichText()
        {
            return transcript.RichText;
        }

        public void ClearSignalBuffer()
        {
            transcript.Clear();
        }

        public string GetDecodedText()
        {
            return transcript.DecodedText;
        }

        /// <summary>
        /// Counts dots in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDotCount()
        {
            return transcript.CountDots();
        }

        /// <summary>
        /// Counts dashes in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDashCount()
        {
            return transcript.CountDashes();
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

        public int StationCount => stations.IsNotNull() ? stations.Length : 0;
        public int DominantStationIndex => dominantStationIndex;

        public StationDiagnostics GetStationDiagnostics(int index)
        {
            if (IsInvalidStationIndex(index))
                return new StationDiagnostics();

            var s = stations[index];
            var station = BeaconData.Stations[index];
            StationCallsignResolver.TryGet(station, out string callsign);

            float elapsed = 0f;
            if (IsStationNavigating(index))
                elapsed = Time.time - s.NavigationStartTime;

            return StationDiagnosticsReport.Build(
                station, callsign, s, schedulers[index].CurrentPhase, elapsed);
        }

        public void UpdatePosition(float latitude, float longitude)
        {
            if (!IsFinite(latitude) || !IsFinite(longitude))
            {
                positionAvailable = false;
                return;
            }

            playerLatitude = latitude;
            playerLongitude = longitude;
            positionAvailable = true;
            UpdateStationNavigation();
        }

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

                if (!positionAvailable
                    || PositionFeed.HasMoved(posLat, posLon, playerLatitude, playerLongitude))
                {
                    UpdatePosition(posLat, posLon);
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
                UpdateStationNavigation();
            }

            // F01/F29: the scheduler/keyer state machine runs on the main
            // thread; the audio thread only samples the volatile KeyOn flag.
            audioEngine.UpdateStations(Time.deltaTime, Time.time, stations, schedulers, keyers, carriers, RadioPowerBus.IsRadioOn);
        }

        private void UpdateStationGains()
        {
            if (!IsInSonneZone)
            {
                for (int i = 0; i < stations.Length; i++)
                    stations[i].Gain = 0f;
                dominantStationIndex = -1;
                return;
            }

            // F09/F58: zero or NaN carrier frequency must not enter the gain
            // math (division by zero / NaN ratio). Silence all stations
            // instead of leaving stale gains behind.
            float f = currentFrequencyKHz * 1000f;
            if (f.IsNegligible(0.001f) || float.IsNaN(f))
            {
                for (int i = 0; i < stations.Length; i++)
                    stations[i].Gain = 0f;
                dominantStationIndex = -1;
                return;
            }

            var beaconStations = BeaconData.Stations;
            float maxGain = 0f;
            int maxIndex = -1;
            float qf = audioEngine.QualityFactor;

            for (int i = 0; i < beaconStations.Length; i++)
            {
                if (beaconStations[i].Role != BeaconStationRole.DirectionalSonne)
                {
                    stations[i].Gain = 0f;
                    continue;
                }

                float? freq = beaconStations[i].History?[0].FrequencyKHz;
                if (freq.IsNull())
                {
                    stations[i].Gain = 0f;
                    continue;
                }

                float gain = StationGainModel.ComputeGain(currentFrequencyKHz, freq.Value, qf);

                stations[i].Gain = gain;

                float offset = currentFrequencyKHz - freq.Value;
                stations[i].BeatFrequency = StationGainModel.ComputeBeatFrequency(offset);
                carriers[i].Frequency = stations[i].BeatFrequency;

                if (gain > maxGain)
                {
                    maxGain = gain;
                    maxIndex = i;
                }
            }

            if (maxGain.IsSignificant(0.01f))
            {
                if (maxIndex != dominantStationIndex)
                {
                    transcript.Clear();
                }
                dominantStationIndex = maxIndex;
            }
            else
            {
                dominantStationIndex = -1;
            }
        }

        private void UpdateStationNavigation()
        {
            var beaconStations = BeaconData.Stations;

            for (int i = 0; i < beaconStations.Length; i++)
            {
                StationNavigator.UpdateStation(
                    ref stations[i], beaconStations[i],
                    playerLatitude, playerLongitude, schedulers[i]);
            }

            LogBearingDiagnostics(beaconStations);
        }

        private void LogBearingDiagnostics(BeaconStation[] beaconStations)
        {
            if (!positionAvailable
                || (bearingDiagnosticsLogged
                    && Math.Abs(playerLatitude - lastBearingLogLatitude) < 0.00001f
                    && Math.Abs(playerLongitude - lastBearingLogLongitude) < 0.00001f))
                return;

            foreach (string line in BearingDiagnostics.BuildLines(
                playerLatitude, playerLongitude, beaconStations, stations))
            {
                Debug.Log(line);
            }

            lastBearingLogLatitude = playerLatitude;
            lastBearingLogLongitude = playerLongitude;
            bearingDiagnosticsLogged = true;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            bool audioEnabled = RadioPowerBus.IsRadioOn && !suppressAudioForTimeCompression;
            audioEngine.ProcessAudio(data, channels, stations, keyers, carriers, audioEnabled, currentZoneIndex, SonneZoneIndex);
        }

        public bool IsInSonneZone => currentZoneIndex == SonneZoneIndex;

        private bool IsInvalidStationIndex(int index) =>
            stations.IsNull() || index.IsNegative() || index >= stations.Length;

        private bool IsProcessingDominantStation(int stationIndex) =>
            IsInSonneZone
            && !stations[stationIndex].Gain.IsNegligible(0.001f)
            && stationIndex == dominantStationIndex;

        private bool IsStationNavigating(int index) =>
            schedulers[index].CurrentPhase == StationScheduler.Phase.Navigation
            && stations[index].NavigationStartTime.IsPositive();

        private static bool IsTimeCompressed(float timeScale) =>
            timeScale > TimeCompressionThreshold;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        public static float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            return GeoMath.InitialBearingDeg(lat1, lon1, lat2, lon2);
        }

        public static float CalculateMapBearing(float lat1, float lon1, float lat2, float lon2)
        {
            return GeoMath.MapBearingDeg(lat1, lon1, lat2, lon2);
        }
    }
}
