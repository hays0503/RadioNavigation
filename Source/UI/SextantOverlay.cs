using DWS.Common.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Builds the sextant overlay UI (SEXTANT_PLAN §13.3): own canvas with
    /// scaler, the lens assembly (SextantOcularBuilder) and the text columns
    /// (SextantColumnsBuilder). Resolution-independent: own canvas +
    /// CanvasScaler (1920x1080 reference, match height). Extracted from
    /// SextantController (SRP: UI construction is not device logic). Pure
    /// orchestrator: creates the root, delegates the two halves, hands the
    /// references back; the controller owns the lifetime. The key list lives
    /// in the help panel (P) since 2026-11-04 — the old bottom hint line was
    /// overlapped by the game's own bottom UI.
    /// </summary>
    internal static class SextantOverlay
    {

        /// <summary>All references the controller needs after the build.</summary>
        public sealed class Result
        {
            public GameObject CanvasRoot;
            public RectTransform PipRect;
            /// <summary>Eyecup (soft outward shade). Sized from the ocular
            /// side, not absolutely — see SextantOcularBuilder.EyecupFactor.</summary>
            public RectTransform EyecupRect;
            public RawImage LeftViewImage;
            public RawImage RightViewImage;
            /// <summary>Black lens plate behind both halves: the instrument's
            /// outer silhouette (antialiased, see SextantOcularBuilder).</summary>
            public Image LensBackingImage;
            public Image FilterDimLeftImage;
            public Image FilterDimRightImage;
            public Image GlareImage;
            // Aiming crosshair (author 2026-11-04): four 1-physical-pixel arms
            // split by the gap whose radius equals the identification cone —
            // recoloured day/night and re-sized with the cone every frame.
            public Image SplitTopImage;
            public Image SplitBottomImage;
            public Image SightLeftImage;
            public Image SightRightImage;
            /// <summary>Last applied gap radius / arm length (canvas units):
            /// re-applied when the UI scale changes
            /// (SextantOcularBuilder.ApplySightLineThickness).</summary>
            public float ConeGapRadiusUnits;
            public float ConeArmUnits;
            /// <summary>Thickness of a 1-physical-pixel line in canvas units.</summary>
            public float SightLineThicknessUnits;
            /// <summary>Live ocular side in canvas units (SextantOcularSize):
            /// the sight lines, the cone box and the debug-aim offset all
            /// derive from it (2026-11-04).</summary>
            public float OcularSizeUnits;
            public Text AngleText;
            /// <summary>Current zoom label ("x6"…"x1") above the altitude
            /// (author 2026-11-04); the controller refreshes it on open and on
            /// every zoom step.</summary>
            public Text ZoomText;
            public Text NameText;
            public Text PainText;
            public Text FilterText;
            public Text BodiesAltText;
            public Text FixCardText;
            /// <summary>Aim diagnostics readout (RadioNav.DebugAim): hidden by
            /// default, the controller enables it when the gate is on and the
            /// aim values are written elsewhere.</summary>
            public Text DebugAimText;
            /// <summary>The worksheet column rect (re-placed when the game's
            /// UI scale changes, 2026-09-22).</summary>
            public RectTransform FixColumn;
            /// <summary>Reading and sun column rects (re-placed when the ocular
            /// size changes, 2026-11-04).</summary>
            public RectTransform ReadingColumn;
            public RectTransform SunColumn;
            /// <summary>Usable card height (canvas units) and the base font
            /// size: the worksheet shrinks the font so the whole card fits
            /// (2026-09-22, FixCardLayout).</summary>
            public float FixCardAvailableUnits;
            public int FixCardBaseFontSize;
            public Texture2D RingTexture;
            public Texture2D CircleMaskTexture;
            public Texture2D EyecupTexture;
            public Texture2D OpticsGrimeTexture;
            public Texture2D OpticsMoteTexture;
            public Image OpticsGrimeImage;
            public Image[] OpticsMotes;
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
            ResourceManager resourceManager, UnityEngine.Object assetContext, float uiScale)
        {
            var result = new Result();
            if (uiScale <= 0f || float.IsNaN(uiScale))
                uiScale = 1f;

            // Ocular side: a fraction of the unit height (SextantOcularSize),
            // clamped by the unit width so the side columns keep their space.
            float ocularSize = SextantOcularSize.UnitsForScreen(Screen.width, Screen.height,
                uiScale, OcularFraction(), SextantOverlayLayout.SideColumnsUnits);

            GameObject canvasRoot = new GameObject("RadioNavSextantOverlay", typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Game convention (2026-09-22): the layout space is the screen
            // divided by the UI scale, exactly like the HUD canvases
            // (CanvasUserSettings + FullscreenRect), so the sextant matches the
            // HUD's apparent size at any resolution and UI scale.
            scaler.referenceResolution = new Vector2(Screen.width, Screen.height) / uiScale;
            scaler.matchWidthOrHeight = 1f;

            SextantOcularBuilder.BuildOcular(canvasRoot, canvas.scaleFactor,
                leftRt, rightRt, resourceManager, assetContext, result, ocularSize);
            SextantColumnsBuilder.BuildColumns(canvasRoot, result, uiScale, ocularSize);

            // Debug aim readout (RadioNav.DebugAim): centred just below the
            // aiming window; off until the controller enables it.
            Text debugAim = SextantColumnsBuilder.MakeLabel(canvasRoot, "DebugAimText", 14,
                TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.7f));
            debugAim.text = "";
            RectTransform debugAimRect = debugAim.rectTransform;
            debugAimRect.anchorMin = new Vector2(0.5f, 0.5f);
            debugAimRect.anchorMax = new Vector2(0.5f, 0.5f);
            debugAimRect.pivot = new Vector2(0.5f, 1f);
            debugAimRect.anchoredPosition = new Vector2(0f, -60f);
            debugAimRect.sizeDelta = new Vector2(600f, 30f);
            debugAim.gameObject.SetActive(false);
            result.DebugAimText = debugAim;

            // No on-screen control buttons: zoom is +/- keys, arc reset is R
            // (the full key list lives in the help panel, P). Mouse stays free
            // for aiming.
            canvasRoot.SetActive(false);
            result.CanvasRoot = canvasRoot;
            Debug.Log("[RadioNav][Sextant] overlay built");
            return result;
        }

        /// <summary>
        /// Follows the game's UI scale live (2026-09-22): PgUp/PgDn change
        /// userSettings.ControlSettings.uiScale even while the sextant is open
        /// (the game handles the scale before its input-blockade check), so the
        /// overlay must re-derive its reference resolution, its ocular size
        /// (a fraction of the unit height, 2026-11-04) and the worksheet
        /// column placement — the unit space shrinks as the scale grows, which
        /// is what the card auto-font then works against.
        /// </summary>
        public static void ApplyScale(Result result, float uiScale)
        {
            if (result == null || result.CanvasRoot == null)
                return;
            if (uiScale <= 0f || float.IsNaN(uiScale))
                uiScale = 1f;

            CanvasScaler scaler = result.CanvasRoot.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                Vector2 reference = new Vector2(Screen.width, Screen.height) / uiScale;
                if (scaler.referenceResolution != reference)
                    scaler.referenceResolution = reference;
            }

            // Ocular side follows the unit height: re-derive and re-place the
            // circle, its sight lines and the columns that hug it.
            float ocularSize = SextantOcularSize.UnitsForScreen(Screen.width, Screen.height,
                uiScale, OcularFraction(), SextantOverlayLayout.SideColumnsUnits);
            result.OcularSizeUnits = ocularSize;
            if (result.PipRect != null)
            {
                result.PipRect.sizeDelta = new Vector2(ocularSize, ocularSize);
                result.PipRect.anchoredPosition = Vector2.zero;
            }
            // The eyecup must stay a multiple of the ocular (SextantOcularBuilder.
            // EyecupFactor): a shade that no longer reaches past the rim leaves
            // the outer silhouette to the Mask's binary stencil, which is the
            // angular rim the author reported at 2560x1440.
            SextantOcularBuilder.ApplyEyecupSize(result, ocularSize);
            SextantColumnsBuilder.PlaceColumns(result, ocularSize);

            SextantOverlayLayout.FixColumnPlacement placement =
                SextantOverlayLayout.ComputeFixColumnPlacement(
                    Screen.width, Screen.height, uiScale, ocularSize);
            if (result.FixColumn != null)
            {
                result.FixColumn.sizeDelta = new Vector2(
                    SextantOverlayLayout.FixColumnWidthUnits, placement.maxHeightUnits);
                result.FixColumn.pivot = new Vector2(placement.pivotX, placement.pivotY);
                result.FixColumn.anchoredPosition = new Vector2(
                    placement.anchorX, placement.anchorY);
            }
            result.FixCardAvailableUnits = placement.maxHeightUnits;
            // The overlay units follow the scale, so the 1-physical-pixel
            // hairlines must be re-derived (1 unit = uiScale pixels).
            SextantOcularBuilder.ApplySightLineThickness(result, 1f / uiScale);
        }

        /// <summary>Ocular fraction from PlayerPrefs (RadioNav.OcularScale),
        /// clamped into the slider band; the default when unset.</summary>
        private static float OcularFraction()
        {
            return SextantOcularSize.ClampFraction(
                PlayerPrefs.GetFloat(SextantOcularSize.PrefsKey, SextantOcularSize.DefaultFraction));
        }
    }
}
