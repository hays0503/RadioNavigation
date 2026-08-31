using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    [RequireComponent(typeof(AudioSource))]
    public class SonneController : MonoBehaviour
    {
        private const int SonneZoneIndex = 1;
        private const float BfoBase = Constants.BfoBaseHz;
        private const float TimeCompressionThreshold = 1.01f;
        private const float NavigationBearingChangeThresholdDeg = 0.2f;

        private static readonly AudioClip silentClip = AudioClip.Create("SonneSilent", AudioSynthesisEngine.SampleRate * 2, 1, AudioSynthesisEngine.SampleRate, false);

        private int dominantStationIndex = -1;
        private StationState[] stations;
        private StationScheduler[] schedulers;
        private MorseKeyer[] keyers;
        private CarrierGenerator[] carriers;
        private Action<NavigationElementType, bool>[] keyerElementHandlers;
        private Action[] keyerSequenceHandlers;
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

        // F120: pre-sized lists avoid capacity-doubling allocations on the
        // audio thread (signalEntries) and in the 5 Hz navigation update.
        private readonly List<SignalEntry> signalEntries = new List<SignalEntry>(SignalRichTextFormatter.MaxSignalEntries);
        private int morseCharCount;
        private readonly StringBuilder morseDecoderBuffer = new StringBuilder();
        private readonly StringBuilder decodedText = new StringBuilder();

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

            var beaconStations = BeaconData.Stations;
            stations = new StationState[beaconStations.Length];
            schedulers = new StationScheduler[beaconStations.Length];
            keyers = new MorseKeyer[beaconStations.Length];
            carriers = new CarrierGenerator[beaconStations.Length];
            keyerElementHandlers = new Action<NavigationElementType, bool>[beaconStations.Length];
            keyerSequenceHandlers = new Action[beaconStations.Length];

            for (int i = 0; i < beaconStations.Length; i++)
            {
                StationCallsignResolver.TryGet(beaconStations[i], out string callsign);

                var config = ConsolCycleConfig.GetForCycleDuration(beaconStations[i].CycleDurationSeconds);
                var initialPattern = SonneBeam.BuildPattern(0, false);

                var scheduler = new StationScheduler(callsign)
                {
                    Config = config,
                    CurrentConsolPattern = initialPattern
                };
                var keyer = new MorseKeyer();
                var carrier = new CarrierGenerator(AudioSynthesisEngine.SampleRate);

                schedulers[i] = scheduler;
                keyers[i] = keyer;
                carriers[i] = carrier;

                stations[i] = new StationState
                {
                    InBeam = false,
                    TransitionIndex = config.MaxNavigationCount,
                    Gain = 0f,
                    BeatFrequency = BfoBase,
                    Config = config,
                    DotCount = 0,
                    CurrentBearing = 0,
                    BearingToBoat = 0,
                    Pattern = SonneSignalGenerator.ToNavigationPattern(initialPattern)
                };

                int stationIndex = i;
                var elementHandler = new Action<NavigationElementType, bool>((elemType, isCallsign) => OnStationElementCompleted(stationIndex, elemType, isCallsign));
                var sequenceHandler = new Action(() => OnStationSequenceFinished(stationIndex));
                keyerElementHandlers[i] = elementHandler;
                keyerSequenceHandlers[i] = sequenceHandler;
                keyer.OnElementCompleted += elementHandler;
                keyer.OnSequenceFinished += sequenceHandler;
            }

            cachedAudioSource = GetComponent<AudioSource>();
            var audio = cachedAudioSource;
            audio.clip = silentClip;
            audio.loop = true;
            audio.Play();

            volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);

            if (volumeSpinner.IsNotNull())
            {
                audioEngine.OutputVolume = Mathf.Clamp(volumeSpinner.Angle / 140f, 0f, 1f);
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
            if (keyers.IsNotNull() && keyerElementHandlers.IsNotNull() && keyerSequenceHandlers.IsNotNull())
            {
                for (int i = 0; i < keyers.Length; i++)
                {
                    if (keyers[i].IsNotNull())
                    {
                        keyers[i].OnElementCompleted -= keyerElementHandlers[i];
                        keyers[i].OnSequenceFinished -= keyerSequenceHandlers[i];
                    }
                }
            }
        }

        private void OnStationElementCompleted(int stationIndex, NavigationElementType elemType, bool isCallsign)
        {
            if (!IsProcessingDominantStation(stationIndex))
                return;

            SignalCharType type;
            char c;
            switch (elemType)
            {
                case NavigationElementType.Equisignal:
                    type = SignalCharType.Equisignal;
                    c = '=';
                    break;
                case NavigationElementType.Dash:
                    type = isCallsign ? SignalCharType.Callsign : SignalCharType.Dash;
                    c = '-';
                    break;
                default:
                    type = isCallsign ? SignalCharType.Callsign : SignalCharType.Dot;
                    c = '.';
                    break;
            }
            signalEntries.Add(new SignalEntry(c, type));
            if (type != SignalCharType.Separator)
                morseCharCount++;
            SignalRichTextFormatter.TrimEntries(signalEntries, ref morseCharCount);

            if (isCallsign)
            {
                if (elemType == NavigationElementType.Dot)
                    morseDecoderBuffer.Append('.');
                else if (elemType == NavigationElementType.Dash)
                    morseDecoderBuffer.Append('-');
            }
            else if (morseDecoderBuffer.Length.IsPositive())
            {
                DecodeCallsignBuffer(stationIndex);
            }
        }

        private void OnStationSequenceFinished(int stationIndex)
        {
            if (!IsProcessingDominantStation(stationIndex))
                return;

            if (morseDecoderBuffer.Length.IsPositive())
                DecodeCallsignBuffer(stationIndex);

            signalEntries.Add(new SignalEntry(' ', SignalCharType.Separator));
            SignalRichTextFormatter.TrimEntries(signalEntries, ref morseCharCount);
        }

        private void DecodeCallsignBuffer(int stationIndex)
        {
            if (morseDecoderBuffer.Length.IsZero())
                return;

            string callsign = null;
            var station = BeaconData.Stations[stationIndex];
            StationCallsignResolver.TryGet(station, out callsign);

            if (string.IsNullOrEmpty(callsign))
            {
                morseDecoderBuffer.Clear();
                return;
            }

            if (morseDecoderBuffer.Length != MorseDecoder.ExpectedLength(callsign))
            {
                morseDecoderBuffer.Clear();
                return;
            }

            // Keep only the latest completed callsign. Appending every cycle
            // made the panel grow into "FRQ MWN SL ..." until trimming.
            decodedText.Clear();
            decodedText.Append(MorseDecoder.Decode(callsign, morseDecoderBuffer));

            SignalRichTextFormatter.TrimDecodedText(decodedText);
            morseDecoderBuffer.Clear();
        }

        public string GetSignalRichText()
        {
            return SignalRichTextFormatter.FormatRichText(signalEntries);
        }

        public void ClearSignalBuffer()
        {
            signalEntries.Clear();
            morseCharCount = 0;
            morseDecoderBuffer.Clear();
            decodedText.Clear();
        }

        public string GetDecodedText()
        {
            return decodedText.ToString();
        }

        /// <summary>
        /// Counts dots in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDotCount()
        {
            int count = 0;
            for (int i = 0; i < signalEntries.Count; i++)
            {
                if (signalEntries[i].Type == SignalCharType.Dot)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Counts dashes in the current emission window (at most 20 characters).
        /// This is not the CAP observer count from the 60-character cycle.
        /// Recv can be much smaller than CAP count and is zero during callsign phase by design.
        /// </summary>
        public int GetReceivedNavigationDashCount()
        {
            int count = 0;
            for (int i = 0; i < signalEntries.Count; i++)
            {
                if (signalEntries[i].Type == SignalCharType.Dash)
                    count++;
            }
            return count;
        }

        private void OnFrequencyChanged(float frequencyKHz, int zoneIndex)
        {
            ClearSignalBuffer();
            UpdateTuning(frequencyKHz, zoneIndex);
        }

        private void OnVolumeChanged(float angle)
        {
            audioEngine.OutputVolume = Mathf.Clamp(angle / 140f, 0f, 1f);
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

            return new StationDiagnostics
            {
                Name = station.Name,
                Callsign = callsign,
                InBeam = s.InBeam,
                IsDashSector = s.IsDashSector,
                SectorNumber = s.SectorNumber,
                Gain = s.Gain,
                BeatFrequency = s.BeatFrequency,
                TransitionIndex = s.TransitionIndex,
                Pattern = s.Pattern,
                SchedulerPhase = schedulers[index].CurrentPhase,
                DotCount = s.DotCount,
                // EqSignIndex is the actual position of the equisignal in the
                // 60-element emitted pattern, after the lost-sign correction.
                EqSignIndex = s.TransitionIndex,
                DashCount = s.Config.MaxNavigationCount - s.DotCount,
                CurrentBearing = s.CurrentBearing,
                BearingToBoat = s.BearingToBoat,
                NavigationElapsed = elapsed
            };
        }

        public struct StationDiagnostics
        {
            public string Name;
            public string Callsign;
            public bool InBeam;
            public bool IsDashSector;
            public int SectorNumber;
            public float Gain;
            public float BeatFrequency;
            public int TransitionIndex;
            public NavigationPattern Pattern;
            public StationScheduler.Phase SchedulerPhase;
            public int DotCount;
            public int EqSignIndex;
            public int DashCount;
            public float CurrentBearing;
            public float BearingToBoat;
            public float NavigationElapsed;
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
                    DateTime utcNow = GameTimeResolver.GetUtcNow();
                    Debug.Log("[RadioNav][POSITION] provider returned " + posLat.ToString("F5") + "," + posLon.ToString("F5")
                        + " | " + posLat.ToString("F4") + "," + posLon.ToString("F4")
                        + " | " + utcNow.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
                }

                if (!positionAvailable
                    || Math.Abs(posLat - playerLatitude) > 0.0001f
                    || Math.Abs(posLon - playerLongitude) > 0.0001f)
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
            audioEngine.UpdateStations(Time.deltaTime, Time.time, stations, schedulers, keyers, carriers, ToggleHandler.IsRadioOn);
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

                float f0 = freq.Value * 1000f;
                float ratio = f / f0 - f0 / f;
                float gain = 1f / (float)Math.Sqrt(1.0 + qf * qf * ratio * ratio);

                stations[i].Gain = gain;

                float offset = currentFrequencyKHz - freq.Value;
                stations[i].BeatFrequency = Math.Max(Constants.BfoMinHz, Math.Min(Constants.BfoMaxHz, BfoBase + offset * 1000f));
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
                    signalEntries.Clear();
                    morseCharCount = 0;
                    morseDecoderBuffer.Clear();
                    decodedText.Clear();
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
                float mapBoatToStation = CalculateMapBearing(
                    playerLatitude, playerLongitude,
                    (float)beaconStations[i].Latitude, (float)beaconStations[i].Longitude);
                float mapBearingToBoat = CalculateMapBearing(
                    (float)beaconStations[i].Latitude, (float)beaconStations[i].Longitude,
                    playerLatitude, playerLongitude);

                if (beaconStations[i].Role != BeaconStationRole.DirectionalSonne)
                {
                    stations[i].InBeam = false;
                    stations[i].DotCount = 0;
                    stations[i].IsDashSector = false;
                    stations[i].SectorNumber = 0;
                    stations[i].CurrentBearing = mapBoatToStation;
                    stations[i].BearingToBoat = mapBearingToBoat;
                    stations[i].HasNavigationBearing = false;
                    stations[i].LastNavigationBearing = 0f;
                    stations[i].TransitionIndex = 0;
                    stations[i].Pattern = new NavigationPattern(Array.Empty<Group>(), 8, 255);
                    continue;
                }

                stations[i].CurrentBearing = mapBoatToStation;
                stations[i].BearingToBoat = mapBearingToBoat;
                float signalBearing = (mapBearingToBoat + 360f) % 360f;
                stations[i].InBeam = SonneSignalGenerator.CalculateInBeam(
                    signalBearing,
                    (float)beaconStations[i].BaseAngle,
                    stations[i].Config.SectorWidthDeg);
                stations[i].SectorNumber = SonneSignalGenerator.CalculateSectorNumber(
                    signalBearing,
                    (float)beaconStations[i].BaseAngle);

                bool bearingChanged = !stations[i].HasNavigationBearing
                    || SonneSignalGenerator.AngularDistance(
                        stations[i].LastNavigationBearing,
                        signalBearing) > NavigationBearingChangeThresholdDeg;
                if (bearingChanged)
                {
                    stations[i].HasNavigationBearing = true;
                    stations[i].LastNavigationBearing = signalBearing;

                    NavigationElementType[] pattern;
                    SonneBeamSignal signal;
                    if (SonneBeam.TryGetNavigationPattern(
                        beaconStations[i].Name,
                        signalBearing,
                        out pattern,
                        out signal))
                    {
                        stations[i].DotCount = signal.DotCount;
                        // DotCount/DashCount are corrected CAP counts. TransitionIndex
                        // is the actual element index where the equisignal starts.
                        stations[i].IsDashSector = signal.IsDashSector;
                        stations[i].TransitionIndex = signal.LeadingCount;
                        stations[i].Pattern = SonneSignalGenerator.ToNavigationPattern(pattern);

                        // The active keyer owns its current array. Replacing the
                        // scheduler reference is safe and makes the latest table
                        // result available for the next navigation phase.
                        schedulers[i].CurrentConsolPattern = pattern;
                    }
                }

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

            Debug.Log("[RadioNav][BEARING] player lat=" + playerLatitude.ToString("F6")
                + " lon=" + playerLongitude.ToString("F6"));

            for (int i = 0; i < beaconStations.Length; i++)
            {
                BeaconStation station = beaconStations[i];
                float mapBoatToStation = stations[i].CurrentBearing;
                float mapStationToBoat = stations[i].BearingToBoat;
                float trueBoatToStation = CalculateBearing(
                    playerLatitude, playerLongitude,
                    (float)station.Latitude, (float)station.Longitude);
                float trueStationToBoat = CalculateBearing(
                    (float)station.Latitude, (float)station.Longitude,
                    playerLatitude, playerLongitude);
                float trueReciprocal = (trueStationToBoat + 180f) % 360f;
                float trueReciprocalDelta = Math.Abs(trueReciprocal - trueBoatToStation);
                if (trueReciprocalDelta > 180f)
                    trueReciprocalDelta = 360f - trueReciprocalDelta;
                float mapReciprocal = (mapStationToBoat + 180f) % 360f;
                float mapReciprocalDelta = Math.Abs(mapReciprocal - mapBoatToStation);
                if (mapReciprocalDelta > 180f)
                    mapReciprocalDelta = 360f - mapReciprocalDelta;

                Debug.Log("[RadioNav][BEARING] index=" + i
                    + " name=" + station.Name
                    + " callsign=" + (station.History.IsNotNull() && station.History.Length.IsPositive()
                        ? station.History[0].Callsign : "?")
                    + " station=" + station.Latitude.ToString("F6") + "," + station.Longitude.ToString("F6")
                    + " mapBoatToStation=" + mapBoatToStation.ToString("F1")
                    + " trueBoatToStation=" + trueBoatToStation.ToString("F1")
                    + " trueStationToBoat=" + trueStationToBoat.ToString("F1")
                    + " mapStationToBoat=" + mapStationToBoat.ToString("F1")
                    + " distanceKm=" + GeoMath.HaversineKm(
                        (float)station.Latitude,
                        (float)station.Longitude,
                        playerLatitude,
                        playerLongitude).ToString("F1")
                    + " trueReciprocalDelta=" + trueReciprocalDelta.ToString("F1")
                    + " mapReciprocalDelta=" + mapReciprocalDelta.ToString("F1")
                    + " baseAngle=" + station.BaseAngle.ToString("F1")
                    + " beamHeading=" + station.BeamHeadingDegTrue.ToString("F1"));
            }

            lastBearingLogLatitude = playerLatitude;
            lastBearingLogLongitude = playerLongitude;
            bearingDiagnosticsLogged = true;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            bool audioEnabled = ToggleHandler.IsRadioOn && !suppressAudioForTimeCompression;
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
