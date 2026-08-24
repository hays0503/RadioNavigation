using System;
using DWS.Common.Resources;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The mod's own map marker for the lighthouse (ADR-027), now a thin
    /// subclass of the shared <see cref="NavMapMarkerBase"/> skeleton (the
    /// station markers reuse the same base). Own content: the lighthouse
    /// sandbox entity placed by LighthousePlacer, the profile name and light
    /// characteristic, stale game-marker cleanup for old saves, and the
    /// gated position diagnostics log.
    ///
    /// The sandbox entity stays DisplayInUI=false / IsPublic=false, so the
    /// game never creates its own (Unknown-icon) marker for it.
    /// </summary>
    public sealed class LighthouseMapMarker : NavMapMarkerBase
    {
        private const string IconAssetName = "IconLighthouse";
        private const float PositionLogIntervalSeconds = 2f;

        private string lighthouseName = "Lighthouse";
        private string lighthouseCharacteristic = "";
        private float positionLogTimer;

        protected override int MarkerCount => 1;

        protected override string MarkerRootName(int index) => "RadioNavLighthouseMapMarker";

        protected override string HoverText(int index) => lighthouseName;

        protected override string PinnedText(int index) =>
            string.IsNullOrEmpty(lighthouseCharacteristic)
                ? lighthouseName
                : lighthouseName + " · " + lighthouseCharacteristic;

        protected override bool HasContent(int index) =>
            LighthousePlacer.LighthouseEntity != null;

        protected override Vector2 GetEncounterPosition(int index) =>
            LighthousePlacer.LighthouseEntity.Position;

        protected override float MaintenanceIntervalSeconds => 1f;

        protected override void OnBeforeBuildViews() => ResolveProfileInfo();

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
            Debug.LogWarning(LogPrefix + " lighthouse icon '" + IconAssetName +
                "' not found in bundle (prefab Image/SpriteRenderer or Sprite); marker icon will be blank");
            return null;
        }

        protected override void OnMaintenance() => DestroyStaleGameMarkers();

        protected override void OnMarkerPositioned(int index, Vector3 worldPosition, float scale, float iconPx)
        {
            if (!RadioNavDiagnostics.IsEnabledCached(RadioNavDiagnostics.LighthouseMapMarkerPositionKey, true))
            {
                return;
            }

            positionLogTimer += Time.unscaledDeltaTime;
            if (positionLogTimer < PositionLogIntervalSeconds)
            {
                return;
            }

            positionLogTimer = 0f;
            Vector2 entityPos = LighthousePlacer.LighthouseEntity.Position;
            Debug.Log(LogPrefix + " lighthouse icon: entityPos=" +
                entityPos.x.ToString("F2") + "," + entityPos.y.ToString("F2") +
                " worldPos=" + worldPosition.ToString("F2") +
                " elevation=" + mainCamera.MapController.CameraElevation.ToString("F0") +
                " scale=" + scale.ToString("F4") +
                " px=" + iconPx.ToString("F0") + "x" + iconPx.ToString("F0"));
        }

        private void ResolveProfileInfo()
        {
            foreach (LighthouseProfile profile in LighthouseProfiles.Active())
            {
                lighthouseName = profile.Name;
                lighthouseCharacteristic = profile.Characteristic;
                return;
            }
        }

        /// <summary>
        /// Old saves contain the lighthouse entity with DisplayInUI/IsPublic
        /// true, for which the game already created an EntityMarkerUI with
        /// the Unknown icon. Remove those so only our own marker remains.
        /// </summary>
        private void DestroyStaleGameMarkers()
        {
            EntityMarkerUI[] markers = FindObjectsOfType<EntityMarkerUI>();
            for (int i = 0; i < markers.Length; i++)
            {
                EntityMarkerUI marker = markers[i];
                if (marker == null || marker.Entity == null || marker.Entity.Name == null)
                {
                    continue;
                }
                if (marker.Entity.Name.StartsWith("Lighthouse "))
                {
                    Debug.Log(LogPrefix + " removed stale game marker for '" + marker.Entity.Name + "'");
                    Destroy(marker.gameObject);
                }
            }
        }
    }
}
