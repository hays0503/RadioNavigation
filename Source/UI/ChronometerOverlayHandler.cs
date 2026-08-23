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
        private readonly ChronometerCursorPolicy cursorPolicy = new ChronometerCursorPolicy();
        private bool reqScaleHover;
        private bool reqMoveValid;
        private bool reqMoveHover;
        private bool chronoPointerInsideCached;
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

        // Single application point: the pure policy decides, this translates
        // the command to the available backend. GameDefault always goes
        // through ApplyGameDefaultCursor - foreign UnsetCustomCursor calls
        // desync the manager's mode and its guarded SetDefaultCursor would
        // early-out, leaving a custom texture stuck on screen.
        private void RefreshCursor()
        {
            var cmd = cursorPolicy.Evaluate(reqScaleHover, reqMoveHover, chronometerExpanded);
            switch (cmd)
            {
                case ChronometerCursorCommand.ArrowsExpand:
                case ChronometerCursorCommand.ArrowsReduce:
                    EnsureScaleCursors();
                    Texture2D arrows = cmd == ChronometerCursorCommand.ArrowsReduce
                        ? reduceCursor
                        : expandCursor;
                    if (cursorManager.IsNotNull())
                        cursorManager.SetCustomCursor(arrows, ScaleCursorHotspot);
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.SetCursorOverride(arrows, ScaleCursorHotspot);
                    else
                        Cursor.SetCursor(arrows, ScaleCursorHotspot, CursorMode.Auto);
                    break;

                case ChronometerCursorCommand.Crosshair:
                    Texture2D moveTexture = panelDragHandler.IsNotNull()
                        ? panelDragHandler.MoveCursorTexture
                        : null;
                    Vector2 moveHotspot = panelDragHandler.IsNotNull()
                        ? panelDragHandler.MoveCursorHotspot
                        : new Vector2(16f, 16f);
                    if (moveTexture.IsNull())
                        break; // no crosshair source - keep current cursor
                    if (cursorManager.IsNotNull())
                        cursorManager.SetCustomCursor(moveTexture, moveHotspot);
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.SetCursorOverride(moveTexture, moveHotspot);
                    else
                        Cursor.SetCursor(moveTexture, moveHotspot, CursorMode.Auto);
                    break;

                case ChronometerCursorCommand.GameDefault:
                    if (cursorManager.IsNotNull())
                        ApplyGameDefaultCursor();
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.ClearCursorOverride();
                    else
                        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    break;
            }
        }

        private static FieldInfo normalCursorTexField;
        private static Texture2D gameDefaultCursor;
        private static FieldInfo userSettingsField;
        private static MemberInfo controlSettingsMember;
        private static MemberInfo uiScaleMember;

        // Temporary [ChronoDbg]: native HandleDialDragging hits a fixed
        // radius circle around the container pivot (sqrt(30625) * uiScale /
        // 1.45), not the visual rect - when we scale x2.5 most of the face
        // falls outside it. This dump captures pivot, corners and the exact
        // effective radius so the drag-zone fix can be sized from real data.
        private void DumpChronometerDragGeometry(string stage)
        {
            RectTransform t = chronometerDragContainer;
            if (t.IsNull())
                return;

            Vector2 pivotPos = t.position;
            var corners = new Vector3[4];
            t.GetWorldCorners(corners);
            string[] cornerNames = { "BL", "TL", "TR", "BR" };

            var sb = new System.Text.StringBuilder();
            sb.Append("[RadioNav][Chronometer][ChronoDbg] ").Append(stage)
              .Append(" expanded=").Append(chronometerExpanded)
              .Append(" pivot=").Append(pivotPos.x.ToString("F0")).Append(',').Append(pivotPos.y.ToString("F0"))
              .Append(" rect=").Append(t.rect.width.ToString("F0")).Append('x').Append(t.rect.height.ToString("F0"))
              .Append(" pivotUV=(").Append(t.pivot.x.ToString("F2")).Append(',').Append(t.pivot.y.ToString("F2")).Append(')')
              .Append(" lossy=").Append(t.lossyScale.x.ToString("F2"));

            float farthestCornerPx = 0f;
            for (int i = 0; i < 4; i++)
            {
                float dist = Vector2.Distance(pivotPos, corners[i]);
                if (dist > farthestCornerPx)
                    farthestCornerPx = dist;
                sb.Append(' ').Append(cornerNames[i]).Append('=').Append(dist.ToString("F0")).Append("px");
            }

            float? uiScale = ProbeUiScale();
            float scaleNum = uiScale.HasValue ? uiScale.Value / 1.45f : 1f;
            float maxRadiusPx = Mathf.Sqrt(30625f) * scaleNum;
            float coverage = Mathf.Clamp01(maxRadiusPx / Mathf.Max(1f, farthestCornerPx));
            sb.Append(" farthest=").Append(farthestCornerPx.ToString("F0")).Append("px")
              .Append(" nativeMaxR=").Append(maxRadiusPx.ToString("F0")).Append("px")
              .Append(" uiScale=").Append(uiScale.HasValue ? uiScale.Value.ToString("F2") : "?")
              .Append(" faceCoverage=").Append((coverage * 100f).ToString("F0")).Append('%');
            Debug.Log(sb.ToString());
        }

        private float? ProbeUiScale()
        {
            try
            {
                if (shipControlUI.IsNull())
                    return null;
                if (userSettingsField == null)
                    userSettingsField = typeof(ShipControlUI).GetField(
                        "userSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                object userSettings = userSettingsField?.GetValue(shipControlUI);
                if (userSettings == null)
                    return null;

                Type usType = userSettings.GetType();
                if (controlSettingsMember == null)
                {
                    var csProp = usType.GetProperty("ControlSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var csField = usType.GetField("ControlSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    controlSettingsMember = (MemberInfo)csProp ?? (MemberInfo)csField;
                    controlSettingsGetter = csProp != null
                        ? o => csProp.GetValue(o)
                        : (csField != null ? o => csField.GetValue(o) : (Func<object, object>)null);
                }
                if (controlSettingsGetter == null)
                    return null;
                object controlSettings = controlSettingsGetter(userSettings);
                if (controlSettings == null)
                    return null;

                Type csType = controlSettings.GetType();
                if (uiScaleMember == null)
                {
                    var sProp = csType.GetProperty("uiScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var sField = csType.GetField("uiScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    uiScaleMember = (MemberInfo)sProp ?? (MemberInfo)sField;
                    uiScaleGetter = sProp != null
                        ? o => (float)sProp.GetValue(o)
                        : (sField != null ? o => (float)sField.GetValue(o) : (Func<object, float>)null);
                }
                if (uiScaleGetter == null)
                    return null;
                return uiScaleGetter(controlSettings);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Chronometer][ChronoDbg] uiScale probe failed: " + e.Message);
                return null;
            }
        }

        private static Func<object, object> controlSettingsGetter;
        private static Func<object, float> uiScaleGetter;

        // Sync the manager's mode to Default AND force the texture: foreign
        // UnsetCustomCursor calls can leave the mode at Default while our
        // custom texture is still on screen, and the guarded SetDefaultCursor
        // would early-out and keep it there.
        private void ApplyGameDefaultCursor()
        {
            cursorManager.SetDefaultCursor();
            Texture2D normal = GetGameDefaultCursorTexture();
            if (normal.IsNotNull())
                Cursor.SetCursor(normal, Vector2.zero, CursorMode.Auto);
        }

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

        internal RectTransform GetDragAreaRect()
        {
            return chronometerDragContainer;
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

            DumpChronometerDragGeometry("pre-toggle");
            lastScaleToggleFrame = Time.frameCount;
            chronometerExpanded = !chronometerExpanded;
            chronometerDragContainer.localScale = chronometerExpanded
                ? attachedChronometerScale * 2.5f
                : attachedChronometerScale;
            // Expanded changed -> the policy re-emits reduce/expand arrows on
            // this refresh even though hover state did not move.
            RefreshCursor();
            DumpChronometerDragGeometry("post-toggle");
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
            SetMoveHover(false);
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
