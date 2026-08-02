using System;
using System.Collections.Generic;
using UBOAT.Game.Sandbox;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Debug overlay for the map opened with M. Uses the game's own map point
    // prefab so sprite, label, canvas and camera scaling match native points.
    public sealed class StationMapOverlay : MonoBehaviour
    {
        private const int DebugStationCount = 6;
        private readonly List<MapPointUI> markers = new List<MapPointUI>();
        private float retryTimer;
        private bool markersCreated;

        private void Start()
        {
            TryCreateMarkers();
        }

        private void Update()
        {
            if (markersCreated)
            {
                MapPointUI.UpdatePoints(markers);
                return;
            }

            retryTimer += Time.unscaledDeltaTime;
            if (retryTimer < 0.5f)
                return;

            retryTimer = 0f;
            TryCreateMarkers();
        }

        private void TryCreateMarkers()
        {
            MapChartingUI charting = FindObjectOfType<MapChartingUI>();
            if (charting.IsNull())
                return;

            Debug.Log("[RadioNav][Map] MapChartingUI=" + charting.name
                + " active=" + charting.gameObject.activeInHierarchy);

            int count = Math.Min(DebugStationCount, BeaconData.Stations.Length);
            for (int i = 0; i < count; i++)
            {
                BeaconStation station = BeaconData.Stations[i];
                Vector2 position = Coordinates.ToWorldPosition(station.Latitude, station.Longitude);
                MapPointUI point = charting.CreatePoint(position, station.Name);
                point.Interactive = false;
                markers.Add(point);
                Debug.Log("[RadioNav][Map] marker=" + i
                    + " name=" + station.Name
                    + " lat=" + station.Latitude.ToString("F6")
                    + " lon=" + station.Longitude.ToString("F6")
                    + " encounter=" + position
                    + " active=" + point.gameObject.activeInHierarchy);
            }

            markersCreated = true;
            MapPointUI.UpdatePoints(markers);
            Debug.Log("[RadioNav][Map] station markers created=" + count);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].IsNotNull())
                    Destroy(markers[i].gameObject);
            }
            markers.Clear();
        }
    }
}
