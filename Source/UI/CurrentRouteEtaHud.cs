using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public sealed class CurrentRouteEtaHud : MonoBehaviour
    {
        private GameUI gameUi;
        private Text label;
        private float nextUpdate;
        private int shownMinutes;
        private bool russianGlyphs;

        internal void Refresh()
        {
            if (Time.unscaledTime < nextUpdate)
                return;
            nextUpdate = Time.unscaledTime + 0.25f;
            if (!gameUi)
                gameUi = InjectionFramework.Instance?.GetInstance<GameUI>();
            int minutes = InNavigationView() ? CurrentRouteRuntime.HudMinutes(CurrentRuntimeAdapter.BoundShip) : 0;
            if (!label && minutes > 0)
                label = BuildLabel();
            if (!label)
                return;
            label.enabled = minutes > 0;
            if (minutes > 0 && shownMinutes != minutes)
                label.text = russianGlyphs
                    ? "До точки ~" + minutes + " мин" : "ETA ~" + minutes + " min";
            shownMinutes = minutes;
        }

        private bool InNavigationView()
        {
            var camera = MainCamera.Instance;
            if (!camera || !gameUi)
                return false;
            var mode = gameUi.CurrentMode;
            if (camera.CurrentMode == CameraMode.Map)
                return mode == GameUI.Mode.Map;
            if (camera.CurrentMode == CameraMode.SectionView)
                return mode == GameUI.Mode.Normal;
            return camera.CurrentMode == CameraMode.FPP && (mode == GameUI.Mode.FPP || mode == GameUI.Mode.FPPEx);
        }

        private Text BuildLabel()
        {
            var root = new GameObject("RadioNavRouteEta", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var text = UIElementFactory.CreateLabel(root.transform, "ETA", "");
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -82f);
            rect.sizeDelta = new Vector2(260f, 34f);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 21;
            text.color = new Color(1f, 0.93f, 0.76f, 1f);
            text.raycastTarget = false;
            russianGlyphs = HasRussianGlyphs(text.font);
            return text;
        }

        private static bool HasRussianGlyphs(Font font)
        {
            if (!font)
                return false;
            foreach (char letter in "Доточкимн")
                if (!font.HasCharacter(letter))
                    return false;
            return true;
        }
    }
}
