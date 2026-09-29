using UBOAT.Game.Sandbox;
using DWS.Common.Resources;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Own map markers for the radio stations (BeaconData) — a thin subclass
    /// of the shared <see cref="NavMapMarkerBase"/> skeleton (the lighthouse
    /// marker uses the same base). One marker per station: IconSonne icon,
    /// hover shows the name, click pins the multi-line tooltip with
    /// callsign, frequency, country and role (StationTooltipText).
    ///
    /// Stations are static lat/lon data with no sandbox entity, so unlike
    /// the lighthouse there is no save/load handling and no stale-marker
    /// maintenance: positions recompute every frame through
    /// EncounterToSceneSpace (the sandbox origin shifts between sessions).
    /// </summary>
    public sealed class StationMapMarker : NavMapMarkerBase
    {
        private const string IconPrefabName = "IconSonne";
        private const string IconSpriteFallback = "sone";

        protected override int MarkerCount => BeaconData.Stations.Length;

        protected override string PointKey(int index)
        {
            return MapMarkerPoints.StationKey(
                index < BeaconData.Stations.Length ? BeaconData.Stations[index].Name : null);
        }

        protected override MapMarkerKind PointKind(int index)
        {
            return MapMarkerKind.Station;
        }

        /// <summary>Stale cleanup scope: only station keys may be destroyed by
        /// this component's reconcile (never lighthouse/fix keys).</summary>
        protected override bool OwnsKey(string key)
        {
            return !string.IsNullOrEmpty(key)
                && key.StartsWith(MapMarkerPoints.StationKeyPrefix, System.StringComparison.Ordinal);
        }

        protected override string HoverText(int index) => StationTooltipText.Hover(BeaconData.Stations[index]);

        protected override string PinnedText(int index) => StationTooltipText.Pinned(BeaconData.Stations[index]);

        protected override bool HasContent(int index) => true;

        protected override Vector2 GetEncounterPosition(int index) =>
            Coordinates.ToWorldPosition(
                (float)BeaconData.Stations[index].Latitude,
                (float)BeaconData.Stations[index].Longitude);

        protected override Sprite LoadIconSprite()
        {
            if (resourceManager == null)
                return null;

            // IconSonne prefab: SpriteRenderer or UI Image carrying the
            // sprite — the exact structure of the proven IconLighthouse.
            Resource<GameObject> prefab = resourceManager.RetrieveAsset<GameObject>(IconPrefabName, this, false);
            if (prefab.Valid && prefab.Asset != null)
            {
                var image = prefab.Asset.GetComponent<UnityEngine.UI.Image>();
                if (image != null && image.sprite != null)
                    return image.sprite;
                var spriteRenderer = prefab.Asset.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null && spriteRenderer.sprite != null)
                    return spriteRenderer.sprite;
            }

            Resource<Sprite> direct = resourceManager.RetrieveAsset<Sprite>(IconSpriteFallback, this, false);
            if (direct.Valid && direct.Asset != null)
                return direct.Asset;

            // Last resort: the texture behind the sprite import, wrapped in
            // a runtime sprite (bundle lookups never resolve sub-assets).
            Resource<Texture2D> texture = resourceManager.RetrieveAsset<Texture2D>(IconSpriteFallback, this, false);
            if (texture.Valid && texture.Asset != null)
                return Sprite.Create(texture.Asset,
                    new Rect(0f, 0f, texture.Asset.width, texture.Asset.height),
                    new Vector2(0.5f, 0.5f));

            // F229: ResourceManager cannot see the mod bundle, so the loads
            // above return null in a normal session — resolve the icon from
            // the bundle the mod itself loaded (prefab Image/SpriteRenderer,
            // else the bare 'sone' sprite/texture).
            Sprite fromBundle = LoadBundleIconSprite(IconPrefabName, IconSpriteFallback);
            if (fromBundle != null)
                return fromBundle;

            if (!iconWarned)
            {
                iconWarned = true;
                Debug.LogWarning(LogPrefix + " station icon '" + IconPrefabName + "'/'" +
                    IconSpriteFallback + "' not found in bundle; station marker icons will be blank");
            }
            return null;
        }
    }
}
