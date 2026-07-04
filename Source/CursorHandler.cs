using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class CursorHandler : BlockInputUI, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        [SerializeField] private float minAngle = -90f;
        [SerializeField] private float maxAngle = 90f;

        private bool isDragging;
        private RectTransform cursorRect;

        private void Start()
        {
            cursorRect = GetComponent<RectTransform>();
            cursorRect.pivot = new Vector2(0.5f, 0.5f);

            GetComponent<Image>().raycastTarget = true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            isDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging)
                return;

            Vector2 dir = Input.mousePosition - cursorRect.position;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            angle = Mathf.Clamp(angle, minAngle, maxAngle);

            cursorRect.localRotation = Quaternion.Euler(0, 0, angle);

            if (RadioHandler.Instance != null)
                RadioHandler.Instance.CursorAngle = angle;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
        }
    }
}
