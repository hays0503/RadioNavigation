using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    public enum SignalCharType : byte
    {
        Callsign,
        Dot,
        Dash,
        Equisignal,
        Separator
    }

    public struct SignalEntry
    {
        public char Ch;
        public SignalCharType Type;

        public SignalEntry(char ch, SignalCharType type)
        {
            Ch = ch;
            Type = type;
        }
    }

    [RequireComponent(typeof(AudioSource))]
    public class SonneController : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const int SonneZoneIndex = 1;
        private const float BfoBase = 1000f;
        private const float BfoMin = 300f;
        private const float BfoMax = 1800f;

        private float _qualityFactor = 20f;
        private float _feedback = 0f;
        private int _dominantStationIndex = -1;

        private StationState[] _stations;
        private Atmosphere _atmosphere;
        private ReceiverDsp _dsp;
        private float _ticksPerSample;
        private float _tickAccumulator;

        private float _currentFrequencyKHz;
        private int _currentZoneIndex;
        private float _playerLatitude;
        private float _playerLongitude;
        private float _outputVolume = 1f;
        private VolumeSpinnerHandler _volumeSpinner;
        private FeedbackSpinnerHandler _feedbackSpinner;

        private readonly List<SignalEntry> _signalEntries = new List<SignalEntry>();
        private const int MaxSignalEntries = 60;

        private float _navUpdateTimer;
        private float _tuningUpdateTimer;
        private const float NavUpdateInterval = 0.2f;
        private const float TuningUpdateInterval = 0.016f;

        private readonly List<PositionFixCalculator.Measurement> _measurements = new List<PositionFixCalculator.Measurement>();
        private PositionFixCalculator.FixResult _lastFix;
        private bool _fixValid;

        private void Awake()
        {
            _atmosphere = new Atmosphere(Environment.TickCount);
            _dsp = new ReceiverDsp(SampleRate);

            RadioHandler.OnFrequencyChanged += OnFrequencyChanged;

            float samplesPerMs = SampleRate / 1000f;
            float msPerTick = 50f;
            _ticksPerSample = 1f / (samplesPerMs * msPerTick);

            var stations = BeaconData.Stations;
            _stations = new StationState[stations.Length];

            for (int i = 0; i < stations.Length; i++)
            {
                string callsign = stations[i].History != null && stations[i].History.Length > 0 && !string.IsNullOrEmpty(stations[i].History[0].Callsign)
                    ? stations[i].History[0].Callsign
                    : stations[i].Name.Substring(0, Math.Min(2, stations[i].Name.Length)).ToUpper();

                var config = ConsolCycleConfig.GetForCycleDuration(stations[i].CycleDurationSeconds);
                var bearingTable = SonneSignalGenerator.GenerateBearingTable(stations[i]);
                int callsignLen = SonneSignalGenerator.GetCallsignElementCount(callsign);

                _stations[i] = new StationState
                {
                    InBeam = false,
                    TransitionIndex = config.MaxNavigationCount,
                    Gain = 0f,
                    BeatFrequency = BfoBase,
                    Config = config,
                    DotCount = 0,
                    CurrentBearing = 0,
                    Pattern = new NavigationPattern(System.Array.Empty<Group>(), 8),
                    Scheduler = new StationScheduler(callsign)
                    {
                        Config = config,
                        BearingTable = bearingTable
                    },
                    Keyer = new MorseKeyer(),
                    Carrier = new CarrierGenerator(SampleRate),
                    CallsignLength = callsignLen
                };

                int stationIndex = i;
                _stations[i].Keyer.OnElementCompleted += (elemType, isCallsign) => OnStationElementCompleted(stationIndex, elemType, isCallsign);
                _stations[i].Keyer.OnSequenceFinished += () => OnStationSequenceFinished(stationIndex);
            }

            var audio = GetComponent<AudioSource>();
            audio.clip = AudioClip.Create("SonneSilent", SampleRate * 2, 1, SampleRate, false);
            audio.loop = true;
            audio.Play();

            _volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
            _feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);

            if (_volumeSpinner != null)
            {
                _outputVolume = Mathf.Clamp(_volumeSpinner.Angle / 140f, 0f, 1f);
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
            if (_signalEntries.Count > MaxSignalEntries)
                _signalEntries.RemoveAt(0);
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
            if (_signalEntries.Count > MaxSignalEntries)
                _signalEntries.RemoveAt(0);
        }

        public string GetSignalRichText()
        {
            var sb = new StringBuilder(_signalEntries.Count * 20);
            SignalCharType prevType = SignalCharType.Separator;

            for (int i = 0; i < _signalEntries.Count; i++)
            {
                var e = _signalEntries[i];

                if (e.Type != prevType && prevType != SignalCharType.Separator && e.Type != SignalCharType.Separator)
                {
                    sb.Append("<color=#888888>│</color>");
                }

                string color;
                string symbol;
                bool bold = false;
                switch (e.Type)
                {
                    case SignalCharType.Callsign:
                        color = "#00FF00";
                        symbol = e.Ch == '-' ? "▬" : "●";
                        break;
                    case SignalCharType.Dot:
                        color = "#FF2222";
                        symbol = "●";
                        break;
                    case SignalCharType.Dash:
                        color = "#2288FF";
                        symbol = "▬";
                        break;
                    case SignalCharType.Equisignal:
                        color = "#FF00FF";
                        symbol = "◆◆";
                        bold = true;
                        break;
                    default:
                        color = "#FFFFFF";
                        symbol = " ";
                        break;
                }

                if (bold)
                    sb.Append("<b>");
                sb.Append("<color=");
                sb.Append(color);
                sb.Append('>');
                sb.Append(symbol);
                sb.Append("</color>");
                if (bold)
                    sb.Append("</b>");

                prevType = e.Type;
            }
            return sb.ToString();
        }

        public string GetSignalBuffer()
        {
            return GetSignalRichText();
        }

        public void ClearSignalBuffer()
        {
            _signalEntries.Clear();
        }

        private void OnFrequencyChanged(float frequencyKHz, int zoneIndex)
        {
            UpdateTuning(frequencyKHz, zoneIndex);
        }

        private void OnVolumeChanged(float angle)
        {
            _outputVolume = Mathf.Clamp(angle / 140f, 0f, 1f);
#if DEBUG
            Debug.Log("[TestUI][DIAG] Volume=" + (_outputVolume * 100f).ToString("F0") + "%");
#endif
        }

        private void OnFeedbackChanged(float angle)
        {
            _feedback = Mathf.Clamp01(angle / 140f);

            _qualityFactor = 20f + _feedback * _feedback * 180f;

            _atmosphere.NoiseFloor = Mathf.Lerp(0.004f, 0.0005f, _feedback);
            _atmosphere.ImpulseProbability = Mathf.Lerp(0.002f, 0.0002f, _feedback);

            _dsp.WhistleLevel = Mathf.Clamp01((_feedback - 0.75f) / 0.2f);

            UpdateStationGains();
#if DEBUG
            Debug.Log("[TestUI][DIAG] Feedback=" + (_feedback * 100f).ToString("F0") + "%"
                + " Q=" + _qualityFactor.ToString("F0")
                + " noise=" + _atmosphere.NoiseFloor.ToString("F4")
                + " whistle=" + _dsp.WhistleLevel.ToString("F2"));
#endif
        }

        public void UpdateTuning(float frequencyKHz, int zoneIndex)
        {
            _currentFrequencyKHz = frequencyKHz;
            _currentZoneIndex = zoneIndex;
            UpdateStationGains();
        }

        public int StationCount => _stations != null ? _stations.Length : 0;

        public StationDiagnostics GetStationDiagnostics(int index)
        {
            if (_stations == null || index < 0 || index >= _stations.Length)
                return new StationDiagnostics();

            var s = _stations[index];
            return new StationDiagnostics
            {
                Name = BeaconData.Stations[index].Name,
                InBeam = s.InBeam,
                Gain = s.Gain,
                BeatFrequency = s.BeatFrequency,
                TransitionIndex = s.TransitionIndex,
                Pattern = s.Pattern,
                SchedulerPhase = s.Scheduler.CurrentPhase,
                DotCount = s.DotCount,
                CurrentBearing = s.CurrentBearing
            };
        }

        public struct StationDiagnostics
        {
            public string Name;
            public bool InBeam;
            public float Gain;
            public float BeatFrequency;
            public int TransitionIndex;
            public NavigationPattern Pattern;
            public StationScheduler.Phase SchedulerPhase;
            public int DotCount;
            public int CurrentBearing;
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
#if DEBUG
                Debug.Log("[TestUI][DIAG] Tune zone=" + _currentZoneIndex + " (not Band II) → silence");
#endif
                return;
            }

            var stations = BeaconData.Stations;
            float maxGain = 0f;
            int maxIndex = -1;

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
                float gain = 1f / (float)Math.Sqrt(1.0 + _qualityFactor * _qualityFactor * ratio * ratio);

                _stations[i].Gain = gain;

                float offset = _currentFrequencyKHz - freq.Value;
                _stations[i].BeatFrequency = Math.Max(BfoMin, Math.Min(BfoMax, BfoBase + offset * 1000f));
                _stations[i].Carrier.Frequency = _stations[i].BeatFrequency;

                if (gain > maxGain)
                {
                    maxGain = gain;
                    maxIndex = i;
                }
            }

            if (maxGain > 0.01f)
            {
                if (maxIndex != _dominantStationIndex)
                    _signalEntries.Clear();
                _dominantStationIndex = maxIndex;
            }
            else
            {
                _dominantStationIndex = -1;
            }

