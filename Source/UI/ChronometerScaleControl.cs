using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public sealed class ChronometerScaleControl : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private ChronometerOverlayHandler owner;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
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
                owner.ToggleChronometerScale();
        }
    }
}
