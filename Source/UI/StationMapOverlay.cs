using System;
using UBOAT.Game.Sandbox;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Legacy cleanup for the map opened with M. Station points are now
    /// drawn by <see cref="StationMapMarker"/> (own icon + tooltip markers,
    /// ADR-027 pattern); this component only removes the old saved
    /// MapPointUI station points from previous mod versions so they do not
    /// duplicate the new markers.
    /// </summary>
    public sealed class StationMapOverlay : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Map]";
        private const string MarkerNamePrefix = "RadioNavStationMarker_";
        private const float LegacyPositionTolerance = 0.05f;
        private const float RetryIntervalSeconds = 0.5f;

        private float retryTimer;
        private bool legacyCleanupPending = true;

        private void Update()
        {
            if (!legacyCleanupPending)
                return;

            retryTimer += Time.unscaledDeltaTime;
            if (retryTimer < RetryIntervalSeconds)
                return;

            retryTimer = 0f;
            MapChartingUI charting = FindObjectOfType<MapChartingUI>();
            if (charting == null)
                return;

            int removed = RemoveLegacyMarkers(charting);
            legacyCleanupPending = false;
            if (removed > 0)
                Debug.Log(LogPrefix + " removed legacy saved station markers=" + removed);
        }

        private int RemoveLegacyMarkers(MapChartingUI charting)
        {
            int removed = 0;
            MapPointUI[] points = charting.GetComponentsInChildren<MapPointUI>(true);
            for (int i = 0; i < points.Length; i++)
            {
                MapPointUI point = points[i];
                if (point == null
                    || point.Data == null
                    || point.gameObject.name.StartsWith(MarkerNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                for (int stationIndex = 0; stationIndex < BeaconData.Stations.Length; stationIndex++)
                {
                    BeaconStation station = BeaconData.Stations[stationIndex];
                    Vector2 stationPosition = Coordinates.ToWorldPosition(station.Latitude, station.Longitude);
                    if (!string.Equals(point.Name, station.Name, StringComparison.Ordinal)
                        || Vector2.Distance(point.Position, stationPosition) > LegacyPositionTolerance)
                    {
                        continue;
                    }

                    Destroy(point.gameObject);
                    removed++;
                    break;
                }
            }

            return removed;
        }
    }
}
