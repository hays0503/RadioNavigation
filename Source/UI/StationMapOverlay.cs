using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Debug overlay for the map opened with M. Uses the game's own map point
    // prefab without registering fixed stations in the saveable charting DB.
    // The lighthouse has its own marker (LighthouseMapMarker) — this overlay
    // only handles the station points.
    public sealed class StationMapOverlay : MonoBehaviour
    {
        private const string MarkerNamePrefix = "RadioNavStationMarker_";
        private const float LegacyPositionTolerance = 0.05f;

        private static readonly FieldInfo PointPrefabField = typeof(MapChartingUI).GetField(
            "pointPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LinesContainerField = typeof(MapChartingUI).GetField(
            "linesContainer", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<MapPointUI> markers = new List<MapPointUI>();
        private float retryTimer;
        private bool markersCreated;
        private bool legacyCleanupPending = true;
        private SavesManager savesManager;

        private void Start()
        {
            savesManager = ScriptableObjectSingleton.LoadSingleton<SavesManager>();
            if (savesManager != null)
            {
                savesManager.LoadingStarted += OnLoadingStarted;
                savesManager.Loaded += OnLoaded;
            }

            TryCreateMarkers();
        }

        private void Update()
        {
            if (markersCreated && AreMarkersAlive())
            {
                MapPointUI.UpdatePoints(markers);
            }
            else
            {
                if (markersCreated)
                    ClearOwnedMarkers();

                if (!markersCreated && AreMarkersAlive())
                {
                    markersCreated = true;
                    MapPointUI.UpdatePoints(markers);
                }
                else
                {
                    retryTimer += Time.unscaledDeltaTime;
                    if (retryTimer >= 0.5f)
                    {
                        retryTimer = 0f;
                        TryCreateMarkers();
                    }
                }
            }
        }

        private void TryCreateMarkers()
        {
            MapChartingUI charting = FindObjectOfType<MapChartingUI>();
            if (charting == null)
                return;

            MapPointUI pointPrefab = PointPrefabField != null
                ? PointPrefabField.GetValue(charting) as MapPointUI
                : null;
            Transform linesContainer = LinesContainerField != null
                ? LinesContainerField.GetValue(charting) as Transform
                : null;
            if (pointPrefab == null || linesContainer == null)
            {
                Debug.LogWarning("[RadioNav][Map] native MapPointUI prefab/container not ready");
                return;
            }

            Debug.Log("[RadioNav][Map] MapChartingUI=" + charting.name
                + " active=" + charting.gameObject.activeInHierarchy);

            if (legacyCleanupPending)
            {
                int removed = RemoveLegacyMarkers(charting);
                legacyCleanupPending = false;
                if (removed > 0)
                {
                    Debug.Log("[RadioNav][Map] removed legacy saved station markers=" + removed);
                    retryTimer = 0f;
                    return;
                }
            }

            if (AreMarkersAlive())
            {
                markersCreated = true;
                MapPointUI.UpdatePoints(markers);
                return;
            }

            if (markers.Count > 0)
                ClearOwnedMarkers();

            int count = BeaconData.Stations.Length;
            var created = new List<MapPointUI>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    BeaconStation station = BeaconData.Stations[i];
                    Vector2 position = Coordinates.ToWorldPosition(station.Latitude, station.Longitude);
                    var data = new MapPointData(position)
                    {
                        Name = station.Name
                    };
                    MapPointUI point = pointPrefab.Duplicate(data);
                    if (point == null)
                        throw new InvalidOperationException("MapPointUI.Duplicate returned null");

                    point.gameObject.name = MarkerNamePrefix + i;
                    point.Interactive = false;
                    point.transform.SetParent(linesContainer, false);
                    created.Add(point);
                    Debug.Log("[RadioNav][Map] marker=" + i
                        + " name=" + station.Name
                        + " lat=" + station.Latitude.ToString("F6")
                        + " lon=" + station.Longitude.ToString("F6")
                        + " encounter=" + position
                        + " active=" + point.gameObject.activeInHierarchy);
                }

                MapPointUI.UpdatePoints(created);
                markers.AddRange(created);
                markersCreated = true;
                Debug.Log("[RadioNav][Map] transient station markers created=" + count);
            }
            catch (Exception ex)
            {
                for (int i = 0; i < created.Count; i++)
                {
                    if (created[i] != null)
                        Destroy(created[i].gameObject);
                }

                Debug.LogError("[RadioNav][Map] marker creation rolled back: " + ex.Message);
            }
        }

        private bool AreMarkersAlive()
        {
            if (markers.Count != BeaconData.Stations.Length)
                return false;

            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] == null)
                    return false;
            }

            return true;
        }

        private void ClearOwnedMarkers()
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] != null)
                    Destroy(markers[i].gameObject);
            }

            markers.Clear();
            markersCreated = false;
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

        private void OnLoadingStarted()
        {
            markersCreated = false;
            legacyCleanupPending = false;
            retryTimer = 0f;
        }

        private void OnLoaded(Queue<Action> queue)
        {
            if (queue != null)
            {
                queue.Enqueue(OnSaveLoadCompleted);
                return;
            }

            OnSaveLoadCompleted();
        }

        private void OnSaveLoadCompleted()
        {
            markersCreated = false;
            legacyCleanupPending = true;
            retryTimer = 0f;
        }

        private void OnDestroy()
        {
            if (savesManager != null)
            {
                savesManager.LoadingStarted -= OnLoadingStarted;
                savesManager.Loaded -= OnLoaded;
            }

            ClearOwnedMarkers();
        }
    }
}
