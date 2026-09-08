using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public class CursorHandler : BlockInputUI, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        private const float MinAngle = -90f;
        private const float MaxAngle = 90f;
        private const float ScrollStepNormal = 3f;
        private const float ScrollStepFine = 0.5f;

        private bool isDragging;
        private bool isHovering;
        private int activePointerId = -1;
        private RectTransform cursorRect;
        // F102: ScrollRect is static after setup; cache instead of walking
        // the hierarchy every frame while scrolling.
        private ScrollRect cachedScrollRect;

        private void Start()
        {
            cursorRect = GetComponent<RectTransform>();
            if (cursorRect.IsNull())
            {
                Debug.LogError("[RadioNav][CursorHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }
            cursorRect.pivot = new Vector2(0.5f, 0.5f);

            Image img = GetComponent<Image>();
            if (img.IsNotNull())
                img.raycastTarget = true;
            else
                Debug.LogError("[RadioNav][CursorHandler] Image component not found, drag may not work");

            // F95: no need for a disabled-axes ScrollRect — IScrollHandler
            // already consumes scroll events via eventData.Use() and the
            // parent ScrollRect is stopped in Update (StopMovement).
            cachedScrollRect = GetComponentInParent<ScrollRect>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (!RadioPowerBus.IsInteractable) return;
            if (cursorRect.IsNull())
            {
                Debug.LogError("[RadioNav][CursorHandler] cursorRect not initialized");
                return;
            }
            isDragging = true;
            activePointerId = eventData.pointerId;
        }

        private bool IsActiveDrag(PointerEventData eventData) =>
            isDragging && eventData.pointerId == activePointerId;

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsActiveDrag(eventData))
                return;
            if (!RadioPowerBus.IsInteractable) return;

            Vector2 dir = eventData.position - (Vector2)cursorRect.position;
            if (dir.y.IsNegative()) dir.y = 0;
            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

            cursorRect.localRotation = Quaternion.Euler(0, 0, -angle);

            if (RadioHandler.Instance.IsNotNull())
                RadioHandler.Instance.CursorAngle = angle;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId) return;
            isDragging = false;
            activePointerId = -1;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovering = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovering = false;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!RadioPowerBus.IsInteractable) return;
            if (cursorRect.IsNull()) return;

            float delta = eventData.scrollDelta.y;
            if (delta.IsZero()) return;

            float step = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                ? ScrollStepFine
                : ScrollStepNormal;

            // F97: RadioHandler.Instance must be resolved before it is
            // dereferenced — a null Instance previously threw on the line
            // computing newAngle while the null guard only ran afterwards.
            if (RadioHandler.Instance.IsNull())
                return;

            float newAngle = Mathf.Clamp(
                RadioHandler.Instance.CursorAngle + delta * step,
                MinAngle, MaxAngle);

            cursorRect.localRotation = Quaternion.Euler(0, 0, -newAngle);
            RadioHandler.Instance.CursorAngle = newAngle;

            eventData.Use();
        }

        private void Update()
        {
            if (!isHovering || !RadioPowerBus.IsInteractable) return;

            float scrollY = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollY).IsNegligible(0.01f)) return;

            if (EventSystem.current.IsNotNull() && EventSystem.current.currentSelectedGameObject != gameObject)
                EventSystem.current.SetSelectedGameObject(gameObject);

            if (cachedScrollRect.IsNotNull())
                cachedScrollRect.StopMovement();
        }
    }
}
