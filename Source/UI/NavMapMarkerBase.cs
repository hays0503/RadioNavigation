using System;
using System.Collections.Generic;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using TMPro;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Shared skeleton for the mod's nav-map markers (lighthouse, station,
    /// sextant fix). Since 2026-11-05 every marker IS a native game map point
    /// (<see cref="MapPointUI"/>, created through the public
    /// <see cref="MapChartingUI.CreatePoint"/>) whose own look is neutralised
    /// and whose visuals are the mod's children (see
    /// <see cref="MapMarkerPointBridge"/>). The markers therefore get the
    /// native behaviour for free — drag, eraser/Delete, snapping by the
    /// charting tools (Lines/Circles/Angles/Bearing), selection — while the
    /// look stays exactly as it always was.
    ///
    /// Lifecycle: a periodic reconciliation (0.25 s) syncs the desired marker
    /// set (subclass state) with the owned points in the scene — adopting the
    /// points restored from the save by their "RadioNav …" name key (never
    /// duplicating them), creating the missing ones and destroying the stale
    /// ones. The bridge compensates the game's per-frame root position/scale
    /// writes in LateUpdate; the static policy (lighthouse/station) restores
    /// the true position and keeps the eraser away.
    ///
    /// Subclasses provide the content (abstract members); the base owns the
    /// points, the declutter, the tooltip/pin and the interaction policy.
    /// </summary>
    public abstract class NavMapMarkerBase : MonoBehaviour
    {
        protected const string LogPrefix = "[RadioNav][Map]";
        // 76 = 1.19× the 64-unit icon: small enough that two hitboxes can
        // never overlap under the 1.3× declutter gap.
        internal const float HitboxSizeUnits = 76f;
        internal const float BackdropSizeUnits = 80f;
        internal const int BackdropTextureSize = 64;
        internal const float SpriteLiftYUnits = 100f;
        internal const float TooltipOffsetYUnits = 40f;
        internal const float TooltipPaddingXUnits = 20f;
        internal const float TooltipPaddingYUnits = 4f;
        internal const int TooltipFontSizeUnits = 30;

        /// <summary>How often the desired set is reconciled with the scene.
        /// The fix marker appears within this delay after a new result.</summary>
        private const float ReconcileIntervalSeconds = 0.25f;

        [Inject] protected static ResourceManager resourceManager = null;
        [Inject] protected static Sandbox sandbox = null;

        private sealed class MarkerView
        {
            public int Index;
            public string Key;
            public MapPointUI Point;
            public MapMarkerPointBridge Bridge;
            public bool Pinned;
            public bool Visible = true;
            public bool Interactive = true;
            public bool PositionLocked;
            public Vector2 TruePosition;
        }

        private static TMP_FontAsset tooltipFont;
        private static Sprite backdropSprite;
        private static Sprite fallbackIconSprite;

        protected MainCamera mainCamera;
        protected Sprite iconSprite;
        protected bool iconWarned;

        private readonly List<MarkerView> views = new List<MarkerView>();
        private readonly List<MarkerDeclutter.Item> declutterItems = new List<MarkerDeclutter.Item>();
        private readonly List<string> desiredKeys = new List<string>();
        private readonly List<string> existingNames = new List<string>();
        private readonly List<string> toCreate = new List<string>();
        private readonly List<string> toAdopt = new List<string>();
        private readonly List<string> toDestroy = new List<string>();
        private readonly Dictionary<string, MapPointUI> pointsByName = new Dictionary<string, MapPointUI>();
        private readonly Dictionary<string, int> indexByKey = new Dictionary<string, int>();
        private bool[] shownBuffer = new bool[0];
        private MapChartingUI charting;
        private bool prepared;
        private float reconcileTimer;
        private float declutterLogTimer;
        private float maintenanceTimer;
        private int hoveredIndex = -1;
        private float lastIconPx = LighthouseMarkerSizing.MinIconPixelSize;
        private double lastUnitsPerPixel;

        protected abstract int MarkerCount { get; }
        /// <summary>Adoption key of the marker (see MapMarkerPoints).</summary>
        protected abstract string PointKey(int index);
        /// <summary>Kind driving the static/dynamic interaction policy.</summary>
        protected abstract MapMarkerKind PointKind(int index);
        protected abstract string HoverText(int index);
        protected abstract string PinnedText(int index);
        protected abstract bool HasContent(int index);
        protected abstract Vector2 GetEncounterPosition(int index);
        protected abstract Sprite LoadIconSprite();

        /// <summary>True when the marker is temporarily suppressed (the fix
        /// marker after its point was erased — until the next result).</summary>
        protected virtual bool PointSuppressed(int index)
        {
            return false;
        }

        /// <summary>Called when the player destroys the marker's point (the
        /// map eraser or the Delete key). The fix marker flips its
        /// suppression here; the static markers are simply recreated by the
        /// next reconciliation pass.</summary>
        protected virtual void OnPointDestroyed(int index)
        {
        }

        /// <summary>One-time content preparation before the first reconcile
        /// (the lighthouse fleet snapshot).</summary>
        protected virtual void PrepareContent()
        {
        }

        /// <summary>Declutter hooks (ADR-032). Default: off — markers draw
        /// every frame exactly as before. Overriding DeclutterEnabled turns
        /// on greedy min-gap suppression; Priority ranks who survives an
        /// overlap (higher wins; the lighthouse marker uses fire height).</summary>
        protected virtual bool DeclutterEnabled => false;
        protected virtual float Priority(int index) => 0f;

        protected virtual void OnHoverEnter(int index) { }
        protected virtual void OnHoverExit(int index) { }
        protected virtual void OnClick(int index) { }
        protected virtual void OnMarkerPositioned(int index, Vector3 worldPosition,
            float scale, float iconPx) { }
        protected virtual float MaintenanceIntervalSeconds => 0f;
        protected virtual void OnMaintenance() { }

        private void Update()
        {
            if (!EnsureDependencies())
                return;

            MapCameraController mapController = mainCamera.MapController;
            bool mapOpen = NavMarkerGate.IsMapOpen(
                mainCamera.isActiveAndEnabled,
                mainCamera.CurrentMode,
                mapController != null,
                mapController != null ? mapController.Mode : default(MapCameraController.MapDisplayMode));
            if (!mapOpen || sandbox == null)
                return;

            float elevation = mapController.CameraElevation;
            if (elevation <= 0f)
                return;

            if (!prepared)
            {
                prepared = true;
                PrepareContent();
                iconSprite = LoadIconSprite();
            }

            Camera mapCam = mainCamera.Camera;
            float fov = mapCam != null ? mapCam.fieldOfView : LighthouseMarkerSizing.DefaultFovDegrees;
            lastUnitsPerPixel = LighthouseMarkerSizing.UnitsPerPixel(elevation, fov, Screen.height);
            lastIconPx = LighthouseMarkerSizing.IconPixelSizeForElevation(elevation);

            RunMaintenance();
            RunReconcile();
            UpdateViews();
        }

        private bool EnsureDependencies()
        {
            if (mainCamera == null)
            {
                mainCamera = FindObjectOfType<MainCamera>();
                if (mainCamera == null)
                    return false;
            }
            return true;
        }

        private void RunMaintenance()
        {
            float interval = MaintenanceIntervalSeconds;
            if (interval <= 0f)
                return;
            maintenanceTimer += Time.unscaledDeltaTime;
            if (maintenanceTimer >= interval)
            {
                maintenanceTimer = 0f;
                OnMaintenance();
            }
        }

        // ---- native points ---------------------------------------------------

        /// <summary>
        /// Syncs the desired marker set with the owned points present in the
        /// scene: adopt (never duplicate) the ones restored from the save,
        /// create the missing, destroy the stale. Throttled — cheap per frame.
        /// </summary>
        private void RunReconcile()
        {
            reconcileTimer -= Time.unscaledDeltaTime;
            if (reconcileTimer > 0f)
                return;
            reconcileTimer = ReconcileIntervalSeconds;

            if (charting == null)
                charting = FindObjectOfType<MapChartingUI>();
            if (charting == null)
                return;

            CollectDesiredKeys();
            CollectExistingPoints();
            MapMarkerPoints.Plan(desiredKeys, existingNames, toCreate, toAdopt, toDestroy);
            ApplyPlan();
            RebuildViews();
        }

        private void CollectDesiredKeys()
        {
            desiredKeys.Clear();
            indexByKey.Clear();
            for (int i = 0; i < MarkerCount; i++)
            {
                if (!HasContent(i) || PointSuppressed(i))
                    continue;
                string key = PointKey(i);
                if (string.IsNullOrEmpty(key) || indexByKey.ContainsKey(key))
                    continue;
                indexByKey.Add(key, i);
                desiredKeys.Add(key);
            }
        }

        private void CollectExistingPoints()
        {
            existingNames.Clear();
            pointsByName.Clear();
            // includeInactive: the declutter deactivates hidden points, and a
            // default FindObjectsOfType would not see them — the reconcile
            // would then treat them as missing and duplicate the fleet.
            MapPointUI[] all = FindObjectsOfType<MapPointUI>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MapPointUI point = all[i];
                if (point == null || point.Data == null)
                    continue;
                string name = point.Data.Name;
                if (!MapMarkerPoints.IsOwned(name))
                    continue;
                existingNames.Add(name);
                if (!pointsByName.ContainsKey(name))
                {
                    pointsByName.Add(name, point);
                }
                else
                {
                    // Duplicate of an owned name (a runaway from an earlier
                    // build): keep the first, destroy the rest.
                    point.Destroyed -= OnPointDestroyedHandler;
                    Destroy(point.gameObject);
                }
            }
        }

        private void ApplyPlan()
        {
            for (int i = 0; i < toDestroy.Count; i++)
            {
                MapPointUI stale;
                if (pointsByName.TryGetValue(toDestroy[i], out stale) && stale != null)
                {
                    stale.Destroyed -= OnPointDestroyedHandler;
                    Destroy(stale.gameObject);
                }
            }

            for (int i = 0; i < toCreate.Count; i++)
            {
                int index;
                if (!indexByKey.TryGetValue(toCreate[i], out index))
                    continue;
                MapPointUI created = charting.CreatePoint(GetEncounterPosition(index), toCreate[i]);
                if (created != null && !pointsByName.ContainsKey(toCreate[i]))
                    pointsByName.Add(toCreate[i], created);
            }
        }

        /// <summary>Rebuilds the view records from the desired keys, keeping
        /// the per-marker session state (pinned, hover) for the keys that
        /// already had a view.</summary>
        private void RebuildViews()
        {
            var previous = new Dictionary<string, MarkerView>(StringComparer.Ordinal);
            for (int i = 0; i < views.Count; i++)
            {
                if (!string.IsNullOrEmpty(views[i].Key))
                    previous[views[i].Key] = views[i];
            }

            views.Clear();
            for (int i = 0; i < desiredKeys.Count; i++)
            {
                string key = desiredKeys[i];
                int index;
                MapPointUI point;
                if (!indexByKey.TryGetValue(key, out index)
                    || !pointsByName.TryGetValue(key, out point)
                    || point == null)
                {
                    continue;
                }

                MarkerView view;
                if (!previous.TryGetValue(key, out view))
                    view = new MarkerView();
                view.Index = index;
                view.Key = key;
                view.Point = point;
                view.TruePosition = GetEncounterPosition(index);
                view.Bridge = MapMarkerPointBridge.Attach(point, this, index, iconSprite);
                if (view.Bridge != null)
                    view.Bridge.ViewSlot = views.Count;
                point.Destroyed -= OnPointDestroyedHandler;
                point.Destroyed += OnPointDestroyedHandler;
                views.Add(view);
            }

            if (hoveredIndex >= 0)
            {
                bool stillThere = false;
                for (int i = 0; i < views.Count; i++)
                {
                    if (views[i].Index == hoveredIndex)
                    {
                        stillThere = true;
                        break;
                    }
                }
                if (!stillThere)
                    hoveredIndex = -1;
            }
        }

        private void OnPointDestroyedHandler(MapPointUI point)
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Point != point)
                    continue;
                int index = views[i].Index;
                views[i].Point = null;
                views[i].Bridge = null;
                OnPointDestroyed(index);
                break;
            }

            // Reconcile on the next frame: the fix marker stays suppressed,
            // the static markers are recreated (Delete-key safety net).
            reconcileTimer = 0f;
        }

        /// <summary>Called by the bridge from its LateUpdate (after the game's
        /// own point update): applies the interaction policy, the static
        /// position lock and the scale/lift compensation with live data.</summary>
        internal void ApplyBridgeFrame(MapMarkerPointBridge bridge)
        {
            if (bridge == null)
                return;
            MarkerView view = ViewFor(bridge);
            if (view == null || view.Point == null)
                return;
            bridge.ApplyFrame(lastIconPx, lastUnitsPerPixel, view.Interactive,
                view.PositionLocked, view.TruePosition, view.Key);
        }

        private MarkerView ViewFor(MapMarkerPointBridge bridge)
        {
            if (bridge == null || bridge.ViewSlot < 0 || bridge.ViewSlot >= views.Count)
                return null;
            MarkerView view = views[bridge.ViewSlot];
            return view.Bridge == bridge ? view : null;
        }

        // ---- per-frame view update ------------------------------------------

        private void UpdateViews()
        {
            bool declutter = DeclutterEnabled && lastUnitsPerPixel > 0.0;
            declutterItems.Clear();
            if (declutter && shownBuffer.Length < views.Count)
                shownBuffer = new bool[views.Count];

            bool pointsTool = charting != null && charting.CurrentTool == MapChartingUI.Tool.Points;
            bool eraser = charting != null && charting.CurrentTool == MapChartingUI.Tool.Eraser;

            for (int i = 0; i < views.Count; i++)
            {
                MarkerView view = views[i];
                if (view.Point == null)
                    continue;

                view.TruePosition = GetEncounterPosition(view.Index);
                MapMarkerPolicy policy = MapMarkerPoints.Evaluate(
                    PointKind(view.Index), PointSuppressed(view.Index), true, pointsTool, eraser);
                view.Interactive = policy.Interactive;
                view.PositionLocked = policy.PositionLocked;

                Vector3 position = sandbox.EncounterToSceneSpace(view.TruePosition);
                OnMarkerPositioned(view.Index, position,
                    (float)LighthouseMarkerSizing.MarkerScale(lastIconPx, lastUnitsPerPixel, 1f),
                    lastIconPx);

                if (!declutter)
                {
                    view.Visible = true;
                    ApplyVisibility(view);
                    continue;
                }

                declutterItems.Add(new MarkerDeclutter.Item
                {
                    ViewIndex = i,
                    X = position.x,
                    Y = position.z,
                    Priority = Priority(view.Index),
                    // Pinned/hovered never suppress: a deactivated root
                    // receives no PointerExit, so its tooltip would dangle.
                    Forced = view.Pinned || view.Index == hoveredIndex
                });
            }

            if (declutter)
                ApplyDeclutter();
        }

        /// <summary>
        /// Runs the greedy selection over the collected candidates and applies
        /// the shown/hidden state to the points. The gap is world units:
        /// screen px target × scene units per screen px.
        /// </summary>
        private void ApplyDeclutter()
        {
            double gap = MarkerDeclutter.DefaultGapFactor * lastIconPx * lastUnitsPerPixel;
            int shownCount = MarkerDeclutter.Select(declutterItems, gap, shownBuffer);
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Point == null)
                    continue;
                views[i].Visible = i < shownBuffer.Length && shownBuffer[i];
                ApplyVisibility(views[i]);
            }

            LogDeclutterStats(shownCount);
        }

        private void ApplyVisibility(MarkerView view)
        {
            if (view.Point == null)
                return;
            if (view.Point.gameObject.activeSelf == view.Visible)
                return;
            // The declutter hides the whole point (the bridge included): a
            // non-pinned tooltip must not come back dangling on re-activation.
            if (!view.Visible && !view.Pinned && view.Bridge != null)
                view.Bridge.HideTooltip();
            view.Point.gameObject.SetActive(view.Visible);
        }

        /// <summary>Throttled shown/hidden counter behind a diagnostics key.</summary>
        private void LogDeclutterStats(int shownCount)
        {
            if (!RadioNavDiagnostics.IsEnabledCached(
                    RadioNavDiagnostics.LighthouseDeclutterStatsKey, false))
            {
                return;
            }

            declutterLogTimer += Time.unscaledDeltaTime;
            if (declutterLogTimer < 2f)
                return;
            declutterLogTimer = 0f;
            Debug.Log(LogPrefix + " declutter shown=" + shownCount + "/" + views.Count);
        }

        // ---- hover / pin -----------------------------------------------------

        internal void OnBridgeHoverEnter(MapMarkerPointBridge bridge)
        {
            MarkerView view = ViewFor(bridge);
            if (view == null)
                return;
            hoveredIndex = view.Index;
            RefreshTooltip(view);
            OnHoverEnter(view.Index);
        }

        internal void OnBridgeHoverExit(MapMarkerPointBridge bridge)
        {
            MarkerView view = ViewFor(bridge);
            if (view == null)
                return;
            if (hoveredIndex == view.Index)
                hoveredIndex = -1;
            if (!view.Pinned && bridge != null)
                bridge.HideTooltip();
            OnHoverExit(view.Index);
        }

        internal void OnBridgeClick(MapMarkerPointBridge bridge)
        {
            MarkerView view = ViewFor(bridge);
            if (view == null)
                return;
            view.Pinned = !view.Pinned;
            RefreshTooltip(view);
            OnClick(view.Index);
        }

        private void RefreshTooltip(MarkerView view)
        {
            if (view.Bridge == null)
                return;
            view.Bridge.RefreshTooltip(
                view.Pinned ? PinnedText(view.Index) : HoverText(view.Index), view.Pinned);
        }

        // ---- shared art ------------------------------------------------------

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

            Font arial = Font.CreateDynamicFontFromOSFont("Arial", TooltipFontSizeUnits)
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (arial == null)
                return null;
            tooltipFont = TMP_FontAsset.CreateFontAsset(arial);
            return tooltipFont;
        }

        /// <summary>Procedural fallback icon (core + ring) for markers whose
        /// bundle prefab is missing. Cached like the backdrop sprite.</summary>
        protected static Sprite GetFallbackIconSprite()
        {
            if (fallbackIconSprite != null)
                return fallbackIconSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "RadioNavMarkerFallbackIcon";
            texture.hideFlags = HideFlags.DontSave;
            texture.SetPixels32(MarkerArtPixels.RenderIconPixels(size));
            texture.Apply();
            fallbackIconSprite = Sprite.Create(texture,
                new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return fallbackIconSprite;
        }

        internal static Sprite GetBackdropSprite()
        {
            if (backdropSprite != null)
                return backdropSprite;

            var texture = new Texture2D(BackdropTextureSize, BackdropTextureSize,
                TextureFormat.RGBA32, false);
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
                if (views[i].Point != null)
                    views[i].Point.Destroyed -= OnPointDestroyedHandler;
            }
            views.Clear();
        }
    }
}
