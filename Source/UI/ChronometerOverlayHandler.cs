using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Input;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    // Keeps the game's native F7 chronometer visible when the radio panel is
    // on top of the shared screen-space canvas.
    public sealed class ChronometerOverlayHandler : MonoBehaviour
    {
        private ShipControlUI shipControlUI;

        // Etalon (native radio-room UX): hovering an interactive element
        // shows the game-wide hand cursor via CursorManager.
        [Inject] private static CursorManager cursorManager;
        private RadioHandler radioHandler;
        private GameInput gameInput;
        private RectTransform chronometerDragContainer;
        private Toggle chronometerButton;
        private GraphicRaycaster chronometerRaycaster;
        private ChronometerDragShield chronometerDragShield;
        private PanelDragHandler panelDragHandler;
        // Cursor application backend lives in ChronometerCursorBackend
        // (extracted); the request flags stay here (sensor inputs).
        private readonly ChronometerCursorBackend cursorBackend = new ChronometerCursorBackend();
        private Canvas chronometerCanvas;
        private Canvas parentCanvas;
        private bool createdCanvas;
        private bool createdRaycaster;
        private bool createdDragShield;
        private bool originalOverrideSorting;
        private int originalSortingLayerId;
        private int originalSortingOrder;
        private bool originalCanvasEnabled;
        private bool canvasStateCaptured;
        private bool chronometerRaised;
        private Transform originalParent;
        private int originalSiblingIndex = -1;
        private bool chronometerReparented;
        private Vector2 originalDefaultPosition;
        private bool defaultPositionCaptured;
        private int originalRadioSiblingIndex = -1;
        private bool radioMovedBehind;
        private bool resolveWarningLogged;
        private bool previousOpenState;
        private bool hasPreviousOpenState;
        private bool nativeChangedThisFrame;
        private bool localDragActive;
        private Vector2 localDragPoint;
        // Expand/scale subsystem lives in ChronometerScaleSubsystem (extracted).
        private readonly ChronometerScaleSubsystem scaleSubsystem = new ChronometerScaleSubsystem();
        private bool reqScaleHover;
        private bool reqMoveValid;
        private bool reqMoveHover;
        private bool chronoPointerInsideCached;

        private void Update()
        {
            if (localDragActive)
                CancelNativeDrag();
        }

        private void LateUpdate()
        {
            if (!TryResolveChronometer())
                return;

            bool isOpen = shipControlUI.IsChronometerOpen;
            if (hasPreviousOpenState && isOpen != previousOpenState)
                nativeChangedThisFrame = true;

            if (Input.GetKeyDown(KeyCode.F7)
                && !nativeChangedThisFrame
                && chronometerButton.IsNotNull())
            {
                chronometerButton.isOn = !chronometerButton.isOn;
                isOpen = chronometerButton.isOn;
                Debug.Log("[RadioNav][Chronometer] F7 fallback toggled native chronometer");
            }

            SyncChronometerLayer(IsRadioVisible && isOpen);
            // Hover is re-asserted by the sensor every frame; the restore
            // path (ResetChronometerScale) clears it when the chronometer
            // context actually goes away. No per-frame reset here - on the
            // map chronometerRaised is false and it would kill the hint.
            previousOpenState = isOpen;
            hasPreviousOpenState = true;
            nativeChangedThisFrame = false;
        }

        private void SyncChronometerLayer(bool isOpen)
        {
            if (isOpen == chronometerRaised)
                return;

            if (isOpen)
                RaiseChronometer();
            else
                RestoreChronometerLayer();
        }

        private bool TryResolveChronometer()
        {
            if (radioHandler.IsNull())
                radioHandler = GetComponent<RadioHandler>();
            if (panelDragHandler.IsNull())
                panelDragHandler = GetComponent<PanelDragHandler>();

            if (shipControlUI.IsNull())
            {
                if (InjectionFramework.Instance.IsNotNull())
                    shipControlUI = InjectionFramework.Instance.GetInstance<ShipControlUI>();
                if (shipControlUI.IsNull())
                    shipControlUI = FindShipControlUI();
            }

            if (shipControlUI.IsNull())
                return false;

            if (chronometerDragContainer.IsNotNull())
            {
                if (parentCanvas.IsNull())
                    parentCanvas = GetComponentInParent<Canvas>(true);
                ResolveChronometerButton();
                // Scale input exists from scene start: the container is a
                // native serialized child, so the control can live under the
                // game canvas immediately instead of waiting for a raise.
                scaleSubsystem.EnsureControl(chronometerDragContainer, this);
                return true;
            }

            if (!ShipControlUIBridge.HasDragContainer)
            {
                LogResolveWarning("ShipControlUI.chronometerToolDragContainer field not found");
                return false;
            }

            chronometerDragContainer = ShipControlUIBridge.GetDragContainer(shipControlUI);
            if (chronometerDragContainer.IsNull())
            {
                LogResolveWarning("native chronometer drag container is not ready");
                return false;
            }

            parentCanvas = GetComponentInParent<Canvas>(true);
            ResolveChronometerButton();
            scaleSubsystem.EnsureControl(chronometerDragContainer, this);
            Debug.Log("[RadioNav][Chronometer] native ShipControlUI bridge resolved");
            return true;
        }

        private bool IsRadioVisible => radioHandler.IsNull() || radioHandler.IsVisible;

        private static ShipControlUI FindShipControlUI()
        {
            ShipControlUI active = FindObjectOfType<ShipControlUI>();
            if (active.IsNotNull())
                return active;

            ShipControlUI[] all = Resources.FindObjectsOfTypeAll<ShipControlUI>();
            for (int i = 0; i < all.Length; i++)
            {
                ShipControlUI candidate = all[i];
                if (candidate.IsNotNull()
                    && candidate.gameObject.scene.IsValid()
                    && candidate.hideFlags == HideFlags.None)
                {
                    return candidate;
                }
            }

            return all.Length > 0 ? all[0] : null;
        }

        private void ResolveChronometerButton()
        {
            if (chronometerButton.IsNotNull() || !ShipControlUIBridge.HasButton)
                return;

            chronometerButton = ShipControlUIBridge.GetButton(shipControlUI);
            if (chronometerButton.IsNull())
                LogResolveWarning("native chronometer toggle is not ready");
        }

        private void RaiseChronometer()
        {
            Canvas canvas = chronometerDragContainer.GetComponent<Canvas>();
            if (canvas.IsNull())
            {
                canvas = chronometerDragContainer.gameObject.AddComponent<Canvas>();
                createdCanvas = true;
            }

            chronometerCanvas = canvas;
            chronometerRaycaster = chronometerDragContainer.GetComponent<GraphicRaycaster>();
            if (chronometerRaycaster.IsNull())
            {
                chronometerRaycaster = chronometerDragContainer.gameObject.AddComponent<GraphicRaycaster>();
                createdRaycaster = true;
            }

            chronometerDragShield = chronometerDragContainer.GetComponent<ChronometerDragShield>();
            if (chronometerDragShield.IsNull())
            {
                chronometerDragShield = chronometerDragContainer.gameObject.AddComponent<ChronometerDragShield>();
                createdDragShield = true;
            }
            chronometerDragShield.SetOwner(this);

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
                MoveRadioBehindNativeUi();
            }

            if (!chronometerReparented && chronometerDragContainer.parent != transform)
            {
                originalParent = chronometerDragContainer.parent;
                originalSiblingIndex = chronometerDragContainer.GetSiblingIndex();
                CaptureDefaultPosition();
                scaleSubsystem.Capture(chronometerDragContainer);
                chronometerDragContainer.SetParent(transform, true);
                scaleSubsystem.CaptureAttached(chronometerDragContainer);
                UpdateDefaultPositionForRadioParent();
                chronometerReparented = true;
            }
            else if (!scaleSubsystem.ScaleCaptured)
            {
                scaleSubsystem.Capture(chronometerDragContainer);
                scaleSubsystem.CaptureAttached(chronometerDragContainer);
            }

            scaleSubsystem.EnsureControl(chronometerDragContainer, this);

            chronometerDragContainer.SetAsLastSibling();
            chronometerRaised = true;
            Debug.Log("[RadioNav][Chronometer] native F7 chronometer attached to radio and raised");
        }

        private void RestoreChronometerLayer()
        {
            EndLocalDrag();
            scaleSubsystem.Reset(chronometerDragContainer, this);

            if (chronometerCanvas.IsNotNull() && canvasStateCaptured)
            {
                chronometerCanvas.overrideSorting = originalOverrideSorting;
                chronometerCanvas.sortingLayerID = originalSortingLayerId;
                chronometerCanvas.sortingOrder = originalSortingOrder;
                chronometerCanvas.enabled = originalCanvasEnabled;
            }

            if (chronometerReparented && chronometerDragContainer.IsNotNull())
            {
                if (originalParent.IsNotNull())
                {
                    chronometerDragContainer.SetParent(originalParent, true);
                    int maxIndex = originalParent.childCount - 1;
                    chronometerDragContainer.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, maxIndex));
                    chronometerDragContainer.localScale = scaleSubsystem.OriginalScale;
                }

                RestoreDefaultPosition();

                originalParent = null;
                originalSiblingIndex = -1;
                chronometerReparented = false;
            }

            scaleSubsystem.ClearSnapshot();

            // CH-3 leak fix: the mod-owned sensor must not survive inside the
            // native hierarchy after restore. Destroy it so the next raise
            // recreates a fresh, correctly-parented control.
            scaleSubsystem.ReleaseControl();

            if (chronometerDragShield.IsNotNull())
                chronometerDragShield.SetOwner(null);

            RestoreRadioSibling();
            chronometerRaised = false;
        }

        internal void SetScaleHover(bool value)
        {
            if (localDragActive)
                return;
            reqScaleHover = value;
            RefreshCursor();
            ForwardChronometerPointerInside();
        }

        internal void SetMoveHover(bool moveHovered)
        {
            if (localDragActive)
                return;
            reqMoveHover = moveHovered;
            reqMoveValid = true;
            RefreshCursor();
            ForwardChronometerPointerInside();
        }

        private void ForwardChronometerPointerInside()
        {
            // PanelDragHandler must not paint its own move cursor while the
            // pointer is over any chronometer zone - the zone cursors win.
            bool inside = reqMoveValid && reqMoveHover;
            if (inside == chronoPointerInsideCached)
                return;
            chronoPointerInsideCached = inside;
            if (panelDragHandler.IsNotNull())
                panelDragHandler.SetChronometerPointerInside(inside);
        }

        // Cursor application lives in ChronometerCursorBackend (extracted);
        // the request flags and the pointer-forward stay here.
        private void RefreshCursor()
        {
            cursorBackend.Apply(reqScaleHover, reqMoveHover, scaleSubsystem.Expanded,
                cursorManager, panelDragHandler);
        }


        internal RectTransform GetDragAreaRect()
        {
            return chronometerDragContainer;
        }

        internal bool IsChronometerExpanded => scaleSubsystem.Expanded;

        private bool mapDragActive;

        internal void OnMapDragBegin()
        {
            if (!scaleSubsystem.Expanded || chronometerRaised)
            {
                scaleSubsystem.CancelMapDrag();
                return;
            }
            mapDragActive = true;
            CancelNativeDrag();
            // Mirror native HandleDialDragging: the GameInput blockade keeps
            // the map pan and other input consumers from eating the same
            // left-drag gesture while we move the expanded face.
            AcquireInputBlockade();
        }

        internal void OnMapDragDelta(Vector2 delta)
        {
            if (!mapDragActive)
                return;
            if (!scaleSubsystem.Expanded || chronometerRaised)
            {
                EndMapDrag();
                return;
            }
            // Native HandleDialDragging would fight us for the same gesture.
            CancelNativeDrag();
            chronometerDragContainer.position += new Vector3(delta.x, delta.y, 0f);
            UpdateDefaultPositionForRadioParent();
            if (Time.frameCount % 30 == 0)
                PersistExpandedChronometerPosition();
        }

        internal void OnMapDragEnd()
        {
            EndMapDrag();
        }

        private void EndMapDrag()
        {
            bool wasActive = mapDragActive;
            mapDragActive = false;

            if (wasActive)
            {
                CancelNativeDrag();
                PersistExpandedChronometerPosition();
                ReleaseInputBlockade();
            }

            scaleSubsystem.CancelMapDrag();
        }

        private void AcquireInputBlockade()
        {
            if (gameInput.IsNull() && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput.IsNotNull())
                gameInput.AddBlockade(this);
        }

        private void ReleaseInputBlockade()
        {
            if (gameInput.IsNull() && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput.IsNotNull())
                gameInput.RemoveBlockade(this);
        }

        // Expanded drag bypasses native HandleDialDragging (its fixed-radius
        // circle cannot reach the scaled face), so the position override is
        // written via ShipControlUIBridge exactly like the game does:
        // normalized screen coords plus its own edge clamping.
        private void PersistExpandedChronometerPosition()
        {
            if (chronometerDragContainer.IsNull())
                return;
            ShipControlUIBridge.PersistExpandedPosition(chronometerDragContainer);
        }

        internal void ToggleChronometerScale()
        {
            // Expanded changed -> the policy re-emits reduce/expand arrows on
            // this refresh even though hover state did not move.
            if (scaleSubsystem.Toggle(chronometerDragContainer))
                RefreshCursor();
        }

        // Scale snapshot/restore lives in ChronometerScaleSubsystem
        // (extracted); Raise/Restore call it directly.

        // Single reaction point for scale input: the sensor (satellite)
        // reports clicks, this decides what a click means.
        internal void OnScaleClicked()
        {
            CancelNativeDrag();
            ToggleChronometerScale();
        }


        private void CaptureDefaultPosition()
        {
            if (defaultPositionCaptured || !ShipControlUIBridge.HasDefaultPosition)
                return;

            Vector2? value = ShipControlUIBridge.GetDefaultPosition(shipControlUI);
            if (value == null)
                return;
            originalDefaultPosition = value.Value;
            defaultPositionCaptured = true;
        }

        private void UpdateDefaultPositionForRadioParent()
        {
            if (!defaultPositionCaptured || !ShipControlUIBridge.HasDefaultPosition)
                return;

            Vector3 offset = chronometerDragContainer.position - transform.position;
            ShipControlUIBridge.SetDefaultPosition(shipControlUI, new Vector2(offset.x, offset.y));
        }

        private void RestoreDefaultPosition()
        {
            if (!defaultPositionCaptured || !ShipControlUIBridge.HasDefaultPosition)
                return;

            ShipControlUIBridge.SetDefaultPosition(shipControlUI, originalDefaultPosition);
            originalDefaultPosition = Vector2.zero;
            defaultPositionCaptured = false;
        }

        public bool IsChronometerElement(Transform target)
        {
            return chronometerDragContainer.IsNotNull()
                && target.IsNotNull()
                && (target == chronometerDragContainer || target.IsChildOf(chronometerDragContainer));
        }

        internal void BeginLocalDrag(PointerEventData eventData)
        {
            if (!chronometerRaised || !IsRadioVisible || eventData == null)
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
            CancelNativeDrag();
        }

        internal void DragLocal(PointerEventData eventData)
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
            UpdateDefaultPositionForRadioParent();
            CancelNativeDrag();
        }

        internal void EndLocalDrag()
        {
            // The shield receives OnEndDrag for every drag over the container
            // even when our Begin gates rejected it - only real drags matter.
            if (!localDragActive)
                return;
            localDragActive = false;
            CancelNativeDrag();
        }

        private void CancelNativeDrag()
        {
            if (shipControlUI.IsNotNull())
                ShipControlUIBridge.ResetNativeDragState(shipControlUI);

            if (gameInput.IsNull() && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput.IsNotNull() && shipControlUI.IsNotNull())
                gameInput.RemoveBlockade(shipControlUI);
        }

        private void MoveRadioBehindNativeUi()
        {
            if (radioMovedBehind || transform.parent.IsNull())
                return;

            originalRadioSiblingIndex = transform.GetSiblingIndex();
            transform.SetAsFirstSibling();
            radioMovedBehind = true;
        }

        private void RestoreRadioSibling()
        {
            if (!radioMovedBehind || transform.parent.IsNull())
                return;

            int maxIndex = transform.parent.childCount - 1;
            transform.SetSiblingIndex(Mathf.Clamp(originalRadioSiblingIndex, 0, maxIndex));
            originalRadioSiblingIndex = -1;
            radioMovedBehind = false;
        }

        private void LogResolveWarning(string message)
        {
            if (resolveWarningLogged)
                return;

            resolveWarningLogged = true;
            Debug.LogWarning("[RadioNav][Chronometer] " + message);
        }

        private void OnDestroy()
        {
            EndLocalDrag();

            if (chronometerRaised)
                RestoreChronometerLayer();

            if (createdCanvas && chronometerCanvas.IsNotNull())
                Destroy(chronometerCanvas);

            if (createdRaycaster && chronometerRaycaster.IsNotNull())
                Destroy(chronometerRaycaster);

            if (createdDragShield && chronometerDragShield.IsNotNull())
                Destroy(chronometerDragShield);

            scaleSubsystem.ReleaseControl();
            cursorBackend.Destroy();
        }
    }
}
