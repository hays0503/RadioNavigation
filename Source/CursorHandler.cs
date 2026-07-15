using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class CursorHandler : BlockInputUI, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private const float MinAngle = -90f;
        private const float MaxAngle = 90f;

        private bool isDragging;
        private int activePointerId = -1;
        private RectTransform cursorRect;

        private void Start()
        {
            cursorRect = GetComponent<RectTransform>();
            if (cursorRect == null)
            {
                Debug.LogError("[TestUI][CursorHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }
            cursorRect.pivot = new Vector2(0.5f, 0.5f);

            Image img = GetComponent<Image>();
            if (img != null)
                img.raycastTarget = true;
            else
                Debug.LogError("[TestUI][CursorHandler] Image component not found, drag may not work");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (cursorRect == null)
            {
                Debug.LogError("[TestUI][CursorHandler] cursorRect not initialized");
                return;
            }
            isDragging = true;
            activePointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging || eventData.pointerId != activePointerId)
                return;

            Vector2 dir = eventData.position - (Vector2)cursorRect.position;
            if (dir.y < 0) dir.y = 0;
            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

            cursorRect.localRotation = Quaternion.Euler(0, 0, -angle);

            if (RadioHandler.Instance != null)
                RadioHandler.Instance.CursorAngle = angle;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId) return;
            isDragging = false;
            activePointerId = -1;
        }
    }
}
