using TMPro;
using UBOAT.Game.UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-point visual/interaction bridge (author 2026-11-05): lives on the
    /// GameObject of a native <see cref="MapPointUI"/>, neutralises the game's
    /// own look (transparent image + disabled label) and re-hosts the mod's
    /// marker look (backdrop/icon/tooltip) as children. The point itself then
    /// provides the native behaviour — drag, eraser/Delete, tool snapping,
    /// selection — while the mod keeps its exact visuals.
    ///
    /// Everything the game owns is touched through public APIs only:
    /// <c>Image.color/sprite/rectTransform</c>, <c>MapPointUI.Interactive</c>
    /// (<c>raycastTarget</c>), <c>MapPointUI.Name/Position</c>. The game's own
    /// per-frame writes (position, localScale) are compensated here in
    /// LateUpdate, after the game's update.
    /// </summary>
    internal sealed class MapMarkerPointBridge : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public NavMapMarkerBase Owner;
        public int Index;
        /// <summary>Position in the owner's view list (O(1) owner lookup).</summary>
        public int ViewSlot;

        private MapPointUI point;
        private Image pointImage;
        private TextMeshProUGUI pointLabel;
        private RectTransform visual;
        private Image backdrop;
        private Image icon;
        private GameObject tooltip;
        private Image tooltipBackground;
        private TextMeshProUGUI tooltipText;
        private bool built;
        private bool pinned;

        public MapPointUI Point => point;
        public bool Pinned => pinned;

        /// <summary>Attaches (or reuses) a bridge on the point and neutralises
        /// the game's own visuals. Idempotent — used by the reconciliation for
        /// both freshly created and adopted points.</summary>
        public static MapMarkerPointBridge Attach(MapPointUI point,
            NavMapMarkerBase owner, int index, Sprite iconSprite)
        {
            if (point == null || owner == null)
                return null;

            MapMarkerPointBridge bridge = point.GetComponent<MapMarkerPointBridge>()
                ?? point.gameObject.AddComponent<MapMarkerPointBridge>();
            bridge.Owner = owner;
            bridge.Index = index;
            bridge.point = point;
            bridge.Neutralise();
            bridge.EnsureBuilt(iconSprite);
            return bridge;
        }

        /// <summary>Hides the game's own point look. The image keeps
        /// raycasting (alphaHitTestMinimumThreshold is 0 in the prefab, so a
        /// fully transparent colour does not disable hover). The hitbox rect
        /// is sized per frame in ApplyFrame — the game scales the root itself.</summary>
        private void Neutralise()
        {
            if (pointImage == null)
                pointImage = GetComponent<Image>();
            if (pointImage != null)
            {
                if (pointImage.color.a != 0f)
                    pointImage.color = new Color(0f, 0f, 0f, 0f);
                if (pointImage.alphaHitTestMinimumThreshold != 0f)
                    pointImage.alphaHitTestMinimumThreshold = 0f;
            }

            if (pointLabel == null)
                pointLabel = GetComponentInChildren<TextMeshProUGUI>(true);
            if (pointLabel?.enabled == true)
                pointLabel.enabled = false;
        }

        private void EnsureBuilt(Sprite iconSprite)
        {
            if (built)
                return;
            built = true;

            var visualGo = new GameObject("RadioNavVisual", typeof(RectTransform));
            visualGo.transform.SetParent(transform, false);
            visual = visualGo.GetComponent<RectTransform>();
            visual.sizeDelta = Vector2.zero;

            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(visual, false);
            backdrop = backdropGo.AddComponent<Image>();
            backdrop.sprite = NavMapMarkerBase.GetBackdropSprite();
            // ADR-032: 0.9 was tuned for 5 station markers; at lighthouse
            // density the cream backdrop washed out the map underneath.
            backdrop.color = new Color(0.95f, 0.92f, 0.85f, 0.5f);
            backdrop.raycastTarget = false;
            backdrop.rectTransform.sizeDelta = new Vector2(
                NavMapMarkerBase.BackdropSizeUnits, NavMapMarkerBase.BackdropSizeUnits);
            backdrop.rectTransform.anchoredPosition = Vector2.zero;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(visual, false);
            icon = iconGo.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.sizeDelta = new Vector2(
                LighthouseMarkerSizing.IconSizeCanvasUnits,
                LighthouseMarkerSizing.IconSizeCanvasUnits);
            icon.rectTransform.anchoredPosition = Vector2.zero;

            tooltip = new GameObject("Tooltip", typeof(RectTransform));
            tooltip.transform.SetParent(visual, false);
            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0f, NavMapMarkerBase.TooltipOffsetYUnits);

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(tooltip.transform, false);
            tooltipBackground = bgGo.AddComponent<Image>();
            tooltipBackground.color = new Color(0.02f, 0.03f, 0.04f, 0.85f);
            tooltipBackground.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(tooltip.transform, false);
            tooltipText = labelGo.AddComponent<TextMeshProUGUI>();
            tooltipText.font = NavMapMarkerBase.GetTooltipFont();
            tooltipText.fontSize = NavMapMarkerBase.TooltipFontSizeUnits;
            tooltipText.color = new Color(1f, 0.95f, 0.85f, 1f);
            tooltipText.alignment = TextAlignmentOptions.Center;
            tooltipText.enableWordWrapping = false;
            tooltipText.raycastTarget = false;
            tooltip.SetActive(false);
        }

        /// <summary>Per-frame compensation, called by the owner from this
        /// component's LateUpdate (after the game's own point update): root
        /// scale/lift compensation, the interaction policy, the adoption-key
        /// re-assertion and the static position lock.</summary>
        public void ApplyFrame(float iconPx, double unitsPerPixel, bool interactive,
            bool positionLocked, Vector2 trueEncounterPosition, string keyName)
        {
            Neutralise();

            if (point != null && point.Interactive != interactive)
                point.Interactive = interactive;

            if (!string.IsNullOrEmpty(keyName) && point != null && point.Name != keyName)
                point.Name = keyName;

            if (positionLocked && point != null && point.Position != trueEncounterPosition)
                point.Position = trueEncounterPosition;

            float rootLossy = transform.lossyScale.x;
            if (visual == null || rootLossy <= LighthouseMarkerSizing.MinParentLossyScale)
                return;

            // The hitbox rect follows the icon's pixel policy (the game scales
            // the root itself, so the rect is derived from the live scale):
            // world hitbox = Hitbox/Icon × iconPx × unitsPerPixel, exactly like
            // the old custom hitbox child was.
            if (pointImage != null)
            {
                float hitboxUnits = (float)(NavMapMarkerBase.HitboxSizeUnits * iconPx
                    * unitsPerPixel
                    / (LighthouseMarkerSizing.IconSizeCanvasUnits * rootLossy));
                var hitbox = new Vector2(hitboxUnits, hitboxUnits);
                if (pointImage.rectTransform.sizeDelta != hitbox)
                    pointImage.rectTransform.sizeDelta = hitbox;
            }

            float scale = (float)LighthouseMarkerSizing.MarkerScale(
                iconPx, unitsPerPixel, rootLossy);
            var desiredScale = new Vector3(scale, scale, scale);
            if (visual.localScale != desiredScale)
                visual.localScale = desiredScale;

            // The map point lies flat on the plane (root Euler(90,0,0)): the
            // world +Y lift is the local -Z axis. The mod's markers have always
            // floated SpriteLiftY above the game's own point plane.
            float lift = -NavMapMarkerBase.SpriteLiftYUnits / rootLossy;
            var desiredPosition = new Vector3(0f, 0f, lift);
            if (visual.localPosition != desiredPosition)
                visual.localPosition = desiredPosition;
        }

        public void RefreshTooltip(string text, bool isPinned)
        {
            if (tooltipText == null)
                return;

            tooltipText.text = text ?? string.Empty;
            tooltipText.ForceMeshUpdate();
            Vector2 preferred = tooltipText.GetPreferredValues();
            tooltipText.rectTransform.sizeDelta = preferred;
            if (tooltipBackground != null)
            {
                tooltipBackground.rectTransform.sizeDelta = new Vector2(
                    preferred.x + NavMapMarkerBase.TooltipPaddingXUnits,
                    preferred.y + NavMapMarkerBase.TooltipPaddingYUnits);
            }

            if (tooltip?.activeSelf == false)
                tooltip.SetActive(true);
            pinned = isPinned;
        }

        public void HideTooltip()
        {
            if (tooltip?.activeSelf == true)
                tooltip.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Owner == null)
                return;
            Owner.OnBridgeHoverEnter(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Owner == null)
                return;
            Owner.OnBridgeHoverExit(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null)
                return;
            Owner.OnBridgeClick(this);
        }
    }
}
