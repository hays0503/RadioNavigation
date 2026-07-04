using UnityEngine;
using UnityEngine.EventSystems;

namespace UBOAT.Mods.TestUI
{
    public class PanelDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform panelRect;
        private Canvas parentCanvas;
        private Texture2D moveCursorTexture;
        private static readonly Vector2 cursorHotspot = new Vector2(16, 16);
        private bool moveCursorActive;

        private void Start()
        {
            panelRect = GetComponent<RectTransform>();
            parentCanvas = GetComponentInParent<Canvas>();
            GenerateMoveCursor();
        }

        private void GenerateMoveCursor()
        {
            moveCursorTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color transparent = new Color(0, 0, 0, 0);
            Color white = Color.white;

            for (int x = 0; x < 32; x++)
                for (int y = 0; y < 32; y++)
                    moveCursorTexture.SetPixel(x, y, transparent);

            for (int y = 4; y <= 27; y++)
                moveCursorTexture.SetPixel(15, y, white);

            for (int x = 4; x <= 27; x++)
                moveCursorTexture.SetPixel(x, 15, white);

            moveCursorTexture.SetPixel(15, 3, white);
            moveCursorTexture.SetPixel(14, 2, white);
            moveCursorTexture.SetPixel(15, 2, white);
            moveCursorTexture.SetPixel(16, 2, white);
            moveCursorTexture.SetPixel(15, 1, white);

            moveCursorTexture.SetPixel(15, 28, white);
            moveCursorTexture.SetPixel(14, 29, white);
            moveCursorTexture.SetPixel(15, 29, white);
            moveCursorTexture.SetPixel(16, 29, white);
            moveCursorTexture.SetPixel(15, 30, white);

            moveCursorTexture.SetPixel(3, 15, white);
            moveCursorTexture.SetPixel(2, 14, white);
            moveCursorTexture.SetPixel(2, 15, white);
            moveCursorTexture.SetPixel(2, 16, white);
            moveCursorTexture.SetPixel(1, 15, white);

            moveCursorTexture.SetPixel(28, 15, white);
            moveCursorTexture.SetPixel(29, 14, white);
            moveCursorTexture.SetPixel(29, 15, white);
            moveCursorTexture.SetPixel(29, 16, white);
            moveCursorTexture.SetPixel(30, 15, white);

            moveCursorTexture.Apply();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            GameObject pressed = eventData.pointerPress;
            if (pressed != null && pressed.GetComponentInParent<ExpanderHandler>() != null)
                return;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (parentCanvas == null)
                return;

            panelRect.anchoredPosition += eventData.delta / parentCanvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData) { }

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
