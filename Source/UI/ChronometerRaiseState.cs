using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Raise/restore state machine for the native chronometer (P5.3 CH
    /// slice): the canvas sorting snapshot, the reparent snapshot (parent,
    /// sibling index, default position), the created-component ownership
    /// flags and the radio-behind-native reordering. Extracted from
    /// ChronometerOverlayHandler (SRP: the snapshot bookkeeping is not the
    /// overlay orchestrator's logic). The owner supplies the drag container,
    /// the radio transform, the parent canvas and the ShipControlUI bridge
    /// writes; the subsystem reports success so the owner can log.
    /// </summary>
    internal sealed class ChronometerRaiseState
    {
        private Canvas chronometerCanvas;
        private GraphicRaycaster raycaster;
        private ChronometerDragShield dragShield;
        private bool createdCanvas;
        private bool createdRaycaster;
        private bool createdDragShield;
        private bool canvasStateCaptured;
        private bool reparented;
        private bool radioMovedBehind;

        private bool originalOverrideSorting;
        private int originalSortingLayerId;
        private int originalSortingOrder;
        private bool originalCanvasEnabled;
        private Transform originalParent;
        private int originalSiblingIndex = -1;
        private int originalRadioSiblingIndex = -1;

        /// <summary>True while the chronometer is raised (owner's gate).</summary>
        public bool Raised { get; private set; }

        public Canvas Canvas
        {
            get { return chronometerCanvas; }
        }
        public GraphicRaycaster Raycaster
        {
            get { return raycaster; }
        }
        public ChronometerDragShield DragShield
        {
            get { return dragShield; }
        }
        public bool CreatedCanvas
        {
            get { return createdCanvas; }
        }
        public bool CreatedRaycaster
        {
            get { return createdRaycaster; }
        }
        public bool CreatedDragShield
        {
            get { return createdDragShield; }
        }
        public bool Reparented
        {
            get { return reparented; }
        }

        /// <summary>
        /// Raises the chronometer: ensures the Canvas/Raycaster/DragShield on
        /// the container, snapshots the canvas sorting state, re-parents the
        /// container under the radio and reorders the radio behind the native
        /// UI when no parent canvas exists. onBeforeReparent snapshots state
        /// that must read the OLD parent (scale, default position);
        /// onAfterReparent reads the NEW one (attached scale, position
        /// update). Returns true on the raise transition.
        /// </summary>
        public bool Raise(RectTransform chronometerDragContainer, Transform radioTransform,
            Canvas parentCanvas, ChronometerOverlayHandler owner,
            System.Action onBeforeReparent,
            System.Action onAfterReparent)
        {
            if (Raised)
                return false;

            Canvas canvas = chronometerDragContainer.GetComponent<Canvas>();
            if (canvas.IsNull())
            {
                canvas = chronometerDragContainer.gameObject.AddComponent<Canvas>();
                createdCanvas = true;
            }

            chronometerCanvas = canvas;
            raycaster = chronometerDragContainer.GetComponent<GraphicRaycaster>();
            if (raycaster.IsNull())
            {
                raycaster = chronometerDragContainer.gameObject.AddComponent<GraphicRaycaster>();
                createdRaycaster = true;
            }

            dragShield = chronometerDragContainer.GetComponent<ChronometerDragShield>();
            if (dragShield.IsNull())
            {
                dragShield = chronometerDragContainer.gameObject.AddComponent<ChronometerDragShield>();
                createdDragShield = true;
            }
            dragShield.SetOwner(owner);

            if (!canvasStateCaptured)
            {
                originalOverrideSorting = canvas.overrideSorting;
                originalSortingLayerId = canvas.sortingLayerID;
                originalSortingOrder = canvas.sortingOrder;
                originalCanvasEnabled = canvas.enabled;
                canvasStateCaptured = true;
            }

            if (parentCanvas.IsNotNull())
            {
                canvas.sortingLayerID = parentCanvas.sortingLayerID;
                canvas.sortingOrder = parentCanvas.sortingOrder + 100;
                canvas.overrideSorting = true;
                canvas.enabled = true;
            }
            else
            {
                MoveRadioBehindNativeUi(radioTransform);
            }

            if (!reparented && chronometerDragContainer.parent != radioTransform)
            {
                originalParent = chronometerDragContainer.parent;
                originalSiblingIndex = chronometerDragContainer.GetSiblingIndex();
                if (onBeforeReparent != null)
                    onBeforeReparent();
                chronometerDragContainer.SetParent(radioTransform, true);
                if (onAfterReparent != null)
                    onAfterReparent();
                reparented = true;
            }

            chronometerDragContainer.SetAsLastSibling();
            Raised = true;
            return true;
        }

        /// <summary>
        /// Restores the snapshotted state: canvas sorting, native parent and
        /// sibling index, the radio sibling order; releases the drag-shield
        /// owner. Returns true on the restore transition.
        /// </summary>
        public bool Restore(RectTransform chronometerDragContainer, Transform radioTransform,
            Vector3 originalScale,
            System.Action restoreDefaultPosition)
        {
            if (!Raised)
                return false;

            if (chronometerCanvas.IsNotNull() && canvasStateCaptured)
            {
                chronometerCanvas.overrideSorting = originalOverrideSorting;
                chronometerCanvas.sortingLayerID = originalSortingLayerId;
                chronometerCanvas.sortingOrder = originalSortingOrder;
                chronometerCanvas.enabled = originalCanvasEnabled;
            }

            if (reparented && chronometerDragContainer.IsNotNull())
            {
                if (originalParent.IsNotNull())
                {
                    chronometerDragContainer.SetParent(originalParent, true);
                    int maxIndex = originalParent.childCount - 1;
                    chronometerDragContainer.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, maxIndex));
                    chronometerDragContainer.localScale = originalScale;
                }

                if (restoreDefaultPosition != null)
                    restoreDefaultPosition();

                originalParent = null;
                originalSiblingIndex = -1;
                reparented = false;
            }

            if (dragShield.IsNotNull())
                dragShield.SetOwner(null);

            RestoreRadioSibling(radioTransform);
            Raised = false;
            return true;
        }

        /// <summary>
        /// Destroys the mod-created components (teardown). Only the ones we
        /// added ourselves — the native container keeps its own.
        /// </summary>
        public void DestroyCreated()
        {
            if (createdCanvas && chronometerCanvas.IsNotNull())
                Object.Destroy(chronometerCanvas);
            if (createdRaycaster && raycaster.IsNotNull())
                Object.Destroy(raycaster);
            if (createdDragShield && dragShield.IsNotNull())
                Object.Destroy(dragShield);
        }

        private void MoveRadioBehindNativeUi(Transform radioTransform)
        {
            if (radioMovedBehind || radioTransform.parent.IsNull())
                return;

            originalRadioSiblingIndex = radioTransform.GetSiblingIndex();
            radioTransform.SetAsFirstSibling();
            radioMovedBehind = true;
        }

        private void RestoreRadioSibling(Transform radioTransform)
        {
            if (!radioMovedBehind || radioTransform.parent.IsNull())
                return;

            int maxIndex = radioTransform.parent.childCount - 1;
            radioTransform.SetSiblingIndex(Mathf.Clamp(originalRadioSiblingIndex, 0, maxIndex));
            originalRadioSiblingIndex = -1;
            radioMovedBehind = false;
        }
    }
}
