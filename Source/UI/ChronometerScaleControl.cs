using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the chronometer scale zone. Everything is
    /// geometric (rect vs mouse position): hover hint, click, drag
    /// observation. uGUI pointer events are not used because native graphics
    /// above the transparent control can occlude the raycast, which would
    /// kill the hover hint exactly where it matters. Click = press + release
    /// inside the rect without exceeding the drag threshold; press-drag is a
    /// move gesture and never toggles the scale.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour
    {
        private ChronometerOverlayHandler owner;
        private Canvas canvas;
        private bool canvasResolved;
        private bool pressPending;
        private Vector2 pressScreenPos;
        private bool nativeDragNotified;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
        }

        private void Update()
        {
            if (owner.IsNull())
                return;

            Vector2 mouse = Input.mousePosition;
            bool hovered = RectTransformContains(mouse);

            if (nativeDragNotified || owner.IsLocalDragActive)
            {
                // A drag owns the cursor; hover must not fight it.
                if (!Input.GetMouseButton(0))
                {
                    nativeDragNotified = false;
                    owner.OnScaleNativeDragChanged(false);
                }
                return;
            }

            owner.SetScaleHover(hovered);

            if (Input.GetMouseButtonDown(0))
            {
                pressPending = hovered;
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
                nativeDragNotified = true;
                owner.OnScaleNativeDragChanged(true);
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                pressPending = false;
                if (hovered)
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
    }
}
