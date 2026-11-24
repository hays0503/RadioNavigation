using System;
using System.Reflection;
using DWS.Common.InjectionFramework;
using TMPro;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Wind bearing in the map's wind row while the boat is on the surface.
    /// Reads the rendered TextMeshPro glyphs via GetParsedText/textInfo without
    /// mutating the game's label (GetPreferredValues(string) would overwrite
    /// ZString's char buffer). Places the bearing to the right of the rendered
    /// speed glyphs inside the visible panel container, or between icon and
    /// glyphs if space allows, hiding only itself when no unclipped gap fits.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class MapWindDirectionDriver : MonoBehaviour
    {
        private const string RootName = "RadioNavigation.MapWindDirection";
        private const string LabelName = "RadioNavWindBearing";
        private const float RefreshSeconds = 0.25f;
        private const float ResolveCooldownSeconds = 1.0f;
        private const float PanelPaddingPixels = 4f;

        private static readonly FieldInfo WindSpeedLabelField = typeof(MapUI).GetField(
            "windSpeedLabel", BindingFlags.Instance | BindingFlags.NonPublic);

        private MapUI mapUi;
        private TextMeshProUGUI reference;
        private RectTransform groupRect;
        private RectTransform container;
        private TextMeshProUGUI value;
        private Canvas rootCanvas;
        private float nextRefresh;
        private float nextResolve;
        private bool failureLogged;
        private string lastParsedText;
        private string lastSuffix;
        private Vector2 lastRefSize;
        private Vector2 lastContainerSize;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastScaleFactor;
        private readonly Vector3[] cornerBuffer = new Vector3[4];

        public static void EnsureRunning()
        {
            GameObject root = GameObject.Find(RootName);
            if (root != null)
            {
                MonoBehaviour[] existing = root.GetComponents<MonoBehaviour>();
                bool hasForeign = false;
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i].IsNotNull() && existing[i].GetType() == typeof(MapWindDirectionDriver))
                        return;
                    if (existing[i].IsNotNull() && existing[i].GetType().Name == nameof(MapWindDirectionDriver))
                        hasForeign = true;
                }
                if (hasForeign)
                {
                    Destroy(root);
                    root = null;
                }
            }
            if (root == null)
            {
                root = new GameObject(RootName);
                DontDestroyOnLoad(root);
            }
            root.AddComponent<MapWindDirectionDriver>();
        }

        public void LateUpdate()
        {
            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                if (failureLogged)
                    return;
                failureLogged = true;
                Debug.LogWarning("[RadioNav][WIND] map indicator unavailable: " + ex.Message);
                enabled = false;
            }
        }

        private void Refresh()
        {
            InjectionFramework framework = InjectionFramework.Instance;
            if (framework == null || !Resolve() || !IsRowActive())
            {
                SetVisible(false);
                return;
            }

            if (!TryGetWindState(framework, out string suffix))
            {
                SetVisible(false);
                return;
            }

            // Wait for Canvas pre-render to populate textInfo after ZString SetCharArray
            // updates so glyph bounds and parsed text match what is actually drawn.
            if (reference.havePropertiesChanged)
            {
                nextRefresh = 0f;
                return;
            }
            if (reference.textInfo == null || reference.textInfo.characterCount == 0)
            {
                SetVisible(false);
                return;
            }

            string parsed = ReadRenderedText();
            if (string.IsNullOrEmpty(parsed))
            {
                SetVisible(false);
                return;
            }

            EnsureLabel();
            if (!string.Equals(value.text, suffix, StringComparison.Ordinal))
                value.text = suffix;

            if (NeedsLayout(parsed, suffix) || !value.gameObject.activeSelf)
                ApplyLayout(parsed, suffix);
        }

        private bool IsRowActive()
        {
            var camera = MainCamera.Instance;
            return camera != null && camera.CurrentMode == CameraMode.Map
                && mapUi.isActiveAndEnabled && reference.gameObject.activeInHierarchy;
        }

        private bool TryGetWindState(InjectionFramework framework, out string suffix)
        {
            suffix = null;
            var ship = framework.GetInstance<PlayerShip>();
            if (!CurrentRuntimeAdapter.TrySampleWind(out Vector3 wind, out _))
                wind = Vector3.zero;
            if (ship == null || !WindCompass.IsSurfaced(ship.Depth) || WindCompass.IsCalm(wind.x, wind.z))
                return false;

            string parsed = ReadRenderedText();
            suffix = WindCompass.Suffix(wind.x, wind.z, IsRussianLocale(framework, parsed));
            return !string.IsNullOrEmpty(suffix);
        }

        private bool Resolve()
        {
            if (mapUi != null && reference != null && groupRect != null && container != null)
                return true;

            ClearBinding();
            var camera = MainCamera.Instance;
            if (camera == null || camera.CurrentMode != CameraMode.Map || Time.unscaledTime < nextResolve)
                return false;
            nextResolve = Time.unscaledTime + ResolveCooldownSeconds;

            foreach (MapUI candidate in Resources.FindObjectsOfTypeAll<MapUI>())
            {
                if (TryBindCandidate(candidate))
                    return true;
            }
            return false;
        }

        private bool TryBindCandidate(MapUI candidate)
        {
            if (candidate == null)
                return false;
            var go = candidate.gameObject;
            if (!go.scene.IsValid() || !go.activeInHierarchy)
                return false;
            var label = WindSpeedLabelField?.GetValue(candidate) as TextMeshProUGUI;
            var iconRect = label != null ? label.transform.parent as RectTransform : null;
            if (iconRect == null)
                return false;

            mapUi = candidate;
            reference = label;
            groupRect = iconRect;
            container = (iconRect.parent as RectTransform) ?? iconRect;
            rootCanvas = container.GetComponentInParent<Canvas>();
            if (rootCanvas != null)
                rootCanvas = rootCanvas.rootCanvas;
            return true;
        }

        private void ClearBinding()
        {
            mapUi = null;
            reference = null;
            groupRect = null;
            container = null;
            rootCanvas = null;
            lastParsedText = null;
            lastSuffix = null;
        }

        private void EnsureLabel()
        {
            if (value != null && value.transform.parent == container)
                return;
            RemoveDuplicates(container);
            RemoveDuplicates(groupRect);
            var label = new GameObject(LabelName, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var rect = label.rectTransform;
            rect.SetParent(container, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160f, reference.rectTransform.rect.height);
            label.font = reference.font;
            label.fontSize = reference.fontSize * 0.8f;
            label.color = reference.color;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            value = label;
            lastParsedText = null;
            lastSuffix = null;
        }

        private static void RemoveDuplicates(Transform parent)
        {
            if (parent == null)
                return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child == null)
                    continue;
                if (child.name == LabelName)
                    Destroy(child.gameObject);
            }
        }

        private bool NeedsLayout(string parsed, string suffix)
        {
            float scale = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
            Vector2 refSize = reference.rectTransform.rect.size;
            Vector2 containerSize = container.rect.size;
            return !string.Equals(lastParsedText, parsed, StringComparison.Ordinal)
                || !string.Equals(lastSuffix, suffix, StringComparison.Ordinal)
                || lastRefSize != refSize
                || lastContainerSize != containerSize
                || lastScreenWidth != Screen.width
                || lastScreenHeight != Screen.height
                || !Mathf.Approximately(lastScaleFactor, scale);
        }

        private void ApplyLayout(string parsed, string suffix)
        {
            lastParsedText = parsed;
            lastSuffix = suffix;
            lastRefSize = reference.rectTransform.rect.size;
            lastContainerSize = container.rect.size;
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastScaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;

            if (!TryGetGlyphBounds(out float glyphMinX, out float glyphMaxX, out float centerY))
            {
                SetVisible(false);
                return;
            }
            GetRectXBounds(groupRect, out float iconMinX, out float iconMaxX);
            GetVisibleXBounds(out float visibleMinX, out float visibleMaxX);
            float labelWidth = value.GetPreferredValues(suffix).x;

            WindLabelPlacement placement = WindLabelLayout.TryPlace(
                glyphMinX, glyphMaxX, iconMinX, iconMaxX,
                visibleMinX, visibleMaxX, labelWidth, WindLabelLayout.DefaultGap);
            if (!placement.Visible)
            {
                SetVisible(false);
                return;
            }

            var rect = value.rectTransform;
            rect.pivot = new Vector2(placement.AlignRight ? 1f : 0f, 0.5f);
            rect.sizeDelta = new Vector2(Mathf.Ceil(labelWidth) + 4f, Mathf.Max(16f, reference.rectTransform.rect.height));
            rect.localPosition = new Vector3(placement.AnchorX, centerY, 0f);
            value.alignment = placement.AlignRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            SetVisible(true);
        }

        private bool TryGetGlyphBounds(out float minX, out float maxX, out float centerY)
        {
            TMP_TextInfo info = reference.textInfo;
            int count = info == null ? 0 : Math.Min(info.characterCount, info.characterInfo.Length);
            float localMin = float.MaxValue;
            float localMax = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                TMP_CharacterInfo ch = info.characterInfo[i];
                if (!ch.isVisible)
                    continue;
                if (ch.bottomLeft.x < localMin)
                    localMin = ch.bottomLeft.x;
                if (ch.topRight.x > localMax)
                    localMax = ch.topRight.x;
            }
            if (localMin > localMax)
            {
                minX = maxX = centerY = 0f;
                return false;
            }

            float localCenterY = reference.rectTransform.rect.center.y;
            minX = container.InverseTransformPoint(
                reference.rectTransform.TransformPoint(new Vector3(localMin, localCenterY, 0f))).x;
            Vector3 rightContainer = container.InverseTransformPoint(
                reference.rectTransform.TransformPoint(new Vector3(localMax, localCenterY, 0f)));
            maxX = rightContainer.x;
            centerY = rightContainer.y;
            if (minX > maxX)
            {
                float swap = minX;
                minX = maxX;
                maxX = swap;
            }
            return true;
        }

        private void GetRectXBounds(RectTransform target, out float minX, out float maxX)
        {
            target.GetWorldCorners(cornerBuffer);
            minX = float.MaxValue;
            maxX = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = container.InverseTransformPoint(cornerBuffer[i]).x;
                if (x < minX)
                    minX = x;
                if (x > maxX)
                    maxX = x;
            }
        }

        private void GetVisibleXBounds(out float minX, out float maxX)
        {
            Rect r = container.rect;
            minX = r.xMin + PanelPaddingPixels;
            maxX = r.xMax - PanelPaddingPixels;

            Transform current = container;
            while (current != null)
            {
                if (HasClipper(current) && current is RectTransform clipRect)
                {
                    GetRectXBounds(clipRect, out float clipMin, out float clipMax);
                    if (clipMin > minX)
                        minX = clipMin;
                    if (clipMax < maxX)
                        maxX = clipMax;
                }
                if (rootCanvas != null && current == rootCanvas.transform)
                    break;
                current = current.parent;
            }

            var canvasRect = rootCanvas == null ? null : rootCanvas.transform as RectTransform;
            if (canvasRect != null)
            {
                GetRectXBounds(canvasRect, out float canvasMin, out float canvasMax);
                if (canvasMin + PanelPaddingPixels > minX)
                    minX = canvasMin + PanelPaddingPixels;
                if (canvasMax - PanelPaddingPixels < maxX)
                    maxX = canvasMax - PanelPaddingPixels;
            }
        }

        private static bool HasClipper(Transform node)
        {
            if (node.GetComponent<RectMask2D>() != null)
                return true;
            var mask = node.GetComponent<Mask>();
            if (mask == null)
                return false;
            return mask.enabled;
        }

        private string ReadRenderedText()
        {
            string parsed = reference.GetParsedText();
            return string.IsNullOrEmpty(parsed) ? (reference.text ?? string.Empty) : parsed;
        }

        private static bool IsRussianLocale(InjectionFramework framework, string parsed)
        {
            var locale = framework.GetInstance<Locale>();
            if (locale != null && !string.IsNullOrEmpty(locale.Language))
                return string.Equals(locale.Language, "russian", StringComparison.OrdinalIgnoreCase);
            return WindCompass.PrefersRussian(parsed);
        }

        public void OnDestroy()
        {
            if (value != null)
                Destroy(value.gameObject);
            value = null;
            ClearBinding();
        }

        private void SetVisible(bool visible)
        {
            if (value != null && value.gameObject.activeSelf != visible)
                value.gameObject.SetActive(visible);
        }
    }
}