#if DEBUG
            Debug.Log("[TestUI][DIAG] Tune freq=" + _currentFrequencyKHz.ToString("F1") + " kHz" +
                " zone=" + _currentZoneIndex +
                " Q=" + _qualityFactor.ToString("F0") +
                " dominant=" + (_dominantStationIndex >= 0 ? stations[_dominantStationIndex].Name : "none") +
                " stations=" + CountAudibleStations());
#endif
        }

#if DEBUG
        private int CountAudibleStations()
        {
            int count = 0;
            for (int i = 0; i < _stations.Length; i++)
                if (_stations[i].Gain > 0.01f) count++;
            return count;
        }
#endif

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

                _stations[i].Scheduler.CurrentConsolPattern = consolPattern;
                _stations[i].Scheduler.CalculateCyclePause(consolPattern.Length, 50f);

                _measurements.Add(new PositionFixCalculator.Measurement
                {
                    StationIndex = i,
                    Latitude = (float)stations[i].Latitude,
                    Longitude = (float)stations[i].Longitude,
                    Bearing = bearing,
                    Weight = 1f
                });

#if DEBUG
                Debug.Log("[TestUI][DIAG] Nav station=" + stations[i].Name +
                    " bearing=" + bearing.ToString("F1") + "°" +
                    " dotCount=" + sector.DotCount + "/" + SonneSignalGenerator.TotalSignals +
                    " isDashSector=" + sector.IsDashSector +
                    " localAngle=" + sector.LocalAngle.ToString("F2") + "°" +
                    " patternLen=" + consolPattern.Length);
