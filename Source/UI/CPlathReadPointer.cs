using System;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Input;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Reading-only cursor, local picking and non-intercepting angle tooltip.</summary>
    public sealed class CPlathReadPointer : MonoBehaviour
    {
        sealed class NativeBackend : ICPlathPointerBackend
        {
            GameInput input;
            CursorManager cursor;
            UBOAT.Game.Scene.Selectables.SelectablesRaycaster world;
            object owner;
            bool worldSuppressed;
            public bool Available
            {
                get
                {
                    // A scene teardown must release the old cursor token before
                    // resolving a replacement GameInput on the next tick.
                    if (owner != null) return input != null && cursor != null && world != null;
                    var framework = InjectionFramework.Instance;
                    if (framework == null) return false;
                    if (input == null) input = framework.GetInstance<GameInput>();
                    if (cursor == null) cursor = framework.GetInstance<CursorManager>();
                    if (world == null) world = framework.GetInstance<UBOAT.Game.Scene.Selectables.SelectablesRaycaster>();
                    return input != null && cursor != null && world != null;
                }
            }
            public bool BlockedByOthers(object token)
            {
                if (input.HasActiveBlockadesExcept(token)) return true;
                var events = EventSystem.current;
                var selected = events != null ? events.currentSelectedGameObject : null;
                if (selected == null) return false;
                var text = selected.GetComponent<InputField>();
                var tmp = selected.GetComponent<TMPro.TMP_InputField>();
                return text != null && text.isFocused || tmp != null && tmp.isFocused;
            }
            public void Acquire(object token)
            {
                owner = token;
                cursor.AddCursorVisibilityFlag(token);
                input.AddBlockade(token);
            }
            public void Release(object token)
            {
                if (cursor != null) cursor.RemoveCursorVisibilityFlag(token);
                if (input != null) input.RemoveBlockade(token);
                owner = null;
            }
            // The world raycaster wins EventSystem sorting against any extra
            // raycaster we could add (sortingOrder precedes distance), so disable
            // it while we own the pointer. Disabled raycasters emit no hover, so
            // no activator opens its wheel and no click can reach the world.
            public void SuppressWorldPointer(bool suppress)
            {
                if (suppress)
                {
                    if (world == null || !world.enabled) return;
                    world.enabled = false; worldSuppressed = true;
                    return;
                }
                if (!worldSuppressed) return;
                worldSuppressed = false;
                if (world != null) world.enabled = true;
            }
            public void Hover(bool interactive)
            {
                if (cursor == null) return;
                // Clearing our hand cursor must not depend on a foreign blockade
                // or on game UI under the pointer: after a modal or a focus loss no
                // later retry comes, and the hand would stick.
                if (interactive && (input == null || input.HasActiveBlockadesExcept(owner))) return;
                if (interactive) cursor.SetHoverCursor(); else cursor.SetDefaultCursor();
            }
        }

        static Font tooltipFont;
        CPlathPointerLease lease;
        CPlathPointerGeometry geometry;
        GameObject boundModel, canvasObject, tooltipObject;
        RectTransform tooltipRect;
        Text label;
        CanvasScaler scaler;
        readonly CPlathPointerButton button = new CPlathPointerButton();
        bool failed;
        internal int HoveredTarget { get; private set; } = CPlathPointerGeometry.Nothing;
        internal bool CursorLeased { get { return lease != null && lease.Active; } }

        internal void Tick(bool requested, bool settled, GameObject model, Camera camera, double reading, Action<int> toggle)
        {
            if (failed) return;
            try
            {
                bool active = UpdateLease(requested, model, camera);
                bool down = Input.GetMouseButton(0);
                bool overUi = active && settled && OverGameUi();
                bool pressed = button.Advance(active, down, settled && !overUi);
                if (!active || !settled || overUi) { ClearHover(); return; }
                geometry = EnsureGeometry(model);
                geometry.RefreshSkins(reading);
                HoveredTarget = geometry.Pick(camera, Input.mousePosition, EdgePadding());
                lease.SetHover(HoveredTarget >= 0);
                ShowAngle(reading);
                if (pressed && HoveredTarget >= 0 && toggle != null) toggle(HoveredTarget);
            }
            catch (Exception e)
            {
                failed = true; Suspend();
                // Mouse interaction is optional: never suspend the working actor.
                Debug.LogError("[RadioNav][CPlath] reading mouse disabled: "+e.Message);
            }
        }

        bool UpdateLease(bool requested, GameObject model, Camera camera)
        {
            if (lease == null) lease = new CPlathPointerLease(new NativeBackend());
            return lease.Update(requested && model != null && model.activeInHierarchy && camera != null,
                Application.isFocused);
        }

        CPlathPointerGeometry EnsureGeometry(GameObject model)
        {
            if (boundModel != model || geometry == null)
            {
                if (geometry != null) geometry.Dispose();
                geometry = new CPlathPointerGeometry(model); boundModel = model;
            }
            return geometry;
        }

        static float EdgePadding()
        { return 3*Mathf.Clamp(SextantController.UiScale(), .5f, 4); }

        // Same proven Cyrillic path as `UIElementFactory`: OS Arial first, then
        // the engine's built-in fallback. Legacy Text renders Cyrillic and the
        // prime sign reliably; a null font would render nothing.
        static Font TooltipFont()
        {
            if (tooltipFont != null) return tooltipFont;
            tooltipFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
            if (tooltipFont == null)
                tooltipFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (tooltipFont == null)
                Debug.LogWarning("[RadioNav][CPlath] tooltip font unavailable");
            return tooltipFont;
        }

        void ShowAngle(double reading)
        {
            if (HoveredTarget == CPlathPointerGeometry.Micrometer)
                ShowTooltip(CPlathPointerGeometry.AngleText(reading), Input.mousePosition, EdgePadding()/3);
            else if (tooltipObject != null) tooltipObject.SetActive(false);
        }

        static bool OverGameUi()
        {
            var events = EventSystem.current;
            if (events == null) return false;
            var module = events.currentInputModule as GameInputModule;
            return module != null ? module.IsPointerOverGUI() : events.IsPointerOverGameObject();
        }

        void ClearHover()
        {
            HoveredTarget = CPlathPointerGeometry.Nothing;
            if (lease != null) lease.SetHover(false);
            if (tooltipObject != null) tooltipObject.SetActive(false);
        }

        void ShowTooltip(string text, Vector2 pointer, float scale)
        {
            if (canvasObject == null)
            {
                canvasObject = new GameObject("CPlath_ReadTooltip", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20000;
                scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var group = canvasObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
                tooltipObject = new GameObject("Angle", typeof(RectTransform), typeof(Image));
                tooltipObject.transform.SetParent(canvasObject.transform, false);
                tooltipRect = tooltipObject.GetComponent<RectTransform>();
                tooltipRect.anchorMin = tooltipRect.anchorMax = Vector2.zero; tooltipRect.pivot = new Vector2(0, 1);
                var background = tooltipObject.GetComponent<Image>();
                background.color = new Color(.035f, .05f, .035f, .96f); background.raycastTarget = false;
                var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(tooltipRect, false);
                var rect = textObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10, 5); rect.offsetMax = new Vector2(-10, -5);
                label = textObject.GetComponent<Text>(); label.raycastTarget = false;
                label.font = TooltipFont(); label.fontSize = 16;
                label.color = new Color(.94f, .91f, .8f); label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }
            scaler.scaleFactor = scale;
            canvasObject.GetComponent<Canvas>().scaleFactor = scale;
            label.text = text;
            Vector2 size = new Vector2(label.preferredWidth+20, label.preferredHeight+10);
            tooltipRect.sizeDelta = size;
            Rect rectPixels = CPlathPointerGeometry.TooltipPixels(pointer, size*scale, new Vector2(Screen.width, Screen.height), 8*scale);
            tooltipRect.sizeDelta = rectPixels.size/scale;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 10; label.resizeTextMaxSize = 16;
            tooltipRect.anchoredPosition = new Vector2(rectPixels.xMin, rectPixels.yMax)/scale;
            tooltipObject.SetActive(true);
        }

        internal void Suspend()
        {
            ClearHover();
            if (lease != null) lease.Suspend();
            button.Suspend();
        }
        void OnDisable() { Suspend(); }
        void OnApplicationFocus(bool focused) { if (!focused) Suspend(); }
        void OnDestroy()
        {
            Suspend();
            if (geometry != null) geometry.Dispose();
            if (canvasObject != null) Destroy(canvasObject);
        }
    }
}
