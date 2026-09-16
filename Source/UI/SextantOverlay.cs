using DWS.Common.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Builds the sextant overlay UI (SEXTANT_PLAN §13.3): own canvas with
    /// scaler, the lens assembly (SextantOcularBuilder), the text columns
    /// (SextantColumnsBuilder) and the bottom hint line. Resolution-
    /// independent: own canvas + CanvasScaler (1920x1080 reference, match
    /// height). Extracted from SextantController (SRP: UI construction is
    /// not device logic). Pure orchestrator: creates the root, delegates
    /// the two halves, hands the references back; the controller owns the
    /// lifetime.
    /// </summary>
    internal static class SextantOverlay
    {
        private const string HintText =
            "\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0443\u0433\u043E\u043B \u00B7 Shift: \u0431\u044B\u0441\u0442\u0440\u0435\u0435 \u00B7 " +
            "Ctrl+\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0442\u043E\u0447\u043D\u043E (0,2\u2032) \u00B7 " +
            "1\u20133: \u0444\u0438\u043B\u044C\u0442\u0440 \u043F\u0440\u0430\u0432\u044B\u0439 \u00B7 Ctrl+1\u20133: \u043B\u0435\u0432\u044B\u0439 \u00B7 " +
            "0: \u0441\u043D\u044F\u0442\u044C \u00B7 R: 0\u00B0 \u00B7 +/\u2212: \u0437\u0443\u043C \u00B7 " +
            "I: \u0437\u0430\u043C\u0435\u0440 \u00B7 O: \u0441\u0435\u0440\u0438\u044F \u00B7 " +
            "P: \u0441\u043F\u0440\u0430\u0432\u043A\u0430 \u00B7 Z: \u0432\u044B\u0445\u043E\u0434";

        /// <summary>All references the controller needs after the build.</summary>
        public sealed class Result
        {
            public GameObject CanvasRoot;
            public RectTransform PipRect;
            public RawImage LeftViewImage;
            public RawImage RightViewImage;
            public Image FilterDimLeftImage;
            public Image FilterDimRightImage;
            public Image GlareImage;
            public Text AngleText;
            public Text NameText;
            public Text PainText;
            public Text FilterText;
            public Text SunAltText;
            public Text BodiesAltText;
            public Text FixCardText;
            public Texture2D RingTexture;
            public Texture2D CircleMaskTexture;
            public Texture2D EyecupTexture;
            public Material OpaqueViewMaterial;
            // Sprites created with Sprite.Create are NOT owned by the canvas
            // GameObject; the controller must Destroy() them explicitly.
            public readonly System.Collections.Generic.List<Sprite> OwnedSprites =
                new System.Collections.Generic.List<Sprite>();
        }

        /// <summary>
        /// Builds the whole overlay (once). The RTs must already exist; the
        /// resourceManager may be null (views keep the stock UI shader).
        /// </summary>
        public static Result Build(RenderTexture leftRt, RenderTexture rightRt,
            ResourceManager resourceManager, UnityEngine.Object assetContext)
        {
            var result = new Result();

            GameObject canvasRoot = new GameObject("RadioNavSextantOverlay", typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            SextantOcularBuilder.BuildOcular(canvasRoot, canvas.scaleFactor,
                leftRt, rightRt, resourceManager, assetContext, result);
            SextantColumnsBuilder.BuildColumns(canvasRoot, result);

            // Bottom hint line.
            Text hint = SextantColumnsBuilder.MakeLabel(canvasRoot, "HintText", 13,
                TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f));
            hint.text = HintText;
            RectTransform hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0.5f, 0f);
            hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1100f, 40f);
            hr.anchoredPosition = new Vector2(0f, SextantOverlayLayout.HintYUnits);

            // No on-screen control buttons: zoom is +/- keys, arc reset is R
            // (see HintText). Mouse stays free for aiming.
            canvasRoot.SetActive(false);
            result.CanvasRoot = canvasRoot;
            Debug.Log("[RadioNav][Sextant] overlay built");
            return result;
        }
    }
}
