using UnityEngine.EventSystems;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Stops the radio root PanelDragHandler from claiming native chronometer drags.
    public sealed class ChronometerDragShield : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ChronometerOverlayHandler owner;

        public void SetOwner(ChronometerOverlayHandler value)
        {
            owner = value;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner.IsNotNull())
                owner.BeginLocalDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner.IsNotNull())
                owner.DragLocal(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (owner.IsNotNull())
                owner.EndLocalDrag();
        }
    }
}
