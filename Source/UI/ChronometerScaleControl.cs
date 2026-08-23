using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the chronometer scale zone. Everything is
    /// geometric (rect vs mouse position): hover hint, click, drag
    /// discrimination. uGUI pointer events are not used because native
    /// graphics above the transparent control can occlude the raycast.
    /// Cursor policy lives in the owner and mirrors the native radio-room
    /// etalon: hand cursor on hover, no cursor change during drags.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour
    {
        private ChronometerOverlayHandler owner;
        private Canvas canvas;
        private bool canvasResolved;
        private bool pressPending;
        private Vector2 pressScreenPos;
        private bool dbgLastHovered;
        private bool dbgEnabledLogged;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
        }

        private void OnEnable()
        {
            Debug.Log("[RadioNav][Chronometer][HoverDbg] sensor OnEnable frame=" + Time.frameCount);
        }

        private void OnDisable()
        {
            Debug.Log("[RadioNav][Chronometer][HoverDbg] sensor OnDisable frame=" + Time.frameCount);
        }

        private void Update()
        {
            if (!dbgEnabledLogged)
            {
                dbgEnabledLogged = true;
                Debug.Log("[RadioNav][Chronometer][HoverDbg] sensor first Update frame=" + Time.frameCount
                    + " activeInHierarchy=" + gameObject.activeInHierarchy
                    + " rect=" + ((RectTransform)transform).rect);
            }

            if (owner.IsNull())
                return;

            Vector2 mouse = Input.mousePosition;
            bool hovered = RectTransformContains(mouse);
            if (hovered != dbgLastHovered)
            {
                Debug.Log("[RadioNav][Chronometer][HoverDbg] sensor hover " + dbgLastHovered + "->" + hovered
                    + " frame=" + Time.frameCount + " mouse=" + mouse);
                dbgLastHovered = hovered;
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
                // Drag gesture: the game moves the chronometer, the scale
                // toggle is cancelled and the cursor stays as-is (etalon).
                pressPending = false;
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
