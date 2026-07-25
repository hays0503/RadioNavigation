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

        private int _dominantStationIndex = -1;
        private StationState[] _stations;
        private StationScheduler[] _schedulers;
        private MorseKeyer[] _keyers;
        private CarrierGenerator[] _carriers;
        private AudioSynthesisEngine _audioEngine;

        private float _currentFrequencyKHz;
        private int _currentZoneIndex;
        private float _playerLatitude;
        private float _playerLongitude;
        private VolumeSpinnerHandler _volumeSpinner;
        private FeedbackSpinnerHandler _feedbackSpinner;

        private readonly List<SignalEntry> _signalEntries = new List<SignalEntry>();
        private int _morseCharCount;
        private readonly StringBuilder _morseDecoderBuffer = new StringBuilder();
        private readonly StringBuilder _decodedText = new StringBuilder();

        private float _navUpdateTimer;
        private float _tuningUpdateTimer;
        private const float NavUpdateInterval = 0.2f;
        private const float TuningUpdateInterval = 0.016f;

        private readonly List<PositionFixCalculator.Measurement> _measurements = new List<PositionFixCalculator.Measurement>();
        private PositionFixCalculator.FixResult _lastFix;
        private bool _fixValid;

        private void Awake()
        {
            _audioEngine = new AudioSynthesisEngine();

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;

            var beaconStations = BeaconData.Stations;
            _stations = new StationState[beaconStations.Length];
            _schedulers = new StationScheduler[beaconStations.Length];
            _keyers = new MorseKeyer[beaconStations.Length];
            _carriers = new CarrierGenerator[beaconStations.Length];

            for (int i = 0; i < beaconStations.Length; i++)
            {
                string callsign = beaconStations[i].History != null && beaconStations[i].History.Length > 0 && !string.IsNullOrEmpty(beaconStations[i].History[0].Callsign)
                    ? beaconStations[i].History[0].Callsign
                    : beaconStations[i].Name.Substring(0, Math.Min(2, beaconStations[i].Name.Length)).ToUpper();

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

                _schedulers[i] = scheduler;
                _keyers[i] = keyer;
                _carriers[i] = carrier;

                _stations[i] = new StationState
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
            audio.clip = AudioClip.Create("SonneSilent", AudioSynthesisEngine.SampleRate * 2, 1, AudioSynthesisEngine.SampleRate, false);
            audio.loop = true;
            audio.Play();

            _volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            _feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);

            if (_volumeSpinner != null)
            {
                _audioEngine.OutputVolume = Mathf.Clamp(_volumeSpinner.Angle / 140f, 0f, 1f);
                _volumeSpinner.OnValueChanged += OnVolumeChanged;
            }

            if (_feedbackSpinner != null)
                _feedbackSpinner.OnValueChanged += OnFeedbackChanged;
        }

        private void OnDestroy()
        {
            RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
            if (_volumeSpinner != null)
                _volumeSpinner.OnValueChanged -= OnVolumeChanged;
            if (_feedbackSpinner != null)
                _feedbackSpinner.OnValueChanged -= OnFeedbackChanged;
        }

        private void OnStationElementCompleted(int stationIndex, NavigationElementType elemType, bool isCallsign)
        {
            if (_currentZoneIndex != SonneZoneIndex)
                return;
            if (_stations[stationIndex].Gain < 0.001f)
                return;
            if (stationIndex != _dominantStationIndex)
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
            _signalEntries.Add(new SignalEntry(c, type));
            if (type != SignalCharType.Separator)
                _morseCharCount++;
            SignalRichTextFormatter.TrimEntries(_signalEntries, ref _morseCharCount);

            if (isCallsign)
            {
                if (elemType == NavigationElementType.Dot)
                    _morseDecoderBuffer.Append('.');
                else if (elemType == NavigationElementType.Dash)
                    _morseDecoderBuffer.Append('-');
            }
            else if (_morseDecoderBuffer.Length > 0)
            {
                DecodeCallsignBuffer(stationIndex);
            }
        }

        private void OnStationSequenceFinished(int stationIndex)
        {
            if (_currentZoneIndex != SonneZoneIndex)
                return;
            if (_stations[stationIndex].Gain < 0.001f)
                return;
            if (stationIndex != _dominantStationIndex)
                return;

            _signalEntries.Add(new SignalEntry(' ', SignalCharType.Separator));
            SignalRichTextFormatter.TrimEntries(_signalEntries, ref _morseCharCount);
        }

        private void DecodeCallsignBuffer(int stationIndex)
        {
            if (_morseDecoderBuffer.Length == 0)
                return;

            string callsign = null;
            var station = BeaconData.Stations[stationIndex];
            if (station.History != null && station.History.Length > 0 && !string.IsNullOrEmpty(station.History[0].Callsign))
                callsign = station.History[0].Callsign;
            if (string.IsNullOrEmpty(callsign))
                callsign = station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper();

            if (string.IsNullOrEmpty(callsign))
            {
                _morseDecoderBuffer.Clear();
                return;
            }

            int expectedLen = 0;
            foreach (char ch in callsign.ToUpperInvariant())
                expectedLen += MorseAlphabet.GetMorse(ch).Length;

            if (_morseDecoderBuffer.Length != expectedLen)
            {
                _morseDecoderBuffer.Clear();
                return;
            }

            if (_decodedText.Length > 0)
                _decodedText.Append(' ');

            int pos = 0;
            foreach (char ch in callsign.ToUpperInvariant())
            {
                string mc = MorseAlphabet.GetMorse(ch);
                if (mc.Length == 0 || pos + mc.Length > _morseDecoderBuffer.Length)
                    break;

                string segment = _morseDecoderBuffer.ToString(pos, mc.Length);
                _decodedText.Append(segment == mc ? ch : '?');
                pos += mc.Length;
            }

            SignalRichTextFormatter.TrimDecodedText(_decodedText);
            _morseDecoderBuffer.Clear();
        }

        public string GetSignalRichText()
        {
            return SignalRichTextFormatter.FormatRichText(_signalEntries);
        }

        public string GetSignalBuffer()
        {
            return GetSignalRichText();
        }

        public void ClearSignalBuffer()
        {
            _signalEntries.Clear();
            _morseCharCount = 0;
            _morseDecoderBuffer.Clear();
            _decodedText.Clear();
        }

        public string GetDecodedText()
        {
            return _decodedText.ToString();
        }

        private void OnFrequencyChanged(float frequencyKHz, int zoneIndex)
        {
            UpdateTuning(frequencyKHz, zoneIndex);
        }

        private void OnVolumeChanged(float angle)
        {
            _audioEngine.OutputVolume = Mathf.Clamp(angle / 140f, 0f, 1f);
        }

        private void OnFeedbackChanged(float angle)
        {
            _audioEngine.ApplyFeedback(angle);
            UpdateStationGains();
        }

        public void UpdateTuning(float frequencyKHz, int zoneIndex)
        {
            _currentFrequencyKHz = frequencyKHz;
            _currentZoneIndex = zoneIndex;
            UpdateStationGains();
        }

        public int StationCount => _stations != null ? _stations.Length : 0;
        public int DominantStationIndex => _dominantStationIndex;

        public StationDiagnostics GetStationDiagnostics(int index)
        {
            if (_stations == null || index < 0 || index >= _stations.Length)
                return new StationDiagnostics();

            var s = _stations[index];
            var station = BeaconData.Stations[index];
            string callsign = station.History != null && station.History.Length > 0 && !string.IsNullOrEmpty(station.History[0].Callsign)
                ? station.History[0].Callsign
                : station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper();

            float elapsed = 0f;
            if (_schedulers[index].CurrentPhase == StationScheduler.Phase.Navigation && s.NavigationStartTime > 0f)
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
                SchedulerPhase = _schedulers[index].CurrentPhase,
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
            _playerLatitude = latitude;
            _playerLongitude = longitude;
            UpdateStationNavigation();
        }

        public bool HasFix => _fixValid;
        public float FixLatitude => _lastFix.Latitude;
        public float FixLongitude => _lastFix.Longitude;
        public float FixErrorKm => _lastFix.ErrorKm;
        public int MeasurementCount => _measurements.Count;

        private void Update()
        {
            _tuningUpdateTimer += Time.deltaTime;
            if (_tuningUpdateTimer >= TuningUpdateInterval)
            {
                _tuningUpdateTimer = 0f;
                UpdateStationGains();
            }

            _navUpdateTimer += Time.deltaTime;
            if (_navUpdateTimer >= NavUpdateInterval)
            {
                _navUpdateTimer = 0f;
                UpdateStationNavigation();
            }
        }

        private void UpdateStationGains()
        {
            if (_currentZoneIndex != SonneZoneIndex)
            {
                for (int i = 0; i < _stations.Length; i++)
                    _stations[i].Gain = 0f;
                _dominantStationIndex = -1;
                return;
            }

            var stations = BeaconData.Stations;
            float maxGain = 0f;
            int maxIndex = -1;
            float qf = _audioEngine.QualityFactor;

            for (int i = 0; i < stations.Length; i++)
            {
                float? freq = stations[i].History?[0].FrequencyKHz;
                if (freq == null)
                {
                    _stations[i].Gain = 0f;
                    continue;
                }

                float f0 = freq.Value * 1000f;
                float f = _currentFrequencyKHz * 1000f;
                float ratio = f / f0 - f0 / f;
                float gain = 1f / (float)Math.Sqrt(1.0 + qf * qf * ratio * ratio);

                _stations[i].Gain = gain;

                float offset = _currentFrequencyKHz - freq.Value;
                _stations[i].BeatFrequency = Math.Max(300f, Math.Min(1800f, BfoBase + offset * 1000f));
                _carriers[i].Frequency = _stations[i].BeatFrequency;

                if (gain > maxGain)
                {
                    maxGain = gain;
                    maxIndex = i;
                }
            }

            if (maxGain > 0.01f)
            {
                if (maxIndex != _dominantStationIndex)
                {
                    _signalEntries.Clear();
                    _morseCharCount = 0;
                    _morseDecoderBuffer.Clear();
                    _decodedText.Clear();
                }
                _dominantStationIndex = maxIndex;
            }
            else
            {
                _dominantStationIndex = -1;
            }
        }

        private void UpdateStationNavigation()
        {
            var stations = BeaconData.Stations;
            _measurements.Clear();

            for (int i = 0; i < stations.Length; i++)
            {
                float bearing = CalculateBearing(
                    _playerLatitude, _playerLongitude,
                    (float)stations[i].Latitude, (float)stations[i].Longitude);

                var sector = SonneSignalGenerator.CalculateSector(bearing, stations[i].BaseAngle);

                _stations[i].InBeam = true;
                _stations[i].DotCount = sector.DotCount;
                _stations[i].CurrentBearing = (int)((bearing + 360f) % 360f);
                _stations[i].TransitionIndex = sector.DotCount;

                string callsign = stations[i].History != null && stations[i].History.Length > 0 && !string.IsNullOrEmpty(stations[i].History[0].Callsign)
                    ? stations[i].History[0].Callsign
                    : stations[i].Name.Substring(0, Math.Min(2, stations[i].Name.Length)).ToUpper();

                var config = _stations[i].Config;
                var consolPattern = SonneSignalGenerator.GenerateConsolPattern(
                    callsign, sector.DotCount, config);

                if (!_keyers[i].IsActive ||
                    _schedulers[i].CurrentPhase != StationScheduler.Phase.Navigation)
                {
                    _schedulers[i].CurrentConsolPattern = consolPattern;
                }

                _measurements.Add(new PositionFixCalculator.Measurement
                {
                    StationIndex = i,
                    Latitude = (float)stations[i].Latitude,
                    Longitude = (float)stations[i].Longitude,
                    Bearing = bearing,
                    Weight = 1f
                });
            }

            if (_measurements.Count >= 2)
            {
                _lastFix = PositionFixCalculator.Calculate(_measurements);
                _fixValid = _lastFix.Valid;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            _audioEngine.ProcessAudio(data, channels, _stations, _schedulers, _keyers, _carriers, ToggleHandler.IsRadioOn, _currentZoneIndex, SonneZoneIndex);
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
