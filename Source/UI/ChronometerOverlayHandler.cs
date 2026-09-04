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
        private PanelDragHandler panelDragHandler;
        // Cursor application backend lives in ChronometerCursorBackend
        // (extracted); the request flags stay here (sensor inputs).
        private readonly ChronometerCursorBackend cursorBackend = new ChronometerCursorBackend();
        // Raise/restore snapshot state machine lives in ChronometerRaiseState
        // (extracted).
        private readonly ChronometerRaiseState raiseState = new ChronometerRaiseState();
        private Canvas parentCanvas;
        private Vector2 originalDefaultPosition;
        private bool defaultPositionCaptured;
        private bool resolveWarningLogged;
        private bool previousOpenState;
        private bool hasPreviousOpenState;
        private bool nativeChangedThisFrame;
        // Expand/scale subsystem lives in ChronometerScaleSubsystem (extracted);
        // drag mechanics live in ChronometerDragProtocol (extracted).
        private readonly ChronometerScaleSubsystem scaleSubsystem = new ChronometerScaleSubsystem();
        private ChronometerDragProtocol dragProtocol;
        private bool reqScaleHover;
        private bool reqMoveValid;
        private bool reqMoveHover;
        private bool chronoPointerInsideCached;
        // Expensive scene-wide fallback (Resources.FindObjectsOfTypeAll)
        // runs at most once per interval while the bridge is unresolved —
        // not every LateUpdate.
        private int lastFindFrame = -1000;
        private const int FindRetryFrames = 60;

        private void Awake()
        {
            dragProtocol = new ChronometerDragProtocol(
                () => raiseState.Raised,
                () => scaleSubsystem.Expanded,
                () => IsRadioVisible,
                UpdateDefaultPositionForRadioParent);
        }

        private void Update()
        {
            if (dragProtocol != null && dragProtocol.LocalDragActive)
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
            // map the raise state is false and it would kill the hint.
            previousOpenState = isOpen;
            hasPreviousOpenState = true;
            nativeChangedThisFrame = false;
        }

        private void SyncChronometerLayer(bool isOpen)
        {
            if (isOpen == raiseState.Raised)
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
                if (shipControlUI.IsNull()
                    && FrameThrottle.ShouldRetry(lastFindFrame, Time.frameCount, FindRetryFrames))
                {
                    lastFindFrame = Time.frameCount;
                    shipControlUI = FindShipControlUI();
                }
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

        // Raise/restore snapshot lives in ChronometerRaiseState (extracted);
        // these orchestrators add the scale snapshot and the leak fixes.

        private void RaiseChronometer()
        {
            bool wasReparented = raiseState.Reparented;
            bool raised = raiseState.Raise(chronometerDragContainer, transform, parentCanvas, this,
                () => {
                    CaptureDefaultPosition();
                    scaleSubsystem.Capture(chronometerDragContainer);
                },
                () => {
                    scaleSubsystem.CaptureAttached(chronometerDragContainer);
                    UpdateDefaultPositionForRadioParent();
                });
            if (!raised)
                return;

            // Original else-branch: the container was already reparented
            // (e.g. a raise without a restore in between) but the scale
            // snapshot is still missing.
            if (wasReparented && !scaleSubsystem.ScaleCaptured)
            {
                scaleSubsystem.Capture(chronometerDragContainer);
                scaleSubsystem.CaptureAttached(chronometerDragContainer);
            }

            scaleSubsystem.EnsureControl(chronometerDragContainer, this);
            Debug.Log("[RadioNav][Chronometer] native F7 chronometer attached to radio and raised");
        }

        private void RestoreChronometerLayer()
        {
            EndLocalDrag();
            scaleSubsystem.Reset(chronometerDragContainer, this);

            raiseState.Restore(chronometerDragContainer, transform,
                scaleSubsystem.OriginalScale, RestoreDefaultPosition);

            scaleSubsystem.ClearSnapshot();

            // CH-3 leak fix: the mod-owned sensor must not survive inside the
            // native hierarchy after restore. Destroy it so the next raise
            // recreates a fresh, correctly-parented control.
            scaleSubsystem.ReleaseControl();
        }


        internal void SetScaleHover(bool value)
        {
            if (dragProtocol != null && dragProtocol.LocalDragActive)
                return;
            reqScaleHover = value;
            RefreshCursor();
            ForwardChronometerPointerInside();
        }

        internal void SetMoveHover(bool moveHovered)
        {
            if (dragProtocol != null && dragProtocol.LocalDragActive)
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

        // Map-drag path and the input blockade live in
        // ChronometerDragProtocol (extracted); these wrappers keep the
        // satellite call sites.

        internal void OnMapDragBegin()
        {
            dragProtocol.OnMapDragBegin(this, scaleSubsystem, CancelNativeDrag);
        }

        internal void OnMapDragDelta(Vector2 delta)
        {
            dragProtocol.OnMapDragDelta(chronometerDragContainer, delta, scaleSubsystem,
                CancelNativeDrag, PersistExpandedChronometerPosition);
        }

        internal void OnMapDragEnd()
        {
            dragProtocol.OnMapDragEnd(scaleSubsystem, CancelNativeDrag,
                PersistExpandedChronometerPosition);
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

        // Local (raised) drag lives in ChronometerDragProtocol (extracted);
        // the native-drag cancel runs from Update while the drag is active.

        internal void BeginLocalDrag(PointerEventData eventData)
        {
            dragProtocol.BeginLocalDrag(chronometerDragContainer, eventData);
        }

        internal void DragLocal(PointerEventData eventData)
        {
            dragProtocol.DragLocal(chronometerDragContainer, eventData);
        }

        internal void EndLocalDrag()
        {
            dragProtocol.EndLocalDrag();
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

        // Radio-behind-native reordering lives in ChronometerRaiseState
        // (extracted); Raise/Restore call it internally.

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

            if (raiseState.Raised)
                RestoreChronometerLayer();

            raiseState.DestroyCreated();

            scaleSubsystem.ReleaseControl();
            cursorBackend.Destroy();
        }
    }
}
