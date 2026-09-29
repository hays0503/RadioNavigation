using System;
using System.Collections.Generic;
using DWS.Common.Resources;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The mod's own map markers for the lighthouse fleet (ADR-027 pattern,
    /// thin subclass of <see cref="NavMapMarkerBase"/>). One marker per
    /// enabled fleet profile — curated plus the LeuchtfeuerRegistry theater
    /// subset (210 total after the 2026-09-09 land curation, ADR-030, and
    /// fire-height curation, ADR-031: only lights with fireHeight >= 15 m
    /// stay in the fleet; declutter keeps them readable at far zoom).
    /// Positions come straight from the profile
    /// lat/lon (encounter km), so a marker exists whether or not the
    /// distance-gated 3D model is currently spawned.
    ///
    /// The sandbox entities stay DisplayInUI=false / IsPublic=false, so the
    /// game never creates its own (Unknown-icon) markers for them; stale
    /// game markers from old saves are still cleaned up.
    /// </summary>
    public sealed class LighthouseMapMarker : NavMapMarkerBase
    {
        private const string IconAssetName = "IconLighthouse";
        private const float PositionLogIntervalSeconds = 2f;

        private readonly List<LighthouseProfile> fleet = new List<LighthouseProfile>();
        private float positionLogTimer;
        private bool staleCleanupDone;

        protected override int MarkerCount => fleet.Count;

        /// <summary>ADR-032: overlapping markers collapse onto the tallest
        /// light (fire height drives both the priority here and the actual
        /// 3D model scale, so the survivor is also the biggest landmark).</summary>
        protected override bool DeclutterEnabled => true;

        protected override float Priority(int index)
        {
            return index < fleet.Count ? fleet[index].FireHeightM : 0f;
        }

        protected override string PointKey(int index)
        {
            return index < fleet.Count
                ? MapMarkerPoints.LighthouseKey(fleet[index].Code)
                : MapMarkerPoints.Prefix + "LH";
        }

        protected override MapMarkerKind PointKind(int index)
        {
            return MapMarkerKind.Lighthouse;
        }

        /// <summary>Stale cleanup scope: only lighthouse keys may be destroyed
        /// by this component's reconcile (never station/fix keys).</summary>
        protected override bool OwnsKey(string key)
        {
            return !string.IsNullOrEmpty(key)
                && key.StartsWith(MapMarkerPoints.LighthouseKeyPrefix, StringComparison.Ordinal);
        }

        protected override string HoverText(int index) => Name(index);

        protected override string PinnedText(int index)
        {
            string characteristic = index < fleet.Count ? fleet[index].Characteristic : null;
            return string.IsNullOrEmpty(characteristic)
                ? Name(index)
                : Name(index) + " · " + characteristic;
        }

        protected override bool HasContent(int index) => index < fleet.Count;

        protected override Vector2 GetEncounterPosition(int index)
        {
            LighthouseProfile profile = fleet[index];
            return Coordinates.ToWorldPosition(profile.Latitude, profile.Longitude);
        }

        protected override float MaintenanceIntervalSeconds => 1f;

        protected override void PrepareContent()
        {
            fleet.Clear();
            foreach (LighthouseProfile profile in LighthouseProfiles.EnabledFleet())
            {
                fleet.Add(profile);
            }
        }

        protected override Sprite LoadIconSprite()
        {
            if (resourceManager == null)
            {
                return null;
            }

            // The bundle ships the icon as a prefab carrying the sprite on a
            // UI Image or a SpriteRenderer; a bare Sprite asset is accepted
            // as a fallback.
            Resource<GameObject> prefab = resourceManager.RetrieveAsset<GameObject>(IconAssetName, this, false);
            if (prefab.Valid && prefab.Asset != null)
            {
                var image = prefab.Asset.GetComponent<UnityEngine.UI.Image>();
                if (image != null && image.sprite != null)
                {
                    return image.sprite;
                }

                var spriteRenderer = prefab.Asset.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null && spriteRenderer.sprite != null)
                {
                    return spriteRenderer.sprite;
                }
            }

            Resource<Sprite> direct = resourceManager.RetrieveAsset<Sprite>(IconAssetName, this, false);
            if (direct.Valid)
            {
                return direct.Asset;
            }

            // F229: ResourceManager cannot see the mod bundle, so the loads
            // above return null in a normal session — resolve the icon from
            // the bundle the mod itself loaded (prefab Image/SpriteRenderer).
            Sprite fromBundle = LoadBundleIconSprite(IconAssetName, null);
            if (fromBundle != null)
                return fromBundle;

            if (!iconWarned)
            {
                iconWarned = true;
                Debug.LogWarning(LogPrefix + " lighthouse icon '" + IconAssetName +
                    "' not found in bundle (prefab Image/SpriteRenderer or Sprite); marker icon will be blank");
            }
            return null;
        }

        protected override void OnMaintenance()
        {
            // One successful scan with nothing left to remove means the old-save
            // stale markers are gone; stop the whole-scene FindObjectsOfType
            // every second for the rest of the session.
            if (staleCleanupDone)
                return;
            if (DestroyStaleGameMarkers() == 0)
                staleCleanupDone = true;
        }

        protected override void OnMarkerPositioned(int index, Vector3 worldPosition, float scale, float iconPx)
        {
            if (index != 0 || !RadioNavDiagnostics.IsEnabledCached(RadioNavDiagnostics.LighthouseMapMarkerPositionKey, false))
            {
                return;
            }

            positionLogTimer += Time.unscaledDeltaTime;
            if (positionLogTimer < PositionLogIntervalSeconds)
            {
                return;
            }

            positionLogTimer = 0f;
            Debug.Log(RadioNavDiagnostics.Stamp() + LogPrefix + " lighthouse[0] icon: worldPos=" + worldPosition.ToString("F2") +
                " elevation=" + mainCamera.MapController.CameraElevation.ToString("F0") +
                " scale=" + scale.ToString("F4") +
                " px=" + iconPx.ToString("F0") + "x" + iconPx.ToString("F0"));
        }

        private string Name(int index)
        {
            return index < fleet.Count ? fleet[index].Name : "Lighthouse";
        }

        /// <summary>
        /// Old saves contain lighthouse entities with DisplayInUI/IsPublic
        /// true, for which the game already created an EntityMarkerUI with
        /// the Unknown icon. Remove those so only our own markers remain.
        /// </summary>
        private int DestroyStaleGameMarkers()
        {
            int removed = 0;
            EntityMarkerUI[] markers = FindObjectsOfType<EntityMarkerUI>();
            for (int i = 0; i < markers.Length; i++)
            {
                EntityMarkerUI marker = markers[i];
                if (marker == null || marker.Entity == null || marker.Entity.Name == null)
                {
                    continue;
                }

                if (marker.Entity.Name.StartsWith("Lighthouse ", StringComparison.Ordinal))
                {
                    Debug.Log(LogPrefix + " removed stale game marker for '" + marker.Entity.Name + "'");
                    Destroy(marker.gameObject);
                    removed++;
                }
            }
            return removed;
        }
    }
}
