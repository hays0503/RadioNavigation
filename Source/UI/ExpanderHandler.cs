using UnityEngine;
using UnityEngine.EventSystems;

namespace UBOAT.Mods.RadioNavigation
{
    public class ExpanderHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float Sensitivity = 0.005f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 3f;

        private RectTransform panelRect;
        private bool isDragging;
        private Vector3 dragStartScale;
        private float dragStartMouseX;

        private void Start()
        {
            if (transform.parent.IsNull())
            {
                Debug.LogError("[RadioNav][ExpanderHandler] parent is null \u2014 cannot get panel RectTransform");
                return;
            }
            panelRect = transform.parent.GetComponent<RectTransform>();
            if (panelRect.IsNull())
            {
                Debug.LogError("[RadioNav][ExpanderHandler] parent has no RectTransform");
                enabled = false;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (panelRect.IsNull()) return;
            isDragging = true;
            dragStartScale = panelRect.localScale;
            dragStartMouseX = eventData.position.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging) return;
            float diff = (eventData.position.x - dragStartMouseX) * Sensitivity;
            float newScale = Mathf.Clamp(dragStartScale.x + diff, MinScale, MaxScale);
            panelRect.localScale = Vector3.one * newScale;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
        }
    }
}
