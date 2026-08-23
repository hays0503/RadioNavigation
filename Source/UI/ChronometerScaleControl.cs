using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the native chronometer. Two geometric zones:
    /// the scale control rect (center) hints the resize arrows and
    /// click-toggles the scale; the rest of the drag-area rect hints the move
    /// crosshair. While a press that started inside the body is held, hover
    /// notifications are frozen so the cursor stays stable through drags; a
    /// release inside the center without exceeding the drag threshold is a
    /// scale click.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour
    {
        private ChronometerOverlayHandler owner;
        private Canvas canvas;
        private bool canvasResolved;
        private bool pressedFromZone;
        private bool downInScale;
        private bool dragExceeded;
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
            bool scaleHovered = RectTransformContains(mouse);
            RectTransform dragArea = owner.GetDragAreaRect();
            bool moveHovered = !scaleHovered
                && dragArea.IsNotNull()
                && RectContains(dragArea, mouse);

            if (Input.GetMouseButtonDown(0))
            {
                pressedFromZone = scaleHovered || moveHovered;
                downInScale = scaleHovered;
                dragExceeded = false;
                pressScreenPos = mouse;
            }

            bool lmbHeld = Input.GetMouseButton(0);
            bool lmbUp = Input.GetMouseButtonUp(0);
            bool frozen = pressedFromZone && lmbHeld && !lmbUp;
            if (frozen)
            {
                // A press from inside the body is in progress (drag or
                // pending click): the last cursor stays until release.
                float threshold = EventSystem.current != null
                    ? EventSystem.current.pixelDragThreshold
                    : 10f;
                if (Vector2.Distance(mouse, pressScreenPos) > threshold)
                    dragExceeded = true;
                return;
            }

            owner.SetScaleHover(scaleHovered);
            owner.SetMoveHover(moveHovered);

            if (lmbUp && pressedFromZone)
            {
                pressedFromZone = false;
                if (!dragExceeded && downInScale && scaleHovered)
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

        private bool RectContains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect.IsNull())
                return false;
            if (!canvasResolved)
            {
                canvas = GetComponentInParent<Canvas>(true);
                canvasResolved = true;
            }
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera);
        }
    }
}
