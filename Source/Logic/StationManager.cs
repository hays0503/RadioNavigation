using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Station fleet state + tuning/navigation behind SonneController (P5.6
    /// slice SC-2, F02). The controller keeps Unity lifecycle, audio I/O,
    /// tuning state and the position-feed polling; fleet arrays, gain
    /// selection, navigation, position and diagnostics live here. The array
    /// instances are shared by reference (built once by StationFleet), so
    /// the audio thread keeps reading the same instances through the
    /// Stations/Keyers/Carriers properties. Main-thread writes only here;
    /// the audio thread only reads. Pure C# apart from Time/Debug.
    /// </summary>
    public sealed class StationManager
    {
        public StationState[] Stations { get; }

        public StationScheduler[] Schedulers { get; }

        public MorseKeyer[] Keyers { get; }

        public CarrierGenerator[] Carriers { get; }

        public int DominantStationIndex { get; private set; } = -1;

        public int StationCount => Stations != null ? Stations.Length : 0;

        public bool HasPosition => positionAvailable;

        public float PlayerLatitude => playerLatitude;

        public float PlayerLongitude => playerLongitude;

        private float playerLatitude;
        private float playerLongitude;
        private bool positionAvailable;
        private bool bearingDiagnosticsLogged;
        private float lastBearingLogLatitude;
        private float lastBearingLogLongitude;

        public StationManager(StationFleet fleet)
        {
            if (fleet == null)
                throw new ArgumentNullException("fleet");
            Stations = fleet.States;
            Schedulers = fleet.Schedulers;
            Keyers = fleet.Keyers;
            Carriers = fleet.Carriers;
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
            UpdateNavigation();
        }

        public void UpdateGains(bool inZone, float frequencyKHz, float qualityFactor, Action dominantChanged)
        {
            if (!inZone)
            {
                for (int i = 0; i < Stations.Length; i++)
                    Stations[i].Gain = 0f;
                DominantStationIndex = -1;
                return;
            }

            // F09/F58: zero or NaN carrier frequency must not enter the gain
            // math (division by zero / NaN ratio). Silence all stations
            // instead of leaving stale gains behind.
            float f = frequencyKHz * 1000f;
            if (f.IsNegligible(0.001f) || float.IsNaN(f))
            {
                for (int i = 0; i < Stations.Length; i++)
                    Stations[i].Gain = 0f;
                DominantStationIndex = -1;
                return;
            }

            var beaconStations = BeaconData.Stations;
            float maxGain = 0f;
            int maxIndex = -1;

            for (int i = 0; i < beaconStations.Length; i++)
            {
                if (beaconStations[i].Role != BeaconStationRole.DirectionalSonne)
                {
                    Stations[i].Gain = 0f;
                    continue;
                }

                float? freq = beaconStations[i].History?[0].FrequencyKHz;
                if (freq.IsNull())
                {
                    Stations[i].Gain = 0f;
                    continue;
                }

                float gain = StationGainModel.ComputeGain(frequencyKHz, freq.Value, qualityFactor);

                Stations[i].Gain = gain;

                float offset = frequencyKHz - freq.Value;
                Stations[i].BeatFrequency = StationGainModel.ComputeBeatFrequency(offset);
                Carriers[i].Frequency = Stations[i].BeatFrequency;

                if (gain > maxGain)
                {
                    maxGain = gain;
                    maxIndex = i;
                }
            }

            if (maxGain.IsSignificant(0.01f))
            {
                if (maxIndex != DominantStationIndex)
                {
                    if (dominantChanged != null)
                        dominantChanged();
                }
                DominantStationIndex = maxIndex;
            }
            else
            {
                DominantStationIndex = -1;
            }
        }

        public void UpdateNavigation()
        {
            var beaconStations = BeaconData.Stations;

            for (int i = 0; i < beaconStations.Length; i++)
            {
                StationNavigator.UpdateStation(
                    ref Stations[i], beaconStations[i],
                    playerLatitude, playerLongitude, Schedulers[i]);
            }

            LogBearingDiagnostics(beaconStations);
        }

        public StationDiagnostics GetStationDiagnostics(int index)
        {
            if (IsInvalidStationIndex(index))
                return new StationDiagnostics();

            var s = Stations[index];
            var station = BeaconData.Stations[index];
            StationCallsignResolver.TryGet(station, out string callsign);

            float elapsed = 0f;
            if (IsStationNavigating(index))
                elapsed = Time.time - s.NavigationStartTime;

            return StationDiagnosticsReport.Build(
                station, callsign, s, Schedulers[index].CurrentPhase, elapsed);
        }

        public bool IsDominantAudible(int index, bool inZone)
        {
            return inZone
                && !Stations[index].Gain.IsNegligible(0.001f)
                && index == DominantStationIndex;
        }

        private void LogBearingDiagnostics(BeaconStation[] beaconStations)
        {
            if (!positionAvailable
                || (bearingDiagnosticsLogged
                    && Math.Abs(playerLatitude - lastBearingLogLatitude) < 0.00001f
                    && Math.Abs(playerLongitude - lastBearingLogLongitude) < 0.00001f))
                return;

            foreach (string line in BearingDiagnostics.BuildLines(
                playerLatitude, playerLongitude, beaconStations, Stations))
            {
                Debug.Log(line);
            }

            lastBearingLogLatitude = playerLatitude;
            lastBearingLogLongitude = playerLongitude;
            bearingDiagnosticsLogged = true;
        }

        private bool IsInvalidStationIndex(int index) =>
            Stations.IsNull() || index.IsNegative() || index >= Stations.Length;

        private bool IsStationNavigating(int index) =>
            Schedulers[index].CurrentPhase == StationScheduler.Phase.Navigation
            && Stations[index].NavigationStartTime.IsPositive();

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
