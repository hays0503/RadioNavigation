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
        private static Texture2D _cachedCursorTexture;
        private bool moveCursorActive;
        private bool isDragging;
        private int activePointerId = -1;
        private ExpanderHandler _expander;

        private void Start()
        {
            panelRect = GetComponent<RectTransform>();
            if (panelRect == null)
            {
                Debug.LogError("[RadioNav][PanelDragHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }

            parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
                Debug.LogWarning("[RadioNav][PanelDragHandler] Canvas not found in parents, drag scaling may be off");

            if (_cachedCursorTexture == null)
                _cachedCursorTexture = GenerateMoveCursor();
            moveCursorTexture = _cachedCursorTexture;
            _expander = GetComponentInChildren<ExpanderHandler>(true);
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
                if (tex != null)
                    Object.Destroy(tex);
            };

            return tex;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (_expander != null && eventData.pointerPress != null &&
                eventData.pointerPress.transform.IsChildOf(_expander.transform))
                return;

            isDragging = true;
            activePointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging || eventData.pointerId != activePointerId || parentCanvas == null)
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
            if (eventData.dragging)
                return;

            Cursor.SetCursor(moveCursorTexture, cursorHotspot, CursorMode.Auto);
            moveCursorActive = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!moveCursorActive)
                return;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            moveCursorActive = false;
        }

        private void OnDisable()
        {
            if (!moveCursorActive)
                return;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            moveCursorActive = false;
        }
    }
}
