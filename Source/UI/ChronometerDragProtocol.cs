using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Chronometer drag protocol (P5.3 CH-4): the local (raised) drag, the
    /// map-drag path for the expanded face, and the GameInput blockade
    /// acquire/release. Extracted from ChronometerOverlayHandler (SRP: the
    /// drag mechanics are not the overlay orchestrator's logic). The owner
    /// supplies the context queries (raised/expanded/radio-visible) and
    /// callbacks (native-drag cancel, position change, position persist);
    /// the native-drag cancellation itself stays with the owner because it
    /// touches the ShipControlUI bridge.
    /// </summary>
    internal sealed class ChronometerDragProtocol
    {
        private readonly System.Func<bool> isRaised;
        private readonly System.Func<bool> isExpanded;
        private readonly System.Func<bool> isRadioVisible;
        private readonly System.Action onPositionChanged;

        private bool localDragActive;
        private Vector2 localDragPoint;
        private bool mapDragActive;
        private GameInput gameInput;

        public ChronometerDragProtocol(
            System.Func<bool> isRaised,
            System.Func<bool> isExpanded,
            System.Func<bool> isRadioVisible,
            System.Action onPositionChanged)
        {
            this.isRaised = isRaised;
            this.isExpanded = isExpanded;
            this.isRadioVisible = isRadioVisible;
            this.onPositionChanged = onPositionChanged;
        }

        /// <summary>True while a local (raised) drag is in flight.</summary>
        public bool LocalDragActive
        {
            get { return localDragActive; }
        }

        // ---- local (raised) drag ------------------------------------------

        public void BeginLocalDrag(RectTransform chronometerDragContainer, PointerEventData eventData)
        {
            if (!isRaised() || !isRadioVisible() || eventData == null)
                return;

            if (eventData.pointerPress.IsNotNull()
                && eventData.pointerPress.GetComponentInParent<Selectable>().IsNotNull())
            {
                return;
            }

            RectTransform parent = chronometerDragContainer.parent as RectTransform;
            if (parent.IsNull()
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out localDragPoint))
            {
                return;
            }

            localDragActive = true;
        }

        public void DragLocal(RectTransform chronometerDragContainer, PointerEventData eventData)
        {
            if (!localDragActive || eventData == null)
                return;

            RectTransform parent = chronometerDragContainer.parent as RectTransform;
            Vector2 currentPoint;
            if (parent.IsNull()
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out currentPoint))
            {
                return;
            }

            chronometerDragContainer.anchoredPosition += currentPoint - localDragPoint;
            localDragPoint = currentPoint;
            if (onPositionChanged != null)
                onPositionChanged();
        }

        public void EndLocalDrag()
        {
            // The shield receives OnEndDrag for every drag over the container
            // even when our Begin gates rejected it - only real drags matter.
            if (!localDragActive)
                return;
            localDragActive = false;
        }

        // ---- map drag (expanded face) --------------------------------------

        public void OnMapDragBegin(Component blocker,
            ChronometerScaleSubsystem scaleSubsystem,
            System.Action cancelNativeDrag)
        {
            if (!isExpanded() || isRaised())
            {
                scaleSubsystem.CancelMapDrag();
                return;
            }
            mapDragActive = true;
            if (cancelNativeDrag != null)
                cancelNativeDrag();
            // Mirror native HandleDialDragging: the GameInput blockade keeps
            // the map pan and other input consumers from eating the same
            // left-drag gesture while we move the expanded face.
            AcquireInputBlockade(blocker);
        }

        public void OnMapDragDelta(RectTransform chronometerDragContainer, Vector2 delta,
            ChronometerScaleSubsystem scaleSubsystem,
            System.Action cancelNativeDrag,
            System.Action persistPosition)
        {
            if (!mapDragActive)
                return;
            if (!isExpanded() || isRaised())
            {
                EndMapDrag(scaleSubsystem, cancelNativeDrag, persistPosition);
                return;
            }
            // Native HandleDialDragging would fight us for the same gesture.
            if (cancelNativeDrag != null)
                cancelNativeDrag();
            chronometerDragContainer.position += new Vector3(delta.x, delta.y, 0f);
            if (onPositionChanged != null)
                onPositionChanged();
            if (Time.frameCount % 30 == 0 && persistPosition != null)
                persistPosition();
        }

        public void OnMapDragEnd(ChronometerScaleSubsystem scaleSubsystem,
            System.Action cancelNativeDrag,
            System.Action persistPosition)
        {
            EndMapDrag(scaleSubsystem, cancelNativeDrag, persistPosition);
        }

        private void EndMapDrag(ChronometerScaleSubsystem scaleSubsystem,
            System.Action cancelNativeDrag,
            System.Action persistPosition)
        {
            bool wasActive = mapDragActive;
            mapDragActive = false;

            if (wasActive)
            {
                if (cancelNativeDrag != null)
                    cancelNativeDrag();
                if (persistPosition != null)
                    persistPosition();
            }

            scaleSubsystem.CancelMapDrag();
        }

        // ---- input blockade -------------------------------------------------

        public void AcquireInputBlockade(Component blocker)
        {
            if (gameInput.IsNull() && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput.IsNotNull())
                gameInput.AddBlockade(blocker);
        }

        public void ReleaseInputBlockade(Component blocker)
        {
            if (gameInput.IsNull() && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput.IsNotNull())
                gameInput.RemoveBlockade(blocker);
        }
    }
}