#endif
            }

            if (_measurements.Count >= 2)
            {
                _lastFix = PositionFixCalculator.Calculate(_measurements);
                _fixValid = _lastFix.Valid;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_currentZoneIndex != SonneZoneIndex)
            {
                Array.Clear(data, 0, data.Length);
#if DEBUG
                Debug.Log("[TestUI][DIAG] Audio clear (zone != Band II)");
#endif
                return;
            }

            for (int i = 0; i < data.Length; i += channels)
            {
                _tickAccumulator += _ticksPerSample;
                while (_tickAccumulator >= 1f)
                {
                    for (int s = 0; s < _stations.Length; s++)
                    {
                        if (_stations[s].Gain < 0.001f)
                            continue;

                        if (!_stations[s].Keyer.IsActive)
                        {
                            var sched = _stations[s].Scheduler;

                            if (sched.CurrentPhase == StationScheduler.Phase.Pause ||
                                sched.CurrentPhase == StationScheduler.Phase.CyclePause)
                            {
                                sched.Tick();
                            }

                            var consolPattern = sched.GetCurrentConsolPattern();
                            if (consolPattern != null && consolPattern.Length > 0)
                            {
                                _stations[s].Keyer.LoadConsol(consolPattern, sched, _stations[s].Config, _stations[s].CallsignLength);
                            }
                            else
                            {
                                var seq = sched.GetCurrentSequence();
                                if (seq == null || seq.ElementCount == 0)
                                {
                                    if (seq != null)
                                        sched.OnSequenceFinished();
                                    sched.Tick();
                                }
                                else
                                {
                                    _stations[s].Keyer.Load(seq, sched, _stations[s].Config);
                                }
                            }
                        }

                        _stations[s].Keyer.Advance();
                    }
                    _tickAccumulator -= 1f;
                }

                float sample = 0f;
                for (int s = 0; s < _stations.Length; s++)
                {
                    if (_stations[s].Gain < 0.001f)
                        continue;
                    if (_stations[s].Keyer.KeyOn)
                        sample += _stations[s].Gain * _stations[s].Carrier.NextSample();
                    else
                        _stations[s].Carrier.NextSample();
                }

                sample = _atmosphere.Apply(sample, SampleRate);
                sample = _dsp.Process(sample, SampleRate);

                for (int c = 0; c < channels; c++)
                    data[i + c] = sample * _outputVolume;
            }
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
