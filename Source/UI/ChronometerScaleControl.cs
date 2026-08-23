using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Single input sensor for the chronometer scale toggle. uGUI pointer
    /// events drive only the hover cursor; the click itself is emitted by
    /// the geometric per-frame poll on the button-DOWN edge, so one physical
    /// press is exactly one toggle regardless of canvas re-parenting.
    /// </summary>
    public sealed class ChronometerScaleControl : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
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
    }
}
