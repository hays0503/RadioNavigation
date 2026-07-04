using UnityEngine;
using UnityEngine.EventSystems;

namespace UBOAT.Mods.TestUI
{
    public class ExpanderHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private float sensitivity = 0.005f;
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 3f;

        private RectTransform panelRect;
        private bool isDragging;
        private Vector3 dragStartScale;
        private float dragStartMouseX;

        private void Start()
        {
            panelRect = transform.parent.GetComponent<RectTransform>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            isDragging = true;
            dragStartScale = panelRect.localScale;
            dragStartMouseX = eventData.position.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging) return;
            float diff = (eventData.position.x - dragStartMouseX) * sensitivity;
            float newScale = Mathf.Clamp(dragStartScale.x + diff, minScale, maxScale);
            panelRect.localScale = Vector3.one * newScale;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
        }
    }
}
