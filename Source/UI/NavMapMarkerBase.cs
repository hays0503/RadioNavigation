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
    /// Shared skeleton for the mod's own map markers (ADR-027 pattern):
    /// world-space canvas under EntityMarkersManager — backdrop + icon +
    /// invisible hitbox + hover/click tooltip, per-frame zoom-dependent
    /// sizing via LighthouseMarkerSizing, visibility gated by
    /// NavMarkerGate.IsMapOpen.
    ///
    /// Subclasses provide the marker set and content:
    /// - MarkerCount / MarkerRootName / GetEncounterPosition / HasContent —
    ///   what to draw and where (encounter-space km; the base adds the
    ///   map-surface lift and converts to scene space every frame, because
    ///   the sandbox origin shifts between sessions);
    /// - HoverText / PinnedText — tooltip content (hover vs pinned);
    /// - LoadIconSprite — the icon source;
    /// - OnBeforeBuildViews — resolve names/profiles before views are built;
    /// - OnMarkerPositioned — per-frame diagnostics hook;
    /// - MaintenanceIntervalSeconds / OnMaintenance — periodic upkeep
    ///   (lighthouse: stale game-marker cleanup), runs even when hidden.
    /// </summary>
    public abstract class NavMapMarkerBase : MonoBehaviour
    {
        protected const string LogPrefix = "[RadioNav][Map]";
        protected const float HitboxSize = 96f;
        protected const float BackdropSize = 80f;
        protected const int BackdropTextureSize = 64;
        protected const float SpriteLiftY = 100f;
        protected const float TooltipOffsetY = 40f;
        protected const float TooltipPaddingX = 20f;
        protected const float TooltipPaddingY = 4f;
        protected const int TooltipFontSize = 30;

        [Inject] protected static ResourceManager resourceManager = null;
        [Inject] protected static Sandbox sandbox = null;

        protected sealed class MarkerView
        {
            public int Index;
            public GameObject Root;
            public GameObject Tooltip;
            public TextMeshProUGUI Text;
            public Image Background;
            public bool Pinned;
        }

        private static TMP_FontAsset tooltipFont;
        private static Sprite backdropSprite;

        protected EntityMarkersManager markersManager;
        protected MainCamera mainCamera;
        protected Sprite iconSprite;
        protected bool iconWarned;

        private readonly System.Collections.Generic.List<MarkerView> views =
            new System.Collections.Generic.List<MarkerView>();
        private bool built;
        private float maintenanceTimer;

        protected abstract int MarkerCount { get; }
        protected abstract string MarkerRootName(int index);
        protected abstract string HoverText(int index);
        protected abstract string PinnedText(int index);
        protected abstract bool HasContent(int index);
        protected abstract Vector2 GetEncounterPosition(int index);
        protected abstract Sprite LoadIconSprite();

        protected virtual void OnBeforeBuildViews() { }
        protected virtual void OnHoverEnter(int index) { }
        protected virtual void OnHoverExit(int index) { }
        protected virtual void OnClick(int index) { }
        protected virtual void OnMarkerPositioned(int index, Vector3 worldPosition, float scale, float iconPx) { }
        protected virtual float MaintenanceIntervalSeconds => 0f;
        protected virtual void OnMaintenance() { }

        private void Update()
        {
            if (!EnsureDependencies())
                return;

            if (!EnsureBuilt())
                return;

            RunMaintenance();

            MapCameraController mapController = mainCamera.MapController;
            bool mapOpen = NavMarkerGate.IsMapOpen(
                mainCamera.isActiveAndEnabled,
                mainCamera.CurrentMode,
                mapController != null,
                mapController != null ? mapController.Mode : default(MapCameraController.MapDisplayMode));
            if (!mapOpen || sandbox == null)
            {
                SetAllActive(false);
                return;
            }

            float elevation = mapController.CameraElevation;
            if (elevation <= 0f)
            {
                SetAllActive(false);
                return;
            }

            Camera mapCam = mainCamera.Camera;
            float fov = mapCam != null ? mapCam.fieldOfView : LighthouseMarkerSizing.DefaultFovDegrees;
            double unitsPerPixel = LighthouseMarkerSizing.UnitsPerPixel(elevation, fov, Screen.height);
            float iconPx = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);
            float parentLossy = markersManager.transform.lossyScale.x;
            float scale = (float)LighthouseMarkerSizing.MarkerScale(iconPx, unitsPerPixel, parentLossy);

            UpdateViewPositions(elevation, scale, iconPx);
        }

        private bool EnsureDependencies()
        {
            if (markersManager == null)
            {
                markersManager = FindObjectOfType<EntityMarkersManager>();
                if (markersManager == null)
                    return false;
            }

            if (mainCamera == null)
            {
                mainCamera = FindObjectOfType<MainCamera>();
                if (mainCamera == null)
                    return false;
            }
            return true;
        }

        private bool EnsureBuilt()
        {
            if (built)
                return true;
            BuildAll();
            return built;
        }

        private void RunMaintenance()
        {
            float maintenanceInterval = MaintenanceIntervalSeconds;
            if (maintenanceInterval <= 0f)
                return;
            maintenanceTimer += Time.unscaledDeltaTime;
            if (maintenanceTimer >= maintenanceInterval)
            {
                maintenanceTimer = 0f;
                OnMaintenance();
            }
        }

        private void UpdateViewPositions(float elevation, float scale, float iconPx)
        {
            for (int i = 0; i < views.Count; i++)
            {
                MarkerView view = views[i];
                if (!HasContent(view.Index))
                {
                    view.Root.SetActive(false);
                    continue;
                }

                view.Root.SetActive(true);
                Vector3 position = sandbox.EncounterToSceneSpace(GetEncounterPosition(view.Index));
                // Same lift formula as MapPointUI.UpdatePoints plus the
                // marker lift: the flat canvas plane at y=0 clips the icon.
                position.y = 1.5f + elevation * 0.0005f + SpriteLiftY;
                view.Root.transform.position = position;
                view.Root.transform.localScale = new Vector3(scale, scale, scale);
                // The icon lies flat on the map plane (horizontal), not
                // billboarded to the camera — Euler(90,0,0) turns the canvas
                // +Z (default forward) to face straight up.
                view.Root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                OnMarkerPositioned(view.Index, position, scale, iconPx);
            }
        }

        private void BuildAll()
        {
            if (iconSprite == null)
            {
                iconSprite = LoadIconSprite();
                if (iconSprite == null)
                    return;
            }

            OnBeforeBuildViews();

            int count = MarkerCount;
            if (count <= 0)
            {
                built = true;
                return;
            }

            for (int i = 0; i < count; i++)
                views.Add(BuildMarkerView(i));

            built = true;
            Debug.Log(LogPrefix + " markers built=" + views.Count +
                " icon='" + (iconSprite != null ? iconSprite.name : "null") + "'" +
                " parent='" + markersManager.transform.parent.name + "/" + markersManager.transform.name + "'");
        }

        private MarkerView BuildMarkerView(int index)
        {
            var view = new MarkerView { Index = index };

            view.Root = new GameObject(MarkerRootName(index), typeof(RectTransform));
            view.Root.transform.SetParent(markersManager.transform, false);

            Canvas canvas = view.Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            view.Root.AddComponent<GraphicRaycaster>();

            var rootRect = view.Root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(HitboxSize, HitboxSize);

            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(view.Root.transform, false);
            var backdropImage = backdropGo.AddComponent<Image>();
            backdropImage.sprite = GetBackdropSprite();
            backdropImage.color = new Color(0.95f, 0.92f, 0.85f, 0.9f);
            backdropImage.raycastTarget = false;
            var backdropRect = backdropGo.GetComponent<RectTransform>();
            backdropRect.sizeDelta = new Vector2(BackdropSize, BackdropSize);
            backdropRect.anchoredPosition = Vector2.zero;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(view.Root.transform, false);
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.sprite = iconSprite;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(
                LighthouseMarkerSizing.IconSizeCanvasUnits,
                LighthouseMarkerSizing.IconSizeCanvasUnits);
            iconRect.anchoredPosition = Vector2.zero;

            var hitGo = new GameObject("Hitbox", typeof(RectTransform));
            hitGo.transform.SetParent(view.Root.transform, false);
            var hitImage = hitGo.AddComponent<Image>();
            hitImage.color = new Color(0f, 0f, 0f, 0f);
            hitImage.raycastTarget = true;
            var hitRect = hitGo.GetComponent<RectTransform>();
            hitRect.sizeDelta = new Vector2(HitboxSize, HitboxSize);
            hitRect.anchoredPosition = Vector2.zero;

            view.Tooltip = new GameObject("Tooltip", typeof(RectTransform));
            view.Tooltip.transform.SetParent(view.Root.transform, false);
            var tooltipRect = view.Tooltip.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0f, TooltipOffsetY);

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(view.Tooltip.transform, false);
            view.Background = bgGo.AddComponent<Image>();
            view.Background.color = new Color(0.02f, 0.03f, 0.04f, 0.85f);
            view.Background.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(view.Tooltip.transform, false);
            view.Text = labelGo.AddComponent<TextMeshProUGUI>();
            view.Text.font = GetTooltipFont();
            view.Text.fontSize = TooltipFontSize;
            view.Text.color = new Color(1f, 0.95f, 0.85f, 1f);
            view.Text.alignment = TextAlignmentOptions.Center;
            view.Text.enableWordWrapping = false;
            view.Text.raycastTarget = false;
            RefreshTooltipText(view);
            view.Tooltip.SetActive(false);

            MarkerView captured = view;
            var pointer = view.Root.AddComponent<MarkerPointerEvents>();
            pointer.OnHoverEnter = () => OnHoverEnterInternal(captured);
            pointer.OnHoverExit = () => OnHoverExitInternal(captured);
            pointer.OnClick = () => OnClickInternal(captured);

            return view;
        }

        private void RefreshTooltipText(MarkerView view)
        {
            if (view.Text == null)
                return;

            view.Text.text = view.Pinned ? PinnedText(view.Index) : HoverText(view.Index);
            view.Text.ForceMeshUpdate();
            Vector2 preferred = view.Text.GetPreferredValues();
            view.Text.rectTransform.sizeDelta = preferred;
            if (view.Background != null)
            {
                view.Background.rectTransform.sizeDelta =
                    new Vector2(preferred.x + TooltipPaddingX, preferred.y + TooltipPaddingY);
            }
        }

        private void OnHoverEnterInternal(MarkerView view)
        {
            RefreshTooltipText(view);
            view.Tooltip.SetActive(true);
            OnHoverEnter(view.Index);
        }

        private void OnHoverExitInternal(MarkerView view)
        {
            if (!view.Pinned)
                view.Tooltip.SetActive(false);
            OnHoverExit(view.Index);
        }

        private void OnClickInternal(MarkerView view)
        {
            view.Pinned = !view.Pinned;
            RefreshTooltipText(view);
            view.Tooltip.SetActive(true);
            OnClick(view.Index);
        }

        private void SetAllActive(bool active)
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Root != null)
                    views[i].Root.SetActive(active);
            }
        }

        public static TMP_FontAsset GetTooltipFont()
        {
            if (tooltipFont != null)
                return tooltipFont;

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

        protected static Sprite GetBackdropSprite()
        {
            if (backdropSprite != null)
                return backdropSprite;

            var texture = new Texture2D(BackdropTextureSize, BackdropTextureSize, TextureFormat.RGBA32, false);
            texture.name = "RadioNavMarkerBackdrop";
            texture.hideFlags = HideFlags.DontSave;
            texture.SetPixels32(MarkerArtPixels.RenderBackdropPixels(BackdropTextureSize));
            texture.Apply();
            backdropSprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            return backdropSprite;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Root != null)
                    Destroy(views[i].Root);
            }
            views.Clear();
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
