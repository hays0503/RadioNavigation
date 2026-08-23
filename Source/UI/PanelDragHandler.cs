using UnityEngine;
using UnityEngine.EventSystems;

namespace UBOAT.Mods.RadioNavigation
{
    public class PanelDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform panelRect;
        private Canvas parentCanvas;
        private Texture2D moveCursorTexture;
        private static readonly Vector2 cursorHotspot = new Vector2(16, 16);
        private static Texture2D cachedCursorTexture;
        private Texture2D cursorOverrideTexture;
        private Vector2 cursorOverrideHotspot;
        private bool moveCursorActive;
        private bool isDragging;
        private int activePointerId = -1;
        private bool chronometerPointerInside;
        private ExpanderHandler expander;
        private ChronometerOverlayHandler chronometerOverlay;

        internal Texture2D MoveCursorTexture => moveCursorTexture;
        internal Vector2 MoveCursorHotspot => cursorHotspot;

        // While the pointer is over any chronometer zone, the zone cursors
        // (arrows / crosshair) own the pointer - the panel move cursor and
        // its clears must not fight them.
        internal void SetChronometerPointerInside(bool value)
        {
            chronometerPointerInside = value;
        }

        private void Start()
        {
            panelRect = GetComponent<RectTransform>();
            if (panelRect.IsNull())
            {
                Debug.LogError("[RadioNav][PanelDragHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }

            parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas.IsNull())
                Debug.LogWarning("[RadioNav][PanelDragHandler] Canvas not found in parents, drag scaling may be off");

            if (cachedCursorTexture.IsNull())
                cachedCursorTexture = GenerateMoveCursor();
            moveCursorTexture = cachedCursorTexture;
            expander = GetComponentInChildren<ExpanderHandler>(true);
            chronometerOverlay = GetComponent<ChronometerOverlayHandler>();
        }

        private static Texture2D GenerateMoveCursor()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color transparent = new Color(0, 0, 0, 0);
            Color white = Color.white;

            Color32[] pixels = new Color32[1024];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = transparent;

            for (int y = 4; y <= 27; y++) pixels[32 * y + 15] = white;
            for (int x = 4; x <= 27; x++) pixels[32 * 15 + x] = white;

            pixels[32 * 3 + 15] = white;
            pixels[32 * 2 + 14] = white;
            pixels[32 * 2 + 15] = white;
            pixels[32 * 2 + 16] = white;
            pixels[32 * 1 + 15] = white;

            pixels[32 * 28 + 15] = white;
            pixels[32 * 29 + 14] = white;
            pixels[32 * 29 + 15] = white;
            pixels[32 * 29 + 16] = white;
            pixels[32 * 30 + 15] = white;

            pixels[32 * 15 + 3] = white;
            pixels[32 * 14 + 2] = white;
            pixels[32 * 15 + 2] = white;
            pixels[32 * 16 + 2] = white;
            pixels[32 * 15 + 1] = white;

            pixels[32 * 15 + 28] = white;
            pixels[32 * 14 + 29] = white;
            pixels[32 * 15 + 29] = white;
            pixels[32 * 16 + 29] = white;
            pixels[32 * 15 + 30] = white;

            tex.SetPixels32(pixels);
            tex.Apply();

            Application.quitting += () =>
            {
                if (tex.IsNotNull())
                    Object.Destroy(tex);
            };

            return tex;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            // F198: match CursorHandler/SpinnerHandlerBase/ExpanderHandler —
            // no panel movement while the radio is off or ground is inactive.
            if (!ToggleHandler.IsInteractable)
                return;

            // F207: child controls without their own IBeginDragHandler
            // (Zone buttons, Toggle, Ground) resolve to this panel root;
            // dragging them must not move the panel.
            Transform press = eventData.pointerPress.IsNotNull()
                ? eventData.pointerPress.transform
                : null;
            if (press.IsNull())
                return;

            if ((expander.IsNotNull() && press.IsChildOf(expander.transform))
                || (chronometerOverlay.IsNotNull() && chronometerOverlay.IsChronometerElement(press))
                || press.GetComponentInParent<ZoneHandler>() != null
                || press.GetComponentInParent<ToggleHandler>() != null
                || press.GetComponentInParent<GroundHandler>() != null)
                return;

            isDragging = true;
            activePointerId = eventData.pointerId;
        }

        private bool IsActiveDrag(PointerEventData eventData) =>
            isDragging && eventData.pointerId == activePointerId;

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsActiveDrag(eventData) || parentCanvas.IsNull())
                return;

            panelRect.anchoredPosition += eventData.delta / parentCanvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId) return;
            isDragging = false;
            activePointerId = -1;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (chronometerPointerInside)
                return;
            if (eventData.dragging)
                return;

            if (cursorOverrideTexture.IsNotNull())
            {
                Cursor.SetCursor(cursorOverrideTexture, cursorOverrideHotspot, CursorMode.Auto);
                return;
            }

            Cursor.SetCursor(moveCursorTexture, cursorHotspot, CursorMode.Auto);
            moveCursorActive = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // F187: while dragging, the cursor may leave the panel bounds
            // (fast drag); OnEndDrag resets the cursor via OnPointerExit
            // semantics below - keep the move cursor until the drag ends.
            if (!moveCursorActive || isDragging)
                return;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            moveCursorActive = false;
        }

        internal void SetCursorOverride(Texture2D texture, Vector2 hotspot)
        {
            cursorOverrideTexture = texture;
            cursorOverrideHotspot = hotspot;
            Cursor.SetCursor(cursorOverrideTexture, cursorOverrideHotspot, CursorMode.Auto);
        }

        internal void ClearCursorOverride()
        {
            cursorOverrideTexture = null;
            cursorOverrideHotspot = Vector2.zero;
            if (moveCursorActive)
                Cursor.SetCursor(moveCursorTexture, cursorHotspot, CursorMode.Auto);
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private void OnDisable()
        {
            // F149: reset drag state on disable so a mid-drag disable (which
            // skips OnEndDrag) cannot lock out future drags.
            isDragging = false;
            activePointerId = -1;

            if (!moveCursorActive)
            {
                cursorOverrideTexture = null;
                return;
            }

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            moveCursorActive = false;
            cursorOverrideTexture = null;
        }

        private void OnDestroy()
        {
            // F42: the cached cursor texture is a GPU resource; Application.quitting
            // only fires on game exit, so a mod reload used to leak it until
            // restart. A single panel is the expected deployment (RadioInstaller
            // instantiates one), so destroying the cache here is safe.
            if (cachedCursorTexture.IsNotNull())
            {
                Object.Destroy(cachedCursorTexture);
                cachedCursorTexture = null;
            }
        }
    }
}
