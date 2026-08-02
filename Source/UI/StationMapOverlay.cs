using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Effects;
using UnityEngine;
using TMPro;

namespace UBOAT.Mods.RadioNavigation
{
    // Debug overlay for the map opened with M. It uses the same world-space
    // layer as the game's MapRenderer, so markers pan and zoom with the map.
    public sealed class StationMapOverlay : MonoBehaviour
    {
        private const int DebugStationCount = 6;
        private const float MapLabelHeight = 1100f;
        private const float LabelScale = 0.8f;
        private readonly List<GameObject> markers = new List<GameObject>();
        private float retryTimer;
        private bool markersCreated;
        private static readonly FieldInfo LabelPrefabField = typeof(MapRenderer).GetField(
            "labelPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

        private void Start()
        {
            TryCreateMarkers();
        }

        private void Update()
        {
            if (markersCreated)
                return;

            retryTimer += Time.unscaledDeltaTime;
            if (retryTimer < 0.5f)
                return;

            retryTimer = 0f;
            TryCreateMarkers();
        }

        private void TryCreateMarkers()
        {
            MapRenderer mapRenderer = FindObjectOfType<MapRenderer>();
            if (mapRenderer.IsNull() || mapRenderer.MapTopLayer.IsNull())
            {
                return;
            }

            if (LabelPrefabField.IsNull())
                return;

            MapLabel labelPrefab = LabelPrefabField.GetValue(mapRenderer) as MapLabel;
            if (labelPrefab.IsNull())
                return;

            Debug.Log("[RadioNav][Map] renderer=" + mapRenderer.name
                + " topLayer=" + mapRenderer.MapTopLayer.name
                + " active=" + mapRenderer.MapTopLayer.activeInHierarchy);

            int count = Math.Min(DebugStationCount, BeaconData.Stations.Length);
            for (int i = 0; i < count; i++)
            {
                if (!CreateMarker(mapRenderer.MapTopLayer.transform, labelPrefab, BeaconData.Stations[i], i))
                    return;
            }

            markersCreated = true;
            Debug.Log("[RadioNav][Map] station markers created=" + count);
        }

        private bool CreateMarker(Transform parent, MapLabel labelPrefab, BeaconStation station, int index)
        {
            Vector2 worldPosition = Coordinates.ToWorldPosition(station.Latitude, station.Longitude);
            TextMeshProUGUI sourceLabel = labelPrefab.GetComponent<TextMeshProUGUI>();
            if (sourceLabel.IsNull() || sourceLabel.font.IsNull())
                return false;

            var marker = new GameObject("RadioNavStation" + index);
            marker.transform.SetParent(parent, false);
            marker.layer = parent.gameObject.layer;
            marker.transform.position = new Vector3(
                worldPosition.x * 1000f,
                MapLabelHeight,
                worldPosition.y * 1000f);
            marker.transform.localScale = Vector3.one * LabelScale;
            marker.transform.localEulerAngles = new Vector3(90f, 0f, 0f);

            var label = marker.AddComponent<TextMeshPro>();
            label.font = sourceLabel.font;
            label.fontSharedMaterial = sourceLabel.fontSharedMaterial;
            label.fontSize = 36f;
            label.color = new Color(1f, 0.82f, 0.15f, 1f);
            label.text = "● " + station.Name;
            label.alignment = TextAlignmentOptions.Left;

            markers.Add(marker);
            Debug.Log("[RadioNav][Map] marker=" + index
                + " name=" + station.Name
                + " lat=" + station.Latitude.ToString("F6")
                + " lon=" + station.Longitude.ToString("F6")
                + " world=" + worldPosition
                + " scene=" + marker.transform.position
                + " layer=" + marker.layer
                + " activeSelf=" + marker.activeSelf
                + " activeInHierarchy=" + marker.activeInHierarchy);
            return true;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].IsNotNull())
                    Destroy(markers[i]);
            }
            markers.Clear();
        }
    }
}
