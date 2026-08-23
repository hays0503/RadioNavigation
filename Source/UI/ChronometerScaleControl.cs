using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the chronometer scale toggle. Combines uGUI
    /// pointer events with a geometric per-frame poll so clicks register even
    /// when the EventSystem path is unreliable (canvas re-parenting between
    /// the native hierarchy and the raised radio layer). The owner only
    /// reacts; it never probes input itself.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private ChronometerOverlayHandler owner;
        private Canvas canvas;
        private bool canvasResolved;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
        }

        private void Update()
        {
            if (owner.IsNull())
                return;
            if (!Input.GetMouseButtonDown(0) || !RectTransformContains(Input.mousePosition))
                return;
            owner.OnScaleClicked();
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

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner.IsNotNull() && eventData.button == PointerEventData.InputButton.Left)
                owner.OnScaleClicked();
        }
    }
}
