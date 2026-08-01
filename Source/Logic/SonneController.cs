using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    [RequireComponent(typeof(AudioSource))]
    public class SonneController : MonoBehaviour
    {
        private const int SonneZoneIndex = 1;
        private const float BfoBase = Constants.BfoBaseHz;

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
        private bool positionAvailable; // F200: a fix without a position feed must not be shown
        private bool positionProviderFailureLogged;
        private bool positionProviderSuccessLogged;
        private int lastLoggedMeasurementCount = -1;
        private bool lastLoggedFixValid;
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

        private readonly List<PositionFixCalculator.Measurement> measurements = new List<PositionFixCalculator.Measurement>(BeaconData.Stations.Length);
        private PositionFixCalculator.FixResult lastFix;
        private bool fixValid;

        private void Awake()
        {
            audioEngine = new AudioSynthesisEngine();

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;

            var beaconStations = BeaconData.Stations;
            stations = new StationState[beaconStations.Length];
            schedulers = new StationScheduler[beaconStations.Length];
            keyers = new MorseKeyer[beaconStations.Length];
            carriers = new CarrierGenerator[beaconStations.Length];
            keyerElementHandlers = new Action<NavigationElementType, bool>[beaconStations.Length];
            keyerSequenceHandlers = new Action[beaconStations.Length];

            for (int i = 0; i < beaconStations.Length; i++)
            {
                TryGetCallsignFromHistory(beaconStations[i], out string callsign);

                var config = ConsolCycleConfig.GetForCycleDuration(beaconStations[i].CycleDurationSeconds);
                var bearingTable = SonneSignalGenerator.GenerateBearingTable(beaconStations[i]);
                int callsignLen = SonneSignalGenerator.GetCallsignElementCount(callsign);

                var scheduler = new StationScheduler(callsign)
                {
                    Config = config,
                    BearingTable = bearingTable
                };
                scheduler.CurrentConsolPattern = SonneSignalGenerator.GenerateConsolPattern(callsign, 0, config); // F145: pre-compute so the first callsign cycle has a pattern with a valid callsign length
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
                    Pattern = new NavigationPattern(Array.Empty<Group>(), 8),
                    CallsignLength = callsignLen
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

            signalEntries.Add(new SignalEntry(' ', SignalCharType.Separator));
            SignalRichTextFormatter.TrimEntries(signalEntries, ref morseCharCount);
        }

        private void DecodeCallsignBuffer(int stationIndex)
        {
            if (morseDecoderBuffer.Length.IsZero())
                return;

            string callsign = null;
            var station = BeaconData.Stations[stationIndex];
            TryGetCallsignFromHistory(station, out callsign);

            if (string.IsNullOrEmpty(callsign))
            {
                morseDecoderBuffer.Clear();
                return;
            }

            int expectedLen = 0;
            foreach (char ch in callsign.ToUpperInvariant())
                expectedLen += MorseAlphabet.GetMorse(ch).Length;

            if (morseDecoderBuffer.Length != expectedLen)
            {
                morseDecoderBuffer.Clear();
                return;
            }

            // Keep only the latest completed callsign. Appending every cycle
            // made the panel grow into "FRQ MWN SL ..." until trimming.
            decodedText.Clear();
            int pos = 0;
            foreach (char ch in callsign.ToUpperInvariant())
            {
                string mc = MorseAlphabet.GetMorse(ch);
                if (mc.Length.IsZero() || pos + mc.Length > morseDecoderBuffer.Length)
                    break;

                string segment = morseDecoderBuffer.ToString(pos, mc.Length);
                decodedText.Append(segment == mc ? ch : '?');
                pos += mc.Length;
            }

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
            TryGetCallsignFromHistory(station, out string callsign);

            float elapsed = 0f;
            if (IsStationNavigating(index))
                elapsed = Time.time - s.NavigationStartTime;

            return new StationDiagnostics
            {
                Name = station.Name,
                Callsign = callsign,
                InBeam = s.InBeam,
                Gain = s.Gain,
                BeatFrequency = s.BeatFrequency,
                TransitionIndex = s.TransitionIndex,
                Pattern = s.Pattern,
                SchedulerPhase = schedulers[index].CurrentPhase,
                DotCount = s.DotCount,
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
            public float Gain;
            public float BeatFrequency;
            public int TransitionIndex;
            public NavigationPattern Pattern;
            public StationScheduler.Phase SchedulerPhase;
            public int DotCount;
            public int CurrentBearing;
            public int BearingToBoat;
            public float NavigationElapsed;
        }

        public void UpdatePosition(float latitude, float longitude)
        {
            playerLatitude = latitude;
            playerLongitude = longitude;
            positionAvailable = true; // F200: the (0,0) fallback is never a valid fix
            UpdateStationNavigation();
        }

        public bool HasFix => fixValid && positionAvailable;
        public float FixLatitude => lastFix.Latitude;
        public float FixLongitude => lastFix.Longitude;
        public float FixErrorKm => lastFix.ErrorKm;
        public int MeasurementCount => measurements.Count;

        private void Update()
        {
            suppressAudioForTimeCompression = Time.timeScale > 1.01f;

            // F200: poll the position feed; UpdatePosition is invoked on
            // significant movement (threshold ~11 m). Without a feed the fix
            // stays unavailable and the UI shows "--- Position Fix: ---".
            float posLat, posLon;
            if (positionProvider.IsNotNull() && positionProvider.TryGetPosition(out posLat, out posLon))
            {
                if (!positionProviderSuccessLogged)
                {
                    Debug.Log("[RadioNav][POSITION] provider returned " + posLat.ToString("F5") + "," + posLon.ToString("F5"));
                    positionProviderSuccessLogged = true;
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
                Debug.LogWarning("[RadioNav][POSITION] player position unavailable; position fix will remain hidden");
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
            audioEngine.UpdateStations(Time.deltaTime, Time.time, stations, schedulers, keyers, ToggleHandler.IsRadioOn);
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
            measurements.Clear();

            for (int i = 0; i < beaconStations.Length; i++)
            {
                float bearing = CalculateBearing(
                    playerLatitude, playerLongitude,
                    (float)beaconStations[i].Latitude, (float)beaconStations[i].Longitude);
                float bearingToBoat = CalculateBearing(
                    (float)beaconStations[i].Latitude, (float)beaconStations[i].Longitude,
                    playerLatitude, playerLongitude);

                var sector = SonneSignalGenerator.CalculateSector(bearing, beaconStations[i].BaseAngle);

                stations[i].InBeam = SonneSignalGenerator.CalculateInBeam(bearing, (float)beaconStations[i].BaseAngle, stations[i].Config.SectorWidthDeg);
                stations[i].DotCount = sector.DotCount;
                stations[i].CurrentBearing = (int)((bearing + 360f) % 360f);
                stations[i].BearingToBoat = (int)((bearingToBoat + 360f) % 360f);
                stations[i].TransitionIndex = sector.DotCount;

                string callsign;
                TryGetCallsignFromHistory(beaconStations[i], out callsign);

                var config = stations[i].Config;
                var consolPattern = SonneSignalGenerator.GenerateConsolPattern(
                    callsign, sector.DotCount, config);

                if (IsKeyerAvailableForPatternUpdate(i))
                {
                    schedulers[i].CurrentConsolPattern = consolPattern;
                }

                stations[i].Pattern = SonneSignalGenerator.ToNavigationPattern(consolPattern); // F77: diagnostics Pattern was never updated after init

                // F131: a station without a receivable frequency (History
                // empty or no value) must not contribute phantom bearings.
                if (HasStationFrequency(beaconStations[i]))
                {
                    measurements.Add(new PositionFixCalculator.Measurement
                    {
                        StationIndex = i,
                        Latitude = (float)beaconStations[i].Latitude,
                        Longitude = (float)beaconStations[i].Longitude,
                        // PositionFixCalculator intersects rays from each
                        // station toward the boat, not boat-to-station bearings.
                        Bearing = bearingToBoat,
                        Weight = 1f
                    });
                }
            }

            if (HasEnoughMeasurementsForFix())
            {
                lastFix = PositionFixCalculator.Calculate(measurements);
                fixValid = lastFix.Valid;
                if (measurements.Count != lastLoggedMeasurementCount || fixValid != lastLoggedFixValid)
                {
                    LogPositionMeasurements();
                    Debug.Log("[RadioNav][POSITION] measurements=" + measurements.Count
                        + " valid=" + fixValid
                        + " lat=" + lastFix.Latitude.ToString("F5")
                        + " lon=" + lastFix.Longitude.ToString("F5")
                        + " errorKm=" + lastFix.ErrorKm.ToString("F2"));
                    lastLoggedMeasurementCount = measurements.Count;
                    lastLoggedFixValid = fixValid;
                }
            }
            else
            {
                fixValid = false;
                if (lastLoggedMeasurementCount != measurements.Count || lastLoggedFixValid)
                {
                    LogPositionMeasurements();
                    Debug.Log("[RadioNav][POSITION] measurements=" + measurements.Count + "; at least 2 required");
                    lastLoggedMeasurementCount = measurements.Count;
                    lastLoggedFixValid = false;
                }
            }
        }

        private void LogPositionMeasurements()
        {
            var details = new StringBuilder();
            details.Append("[RadioNav][POSITION] player=")
                .Append(playerLatitude.ToString("F6"))
                .Append(',').Append(playerLongitude.ToString("F6"))
                .Append(" freqKHz=").Append(currentFrequencyKHz.ToString("F3"))
                .Append(" zone=").Append(currentZoneIndex);

            for (int i = 0; i < measurements.Count; i++)
            {
                var measurement = measurements[i];
                string name = measurement.StationIndex >= 0 && measurement.StationIndex < BeaconData.Stations.Length
                    ? BeaconData.Stations[measurement.StationIndex].Name
                    : "?";
                details.Append(" | ").Append(measurement.StationIndex)
                    .Append(':').Append(name)
                    .Append(" lat=").Append(measurement.Latitude.ToString("F5"))
                    .Append(" lon=").Append(measurement.Longitude.ToString("F5"))
                    .Append(" bearing=").Append(measurement.Bearing.ToString("F1"))
                    .Append(" weight=").Append(measurement.Weight.ToString("F2"));
            }

            Debug.Log(details.ToString());
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

        private bool HasEnoughMeasurementsForFix() => measurements.Count >= 2;

        private bool IsKeyerAvailableForPatternUpdate(int i) =>
            !keyers[i].IsActive
            || schedulers[i].CurrentPhase != StationScheduler.Phase.Navigation;

        private static bool TryGetCallsignFromHistory(BeaconStation station, out string callsign)
        {
            if (station.History.IsNotNull()
                && station.History.Length.IsPositive()
                && !string.IsNullOrEmpty(station.History[0].Callsign))
            {
                callsign = station.History[0].Callsign;
                return true;
            }

            // F87: no history callsign -> report absence instead of fabricating
            // a fake one (e.g. "PL" from "Plougourvest"); GenerateConsolPattern
            // skips the callsign phase and DecodeCallsignBuffer clears the
            // buffer. F66: station.Name may be null and is not used here.
            callsign = null;
            return false;
        }

        private static bool HasStationFrequency(BeaconStation station)
        {
            return station.History.IsNotNull()
                && station.History.Length.IsPositive()
                && station.History[0].FrequencyKHz.IsNotNull();
        }

        public static float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            return GeoMath.InitialBearingDeg(lat1, lon1, lat2, lon2);
        }
    }
}
