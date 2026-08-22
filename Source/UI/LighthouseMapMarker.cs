using System;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using TMPro;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The mod's own map marker for the lighthouse. A world-space canvas
    /// (icon + invisible hitbox + tooltip) parented under the map UI canvas
    /// (the game's EntityMarkersManager parent), so it inherits the same
    /// canvas scale as the game's markers — but the visuals, interaction and
    /// visibility window are fully ours: hover shows the name, click pins
    /// the name + light characteristic from the profile.
    ///
    /// Sizing: zoom-dependent pixel size — 1× base = 25 px, scaling up to
    /// 50×50 px at the closest zoom (elevation ≤ 1000), back to 25 px at the
    /// farthest zoom (elevation ≥ 1400000). The marker scale is recomputed
    /// each frame from the camera FOV, elevation and screen height. The
    /// marker is shown whenever the map is open (any zoom), unlike the
    /// game's markers which are hidden at camera height below 500 m.
    /// The tooltip is TextMeshProUGUI with a background sized from the
    /// label's preferred values (MapMarkerBase.Title).
    ///
    /// The sandbox entity stays DisplayInUI=false / IsPublic=false, so the
    /// game never creates its own (Unknown-icon) marker for it.
    /// </summary>
    public sealed class LighthouseMapMarker : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Map]";
        private const string IconAssetName = "IconLighthouse";
        private const float HitboxSize = 96f;
        private const float BackdropSize = 80f;
        private const int BackdropTextureSize = 64;
        // Extra lift of the marker above the map surface (user request):
        // y = 1.5 + elevation * 0.0005 + SpriteLiftY.
        private const float SpriteLiftY = 100f;
        private const float TooltipOffsetY = 40f;
        private const float TooltipPaddingX = 20f;
        private const float TooltipPaddingY = 4f;
        private const int TooltipFontSize = 30;
        private const float StaleCleanupIntervalSeconds = 1f;
        private const float PositionLogIntervalSeconds = 2f;

        [Inject] private static ResourceManager resourceManager = null;
        [Inject] private static Sandbox sandbox = null;

        private static TMP_FontAsset tooltipFont;
        private static Sprite backdropSprite;

        private EntityMarkersManager markersManager;
        private MainCamera mainCamera;
        private GameObject markerRoot;
        private GameObject tooltip;
        private TextMeshProUGUI tooltipText;
        private Image tooltipBackground;
        private Sprite iconSprite;
        private string lighthouseName = "Lighthouse";
        private string lighthouseCharacteristic = "";
        private bool pinned;
        private bool built;
        private float staleCleanupTimer;
        private float positionLogTimer;

        private void Update()
        {
            if (markersManager == null)
            {
                markersManager = FindObjectOfType<EntityMarkersManager>();
                if (markersManager == null)
                {
                    return;
                }
            }

            if (mainCamera == null)
            {
                mainCamera = FindObjectOfType<MainCamera>();
                if (mainCamera == null)
                {
                    return;
                }
            }

            if (!built)
            {
                BuildMarker();
                if (!built)
                {
                    return;
                }
            }

            staleCleanupTimer += Time.unscaledDeltaTime;
            if (staleCleanupTimer >= StaleCleanupIntervalSeconds)
            {
                staleCleanupTimer = 0f;
                DestroyStaleGameMarkers();
            }

            // Own visibility gate: the marker shows whenever the map is open,
            // at ANY zoom — unlike the game's manager, which deactivates its
            // whole subtree at camera height below 500 m (closest zoom).
            bool mapOpen = mainCamera.isActiveAndEnabled &&
                mainCamera.CurrentMode == CameraMode.Map &&
                mainCamera.MapController != null &&
                mainCamera.MapController.Mode != MapCameraController.MapDisplayMode.Headquarters;
            if (!mapOpen || sandbox == null)
            {
                markerRoot.SetActive(false);
                return;
            }

            SandboxEntity lighthouseEntity = LighthousePlacer.LighthouseEntity;
            if (lighthouseEntity == null)
            {
                markerRoot.SetActive(false);
                return;
            }

            float elevation = mainCamera.MapController.CameraElevation;
            if (elevation <= 0f)
            {
                markerRoot.SetActive(false);
                return;
            }

            // Zoom-dependent pixel size math lives in the pure, unit-tested
            // LighthouseMarkerSizing (ADR-027): 25 px base at the farthest
            // zoom, scaling up to 50×50 px at the closest zoom.
            Camera mapCam = mainCamera.Camera;
            float fov = mapCam != null ? mapCam.fieldOfView : LighthouseMarkerSizing.DefaultFovDegrees;
            double unitsPerPixel = LighthouseMarkerSizing.UnitsPerPixel(elevation, fov, Screen.height);
            float iconPx = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);
            float parentLossy = markersManager.transform.lossyScale.x;
            float scale = (float)LighthouseMarkerSizing.MarkerScale(iconPx, unitsPerPixel, parentLossy);

            markerRoot.SetActive(true);
            // Lift the marker above the terrain: the canvas plane at y=0 is
            // coplanar with the map surface, which clips the lower half of
            // the icon. Same offset formula as the game's charting points
            // (MapPointUI.UpdatePoints: y = 1.5 + elevation * 0.0005), plus
            // the requested extra SpriteLiftY lift.
            Vector3 markerPosition = sandbox.EncounterToSceneSpace(lighthouseEntity.Position);
            markerPosition.y = 1.5f + elevation * 0.0005f + SpriteLiftY;
            markerRoot.transform.position = markerPosition;
            markerRoot.transform.localScale = new Vector3(scale, scale, scale);
            // The icon lies flat on the map plane (horizontal), not
            // billboarded to the camera — Euler(90,0,0) turns the canvas
            // +Z (default forward) to face straight up.
            markerRoot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (RadioNavDiagnostics.IsEnabled(RadioNavDiagnostics.LighthouseMapMarkerPositionKey, true))
            {
                positionLogTimer += Time.unscaledDeltaTime;
                if (positionLogTimer >= PositionLogIntervalSeconds)
                {
                    positionLogTimer = 0f;
                    Vector2 entityPos = lighthouseEntity.Position;
                    Debug.Log(LogPrefix + " lighthouse icon: entityPos=" +
                        entityPos.x.ToString("F2") + "," + entityPos.y.ToString("F2") +
                        " worldPos=" + markerRoot.transform.position.ToString("F2") +
                        " elevation=" + elevation.ToString("F0") +
                        " scale=" + scale.ToString("F4") +
                        " px=" + iconPx.ToString("F0") + "x" + iconPx.ToString("F0"));
                }
            }
        }

        private void BuildMarker()
        {
            if (iconSprite == null)
            {
                iconSprite = LoadIconSprite();
                if (iconSprite == null)
                {
                    return;
                }
            }
            ResolveProfileInfo();

            markerRoot = new GameObject("RadioNavLighthouseMapMarker", typeof(RectTransform));
            // Direct child of the manager GO: same canvas context (render
            // order above the map surface, inherited scale) as the game's
            // own markers — the previous sibling-parenting rendered our
            // separate canvas behind the map.
            markerRoot.transform.SetParent(markersManager.transform, false);

            Canvas canvas = markerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            markerRoot.AddComponent<GraphicRaycaster>();

            var rootRect = markerRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(HitboxSize, HitboxSize);

            // Light circular backdrop behind the black glyph so the icon
            // reads on the dark map (port markers use the same badge look).
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(markerRoot.transform, false);
            var backdropImage = backdropGo.AddComponent<Image>();
            backdropImage.sprite = GetBackdropSprite();
            backdropImage.color = new Color(0.95f, 0.92f, 0.85f, 0.9f);
            backdropImage.raycastTarget = false;
            var backdropRect = backdropGo.GetComponent<RectTransform>();
            backdropRect.sizeDelta = new Vector2(BackdropSize, BackdropSize);
            backdropRect.anchoredPosition = Vector2.zero;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(markerRoot.transform, false);
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.sprite = iconSprite;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(
                LighthouseMarkerSizing.IconSizeCanvasUnits,
                LighthouseMarkerSizing.IconSizeCanvasUnits);
            iconRect.anchoredPosition = Vector2.zero;

            // Invisible hitbox on top of the icon: a generous pointer target
            // for hover/click, while the icon itself stays non-interactive.
            var hitGo = new GameObject("Hitbox", typeof(RectTransform));
            hitGo.transform.SetParent(markerRoot.transform, false);
            var hitImage = hitGo.AddComponent<Image>();
            hitImage.color = new Color(0f, 0f, 0f, 0f);
            hitImage.raycastTarget = true;
            var hitRect = hitGo.GetComponent<RectTransform>();
            hitRect.sizeDelta = new Vector2(HitboxSize, HitboxSize);
            hitRect.anchoredPosition = Vector2.zero;

            tooltip = new GameObject("Tooltip", typeof(RectTransform));
            tooltip.transform.SetParent(markerRoot.transform, false);
            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0f, TooltipOffsetY);

            // Tooltip layout mirrors the game's marker label (MapMarkerBase):
            // a background Image plus a TextMeshProUGUI label, with the
            // background sized from the label's preferred values.
            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(tooltip.transform, false);
            tooltipBackground = bgGo.AddComponent<Image>();
            tooltipBackground.color = new Color(0.02f, 0.03f, 0.04f, 0.85f);
            tooltipBackground.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(tooltip.transform, false);
            tooltipText = labelGo.AddComponent<TextMeshProUGUI>();
            tooltipText.font = GetTooltipFont();
            tooltipText.fontSize = TooltipFontSize;
            tooltipText.color = new Color(1f, 0.95f, 0.85f, 1f);
            tooltipText.alignment = TextAlignmentOptions.Center;
            tooltipText.enableWordWrapping = false;
            tooltipText.raycastTarget = false;
            RefreshTooltipText();
            tooltip.SetActive(false);

            var pointer = markerRoot.AddComponent<MarkerPointerEvents>();
            pointer.OnHoverEnter = OnMarkerHoverEnter;
            pointer.OnHoverExit = OnMarkerHoverExit;
            pointer.OnClick = OnMarkerClick;

            built = true;
            Debug.Log(LogPrefix + " own lighthouse map marker built (sprite='" +
                (iconSprite != null ? iconSprite.name : "null") + "')" +
                " parent='" + markerRoot.transform.parent.name + "'" +
                " managerLossy=" + markersManager.transform.lossyScale.ToString("F4"));
        }

        private Sprite LoadIconSprite()
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

        private void ResolveProfileInfo()
        {
            foreach (LighthouseProfile profile in LighthouseProfiles.Active())
            {
                lighthouseName = profile.Name;
                lighthouseCharacteristic = profile.Characteristic;
                return;
            }
        }

        private void RefreshTooltipText()
        {
            if (tooltipText == null)
            {
                return;
            }
            tooltipText.text = pinned && !string.IsNullOrEmpty(lighthouseCharacteristic)
                ? lighthouseName + " · " + lighthouseCharacteristic
                : lighthouseName;
            tooltipText.ForceMeshUpdate();
            Vector2 preferred = tooltipText.GetPreferredValues();
            tooltipText.rectTransform.sizeDelta = preferred;
            if (tooltipBackground != null)
            {
                tooltipBackground.rectTransform.sizeDelta =
                    new Vector2(preferred.x + TooltipPaddingX, preferred.y + TooltipPaddingY);
            }
        }

        private void OnMarkerHoverEnter()
        {
            RefreshTooltipText();
            tooltip.SetActive(true);
        }

        private void OnMarkerHoverExit()
        {
            if (!pinned)
            {
                tooltip.SetActive(false);
            }
        }

        private void OnMarkerClick()
        {
            pinned = !pinned;
            RefreshTooltipText();
            tooltip.SetActive(true);
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

        private static TMP_FontAsset GetTooltipFont()
        {
            if (tooltipFont != null)
            {
                return tooltipFont;
            }
            // Prefer the game's default UI font so the tooltip matches the
            // rest of the map UI; fall back to a dynamic Arial asset.
            TMP_FontAsset defaults = TMP_Settings.defaultFontAsset;
            if (defaults != null)
            {
                tooltipFont = defaults;
                return tooltipFont;
            }
            Font arial = Font.CreateDynamicFontFromOSFont("Arial", TooltipFontSize);
            tooltipFont = TMP_FontAsset.CreateFontAsset(arial);
            return tooltipFont;
        }

        /// <summary>
        /// Soft white circle texture for the icon backdrop (radial falloff,
        /// like the lighthouse dot texture). Generated once, shared by all
        /// markers.
        /// </summary>
        private static Sprite GetBackdropSprite()
        {
            if (backdropSprite != null)
            {
                return backdropSprite;
            }
            var texture = new Texture2D(BackdropTextureSize, BackdropTextureSize, TextureFormat.RGBA32, false);
            texture.name = "RadioNavLighthouseBackdrop";
            texture.hideFlags = HideFlags.DontSave;
            float half = BackdropTextureSize * 0.5f;
            for (int y = 0; y < BackdropTextureSize; y++)
            {
                for (int x = 0; x < BackdropTextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = r >= 1f ? 0f : (1f - r) * (1f - r);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff));
                }
            }
            texture.Apply();
            backdropSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            return backdropSprite;
        }

        private void OnDestroy()
        {
            if (markerRoot != null)
            {
                Destroy(markerRoot);
            }
            DestroyStaleGameMarkers();
        }

        private sealed class MarkerPointerEvents : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Action OnHoverEnter;
            public Action OnHoverExit;
            public Action OnClick;

            public void OnPointerEnter(PointerEventData eventData) { OnHoverEnter?.Invoke(); }
            public void OnPointerExit(PointerEventData eventData) { OnHoverExit?.Invoke(); }
            public void OnPointerClick(PointerEventData eventData) { OnClick?.Invoke(); }
        }
    }
}
