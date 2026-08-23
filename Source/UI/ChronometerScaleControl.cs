using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the chronometer scale toggle. uGUI pointer
    /// events drive only the hover cursor; the click is detected here as
    /// press + release inside the rect without a drag, so dragging moves the
    /// chronometer and never toggles the scale.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        private ChronometerOverlayHandler owner;
        private Canvas canvas;
        private bool canvasResolved;
        // Click = press inside rect + release inside rect without exceeding
        // the drag threshold. Press-drag-release moves the chronometer and
        // must never toggle the scale.
        private bool pressPending;
        private Vector2 pressScreenPos;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
        }

        private void Update()
        {
            if (owner.IsNull())
                return;

            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                pressPending = RectTransformContains(mouse);
                pressScreenPos = mouse;
                return;
            }

            if (!pressPending)
                return;

            float threshold = EventSystem.current != null
                ? EventSystem.current.pixelDragThreshold
                : 10f;
            if (Vector2.Distance(mouse, pressScreenPos) > threshold)
            {
                pressPending = false;
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                pressPending = false;
                if (RectTransformContains(mouse))
                    owner.OnScaleClicked();
            }
        }

        private bool RectTransformContains(Vector2 screenPoint)
        {
            if (!canvasResolved)
            {
                canvas = GetComponentInParent<Canvas>(true);
                canvasResolved = true;
            }
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            RectTransform rect = transform as RectTransform;
            return rect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner.IsNotNull())
                owner.SetScaleHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner.IsNotNull())
                owner.SetScaleHover(false);
        }
    }
}
