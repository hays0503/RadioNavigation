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
        private const float BfoBase = 1000f;

        private static readonly AudioClip silentClip = AudioClip.Create("SonneSilent", AudioSynthesisEngine.SampleRate * 2, 1, AudioSynthesisEngine.SampleRate, false);

        private int dominantStationIndex = -1;
        private StationState[] stations;
        private StationScheduler[] schedulers;
        private MorseKeyer[] keyers;
        private CarrierGenerator[] carriers;
        private AudioSynthesisEngine audioEngine;

        private float currentFrequencyKHz;
        private int currentZoneIndex;
        private float playerLatitude;
        private float playerLongitude;
        private VolumeSpinnerHandler volumeSpinner;
        private FeedbackSpinnerHandler feedbackSpinner;

        private readonly List<SignalEntry> signalEntries = new List<SignalEntry>();
        private int morseCharCount;
        private readonly StringBuilder morseDecoderBuffer = new StringBuilder();
        private readonly StringBuilder decodedText = new StringBuilder();

        private float navUpdateTimer;
        private float tuningUpdateTimer;
        private const float NavUpdateInterval = 0.2f;
        private const float TuningUpdateInterval = 0.016f;

        private readonly List<PositionFixCalculator.Measurement> measurements = new List<PositionFixCalculator.Measurement>();
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
                    Pattern = new NavigationPattern(Array.Empty<Group>(), 8),
                    CallsignLength = callsignLen
                };

                int stationIndex = i;
                keyer.OnElementCompleted += (elemType, isCallsign) => OnStationElementCompleted(stationIndex, elemType, isCallsign);
                keyer.OnSequenceFinished += () => OnStationSequenceFinished(stationIndex);
            }

            var audio = GetComponent<AudioSource>();
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
            RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
            if (volumeSpinner.IsNotNull())
                volumeSpinner.OnValueChanged -= OnVolumeChanged;
            if (feedbackSpinner.IsNotNull())
                feedbackSpinner.OnValueChanged -= OnFeedbackChanged;
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

            if (decodedText.Length.IsPositive())
                decodedText.Append(' ');

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

        public string GetSignalBuffer()
        {
            return GetSignalRichText();
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
            public float NavigationElapsed;
        }

        public void UpdatePosition(float latitude, float longitude)
        {
            playerLatitude = latitude;
            playerLongitude = longitude;
            UpdateStationNavigation();
        }

        public bool HasFix => fixValid;
        public float FixLatitude => lastFix.Latitude;
        public float FixLongitude => lastFix.Longitude;
        public float FixErrorKm => lastFix.ErrorKm;
        public int MeasurementCount => measurements.Count;

        private void Update()
        {
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
                float f = currentFrequencyKHz * 1000f;
                float ratio = f / f0 - f0 / f;
                float gain = 1f / (float)Math.Sqrt(1.0 + qf * qf * ratio * ratio);

                stations[i].Gain = gain;

                float offset = currentFrequencyKHz - freq.Value;
                stations[i].BeatFrequency = Math.Max(300f, Math.Min(1800f, BfoBase + offset * 1000f));
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

                var sector = SonneSignalGenerator.CalculateSector(bearing, beaconStations[i].BaseAngle);

                stations[i].InBeam = true;
                stations[i].DotCount = sector.DotCount;
                stations[i].CurrentBearing = (int)((bearing + 360f) % 360f);
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

                measurements.Add(new PositionFixCalculator.Measurement
                {
                    StationIndex = i,
                    Latitude = (float)beaconStations[i].Latitude,
                    Longitude = (float)beaconStations[i].Longitude,
                    Bearing = bearing,
                    Weight = 1f
                });
            }

            if (HasEnoughMeasurementsForFix())
            {
                lastFix = PositionFixCalculator.Calculate(measurements);
                fixValid = lastFix.Valid;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            audioEngine.ProcessAudio(data, channels, stations, schedulers, keyers, carriers, ToggleHandler.IsRadioOn, currentZoneIndex, SonneZoneIndex);
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
            callsign = station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper();
            return false;
        }

        public static float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            double dLon = ToRadians(lon2 - lon1);
            double lat1R = ToRadians(lat1);
            double lat2R = ToRadians(lat2);

            double y = Math.Sin(dLon) * Math.Cos(lat2R);
            double x = Math.Cos(lat1R) * Math.Sin(lat2R) -
                       Math.Sin(lat1R) * Math.Cos(lat2R) * Math.Cos(dLon);

            float bearing = (float)(Math.Atan2(y, x) * (180.0 / Math.PI));
            return (bearing + 360f) % 360f;
        }

        public static float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle < -180f) angle += 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        private static double ToRadians(float degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}
