using System;
using System.Reflection;
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
        private static readonly FieldInfo ChronometerDragField = typeof(ShipControlUI).GetField(
            "chronometerToolDragContainer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ChronometerButtonField = typeof(ShipControlUI).GetField(
            "chronometerButton", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ChronometerDefaultPositionField = typeof(ShipControlUI).GetField(
            "chronometerToolDefaultPos", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ChronometerDragStateField = typeof(ShipControlUI).GetField(
            "chronometerToolDragState", BindingFlags.Instance | BindingFlags.NonPublic);

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
        private ChronometerScaleControl chronometerScaleControl;
        private RectTransform chronometerScaleControlRect;
        private PanelDragHandler panelDragHandler;
        private Texture2D expandCursor;
        private Texture2D reduceCursor;
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
        private Vector3 originalChronometerScale;
        private Vector3 attachedChronometerScale;
        private bool scaleCaptured;
        private bool chronometerExpanded;
        private int lastScaleToggleFrame = -1;
        private bool scaleHovered;
        private bool appliedHoverValue;
        private bool appliedExpanded;
        private bool appliedHoverInitialized;
        private static readonly Vector2 ScaleCursorHotspot = new Vector2(16f, 16f);

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
                EnsureScaleControl();
                return true;
            }

            if (ChronometerDragField.IsNull())
            {
                LogResolveWarning("ShipControlUI.chronometerToolDragContainer field not found");
                return false;
            }

            chronometerDragContainer = ChronometerDragField.GetValue(shipControlUI) as RectTransform;
            if (chronometerDragContainer.IsNull())
            {
                LogResolveWarning("native chronometer drag container is not ready");
                return false;
            }

            parentCanvas = GetComponentInParent<Canvas>(true);
            ResolveChronometerButton();
            EnsureScaleControl();
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
            if (chronometerButton.IsNotNull() || ChronometerButtonField.IsNull())
                return;

            chronometerButton = ChronometerButtonField.GetValue(shipControlUI) as Toggle;
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
                CaptureChronometerScale();
                chronometerDragContainer.SetParent(transform, true);
                attachedChronometerScale = chronometerDragContainer.localScale;
                UpdateDefaultPositionForRadioParent();
                chronometerReparented = true;
            }
            else if (!scaleCaptured)
            {
                CaptureChronometerScale();
                attachedChronometerScale = chronometerDragContainer.localScale;
            }

            EnsureScaleControl();

            chronometerDragContainer.SetAsLastSibling();
            chronometerRaised = true;
            Debug.Log("[RadioNav][Chronometer] native F7 chronometer attached to radio and raised");
        }

        private void RestoreChronometerLayer()
        {
            EndLocalDrag();
            ResetChronometerScale();

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
                    chronometerDragContainer.localScale = originalChronometerScale;
                }

                RestoreDefaultPosition();

                originalParent = null;
                originalSiblingIndex = -1;
                chronometerReparented = false;
            }

            scaleCaptured = false;
            originalChronometerScale = Vector3.zero;
            attachedChronometerScale = Vector3.zero;

            if (chronometerDragShield.IsNotNull())
                chronometerDragShield.SetOwner(null);

            RestoreRadioSibling();
            chronometerRaised = false;
        }

        private void EnsureScaleControl()
        {
            if (chronometerScaleControl.IsNotNull())
                return;

            GameObject control = new GameObject(
                "ChronometerScaleControl",
                typeof(RectTransform),
                typeof(Image),
                typeof(ChronometerScaleControl));
            control.transform.SetParent(chronometerDragContainer, false);

            RectTransform controlRect = control.GetComponent<RectTransform>();
            controlRect.anchorMin = new Vector2(0.5f, 0.5f);
            controlRect.anchorMax = new Vector2(0.5f, 0.5f);
            controlRect.pivot = new Vector2(0.5f, 0.5f);
            controlRect.anchoredPosition = Vector2.zero;
            controlRect.sizeDelta = new Vector2(52f, 52f);

            Image controlImage = control.GetComponent<Image>();
            controlImage.color = new Color(0f, 0f, 0f, 0f);
            controlImage.raycastTarget = true;

            chronometerScaleControl = control.GetComponent<ChronometerScaleControl>();
            chronometerScaleControlRect = controlRect;
            chronometerScaleControl.SetOwner(this);
        }

        internal void SetScaleHover(bool value)
        {
            if (localDragActive)
                return;

            // Etalon-conform policy through the game's CursorManager: hover
            // shows the custom expand/reduce arrows as Mode.Custom, leave
            // restores the default texture via SetDefaultCursor.
            if (cursorManager.IsNotNull())
            {
                bool expandedChanged = value && appliedExpanded != chronometerExpanded;
                if (!appliedHoverInitialized || appliedHoverValue != value || expandedChanged)
                {
                    EnsureScaleCursors();
                    if (value)
                    {
                        Texture2D cursor = chronometerExpanded ? reduceCursor : expandCursor;
                        cursorManager.SetCustomCursor(cursor, ScaleCursorHotspot);
                    }
                    else
                    {
                        // UnsetCustomCursor anywhere on the map flips the
                        // manager's mode to Default without touching the
                        // texture, so SetDefaultCursor's guard early-outs and
                        // our arrows stay stuck. Sync the mode AND force the
                        // game's normal texture directly.
                        cursorManager.SetDefaultCursor();
                        Texture2D normal = GetGameDefaultCursorTexture();
                        if (normal.IsNotNull())
                            Cursor.SetCursor(normal, Vector2.zero, CursorMode.Auto);
                    }

                    appliedHoverValue = value;
                    appliedExpanded = chronometerExpanded;
                    appliedHoverInitialized = true;
                }
                scaleHovered = value;
                return;
            }

            // Fallback without the singleton: direct application plus a
            // periodic heal so foreign SetCursor calls cannot kill the hint.
            if (!appliedHoverInitialized || appliedHoverValue != value || Time.frameCount % 45 == 0)
            {
                EnsureScaleCursors();
                if (value)
                {
                    Texture2D cursor = chronometerExpanded ? reduceCursor : expandCursor;
                    if (panelDragHandler.IsNotNull())
                        panelDragHandler.SetCursorOverride(cursor, ScaleCursorHotspot);
                    else
                        Cursor.SetCursor(cursor, ScaleCursorHotspot, CursorMode.Auto);
                }
                else if (panelDragHandler.IsNotNull())
                {
                    panelDragHandler.ClearCursorOverride();
                }
                else
                {
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                }

                appliedHoverValue = value;
                appliedHoverInitialized = true;
            }
            scaleHovered = value;
        }

        private static FieldInfo normalCursorTexField;
        private static Texture2D gameDefaultCursor;

        private static Texture2D GetGameDefaultCursorTexture()
        {
            if (gameDefaultCursor.IsNotNull() || cursorManager.IsNull())
                return gameDefaultCursor;
            try
            {
                if (normalCursorTexField == null)
                    normalCursorTexField = typeof(CursorManager).GetField(
                        "normalCursorTex", BindingFlags.Instance | BindingFlags.NonPublic);
                gameDefaultCursor = normalCursorTexField?.GetValue(cursorManager) as Texture2D;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Chronometer] default cursor texture probe failed: " + e.Message);
            }
            return gameDefaultCursor;
        }

        internal void ToggleChronometerScale()
        {
            if (lastScaleToggleFrame == Time.frameCount)
                return;

            if (!scaleCaptured || chronometerDragContainer.IsNull())
            {
                if (chronometerDragContainer.IsNull())
                    return;
                CaptureChronometerScale();
                attachedChronometerScale = chronometerDragContainer.localScale;
            }

            lastScaleToggleFrame = Time.frameCount;
            chronometerExpanded = !chronometerExpanded;
            chronometerDragContainer.localScale = chronometerExpanded
                ? attachedChronometerScale * 2.5f
                : attachedChronometerScale;
            if (scaleHovered)
                SetScaleHover(true);
        }

        private void EnsureScaleCursors()
        {
            if (expandCursor.IsNotNull() && reduceCursor.IsNotNull())
                return;

            expandCursor = CreateScaleCursor(false);
            reduceCursor = CreateScaleCursor(true);
        }

        private static Texture2D CreateScaleCursor(bool inward)
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            Color32 color = new Color32(255, 255, 255, 255);
            DrawScaleArrow(pixels, 15, 15, 5, 5, inward, color);
            DrawScaleArrow(pixels, 16, 15, 26, 5, inward, color);
            DrawScaleArrow(pixels, 15, 16, 5, 26, inward, color);
            DrawScaleArrow(pixels, 16, 16, 26, 26, inward, color);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static void DrawScaleArrow(
            Color32[] pixels,
            int centerX,
            int centerY,
            int cornerX,
            int cornerY,
            bool inward,
            Color32 color)
        {
            DrawLine(pixels, centerX, centerY, cornerX, cornerY, color);

            int tipX = inward ? centerX : cornerX;
            int tipY = inward ? centerY : cornerY;
            int horizontal = cornerX < centerX ? -5 : 5;
            int vertical = cornerY < centerY ? -5 : 5;
            if (!inward)
            {
                horizontal = -horizontal;
                vertical = -vertical;
            }

            DrawLine(pixels, tipX, tipY, tipX + horizontal, tipY, color);
            DrawLine(pixels, tipX, tipY, tipX, tipY + vertical, color);
        }

        private static void DrawLine(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                if (x0 >= 0 && x0 < 32 && y0 >= 0 && y0 < 32)
                    pixels[y0 * 32 + x0] = color;
                if (x0 == x1 && y0 == y1)
                    break;

                int doubleError = 2 * error;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private void CaptureChronometerScale()
        {
            if (scaleCaptured)
                return;

            originalChronometerScale = chronometerDragContainer.localScale;
            attachedChronometerScale = originalChronometerScale;
            scaleCaptured = true;
            chronometerExpanded = false;
        }

        private void ResetChronometerScale()
        {
            if (!scaleCaptured || chronometerDragContainer.IsNull())
                return;

            chronometerDragContainer.localScale = attachedChronometerScale;
            chronometerExpanded = false;
            SetScaleHover(false);
        }

        // Single reaction point for scale input: the sensor (satellite)
        // reports clicks, this decides what a click means.
        internal void OnScaleClicked()
        {
            CancelNativeDrag();
            ToggleChronometerScale();
        }

        internal bool IsLocalDragActive => localDragActive;

        private void CaptureDefaultPosition()
        {
            if (defaultPositionCaptured || ChronometerDefaultPositionField.IsNull())
                return;

            originalDefaultPosition = (Vector2)ChronometerDefaultPositionField.GetValue(shipControlUI);
            defaultPositionCaptured = true;
        }

        private void UpdateDefaultPositionForRadioParent()
        {
            if (!defaultPositionCaptured || ChronometerDefaultPositionField.IsNull())
                return;

            Vector3 offset = chronometerDragContainer.position - transform.position;
            ChronometerDefaultPositionField.SetValue(shipControlUI, new Vector2(offset.x, offset.y));
        }

        private void RestoreDefaultPosition()
        {
            if (!defaultPositionCaptured || ChronometerDefaultPositionField.IsNull())
                return;

            ChronometerDefaultPositionField.SetValue(shipControlUI, originalDefaultPosition);
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
            if (shipControlUI.IsNotNull() && ChronometerDragStateField.IsNotNull())
            {
                object none = Enum.ToObject(ChronometerDragStateField.FieldType, 0);
                ChronometerDragStateField.SetValue(shipControlUI, none);
            }

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

            if (chronometerScaleControl.IsNotNull())
                Destroy(chronometerScaleControl);
            if (expandCursor.IsNotNull())
                Destroy(expandCursor);
            if (reduceCursor.IsNotNull())
                Destroy(reduceCursor);
        }
    }
}
