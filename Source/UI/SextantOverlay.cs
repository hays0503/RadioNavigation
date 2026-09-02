using System;
using System.Collections.Generic;
using System.Globalization;
using DWS.Common.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Builds the sextant overlay UI (SEXTANT_PLAN §13.3): dark veil, eyecup
    /// shade, the circular ocular (Mask) with the two PIP RawImage halves,
    /// sight lines, shade-filter dimmers, glare flash, edge ring, the
    /// reading/sun/worksheet columns and the bottom hint. Resolution-
    /// independent: own canvas + CanvasScaler (1920x1080 reference, match
    /// height). Extracted from SextantController (SRP: UI construction is
    /// not device logic). Pure builder: creates everything once and hands
    /// the references back; the controller owns the lifetime.
    /// </summary>
    internal static class SextantOverlay
    {
        // §13.3 layout constants (1080-space units).
        private const float OcularSizeUnits = 600f;
        private const float VeilAlpha = 0.85f;
        private const float ColumnGapUnits = 40f;
        private const float ColumnWidthUnits = 320f;
        private const float HintYUnits = 60f;
        private const float SunColumnWidthUnits = 440f;
        private const float EyecupSizeUnits = 1000f;
        private const float FixColumnWidthUnits = 380f;
        private const float FixCardHeightUnits = 560f;
        private const int FixCardFontSize = 11;

        private const string PainMessage =
            "\u041D\u0415 \u041C\u041E\u0413\u0423, \u0413\u041B\u0410\u0417\u0410\u041C \u0411\u041E\u041B\u042C\u041D\u041E \u2014 " +
            "\u0423\u0421\u0422\u0410\u041D\u041E\u0412\u0418\u0422\u0415 \u0424\u0418\u041B\u042C\u0422\u0420";
        private const string HintText =
            "\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0443\u0433\u043E\u043B \u00B7 Shift: \u0431\u044B\u0441\u0442\u0440\u0435\u0435 \u00B7 " +
            "Ctrl+\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0442\u043E\u0447\u043D\u043E (0,2\u2032) \u00B7 " +
            "1\u20133: \u0444\u0438\u043B\u044C\u0442\u0440 \u043F\u0440\u0430\u0432\u044B\u0439 \u00B7 Ctrl+1\u20133: \u043B\u0435\u0432\u044B\u0439 \u00B7 " +
            "0: \u0441\u043D\u044F\u0442\u044C \u00B7 R: 0\u00B0 \u00B7 +/\u2212: \u0437\u0443\u043C \u00B7 " +
            "I: \u0437\u0430\u043C\u0435\u0440 \u00B7 O: \u0441\u0435\u0440\u0438\u044F \u00B7 Z: \u0432\u044B\u0445\u043E\u0434";

        private static Font osFont;

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

            // Veil: solid dark layer, no cut-out (§13.3 - aspect-safe).
            GameObject veilGo = new GameObject("Veil", typeof(Image));
            veilGo.transform.SetParent(canvasRoot.transform, false);
            Image veil = veilGo.GetComponent<Image>();
            veil.color = new Color(0f, 0f, 0f, VeilAlpha);
            Stretch(veil.rectTransform);

            // Eyecup (наглазник): black shade larger than the ocular, drawn
            // between the veil and the lens so the world around the field of
            // view sinks into near-black while instrument texts (later
            // siblings) stay readable.
            GameObject eyecupGo = new GameObject("Eyecup", typeof(Image));
            eyecupGo.transform.SetParent(canvasRoot.transform, false);
            Image eyecup = eyecupGo.GetComponent<Image>();
            result.EyecupTexture = SextantSprites.CreateEyecupTexture();
            eyecup.sprite = SextantSprites.CreateEyecupSprite(result.EyecupTexture);
            eyecup.raycastTarget = false;
            AnchorCenter(eyecup.rectTransform, EyecupSizeUnits, EyecupSizeUnits);

            // Ocular: square container clipped to a circle (in-game finding
            // 2026-08-26: unclipped corners painted opaque black over the live
            // horizon). Mask clips both RT views, glare and ring to the circle;
            // outside it the veil shows through.
            GameObject pipGo = new GameObject("Ocular", typeof(Image), typeof(Mask));
            pipGo.transform.SetParent(canvasRoot.transform, false);
            result.PipRect = pipGo.GetComponent<RectTransform>();
            AnchorCenter(result.PipRect, OcularSizeUnits, OcularSizeUnits);
            Image maskGraphic = pipGo.GetComponent<Image>();
            result.CircleMaskTexture = SextantSprites.CreateCircleMaskTexture();
            maskGraphic.sprite = SextantSprites.CreateCircleMaskSprite(result.CircleMaskTexture);
            maskGraphic.raycastTarget = false;
            Mask ocularMask = pipGo.GetComponent<Mask>();
            ocularMask.showMaskGraphic = false;

            // Opaque black plate under the PIP views: hides the world behind
            // the lenses regardless of the RT alpha left by the game's post
            // stack. Clipped to the circle by the Mask; the veil outside
            // stays unchanged (vignette).
            GameObject backingGo = new GameObject("LensBacking", typeof(Image));
            backingGo.transform.SetParent(pipGo.transform, false);
            Image backing = backingGo.GetComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 1f);
            backing.raycastTarget = false;
            Stretch(backing.rectTransform);

            // Left half: fixed-horizon camera view (pitch 0). Shows only the
            // left half of its RT so the optical axis sits at the split line
            // (screen center); the right half of the frame is not drawn.
            GameObject leftGo = new GameObject("LeftView", typeof(RawImage));
            leftGo.transform.SetParent(pipGo.transform, false);
            result.LeftViewImage = leftGo.GetComponent<RawImage>();
            result.LeftViewImage.raycastTarget = false;
            result.LeftViewImage.texture = leftRt;
            result.LeftViewImage.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.LeftViewImage.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            result.LeftViewImage.rectTransform.offsetMin = Vector2.zero;
            result.LeftViewImage.rectTransform.offsetMax = Vector2.zero;
            result.LeftViewImage.uvRect = new Rect(0f, 0f, 0.5f, 1f);

            // Right half: mirror camera pitched to the arc angle (0-130).
            // Right half of its RT only; at arc=0 the halves form one
            // continuous circling picture.
            GameObject rightGo = new GameObject("RightView", typeof(RawImage));
            rightGo.transform.SetParent(pipGo.transform, false);
            result.RightViewImage = rightGo.GetComponent<RawImage>();
            result.RightViewImage.raycastTarget = false;
            result.RightViewImage.texture = rightRt;
            result.RightViewImage.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            result.RightViewImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.RightViewImage.rectTransform.offsetMin = Vector2.zero;
            result.RightViewImage.rectTransform.offsetMax = Vector2.zero;
            result.RightViewImage.uvRect = new Rect(0.5f, 0f, 0.5f, 1f);

            // Opaque view material (bundle shader RadioNav/UIOpaque, Blend
            // One Zero): the game's post stack writes alpha < 1 into the
            // RTs and the stock UI shader blends the black lens backing
            // through, darkening the picture.
            result.OpaqueViewMaterial = ResolveOpaqueViewMaterial(resourceManager, assetContext);
            if (result.OpaqueViewMaterial != null)
            {
                result.LeftViewImage.material = result.OpaqueViewMaterial;
                result.RightViewImage.material = result.OpaqueViewMaterial;
            }

            // Vertical sight line (1 physical pixel — drum alignment hairline
            // between the direct/mirror halves).
            GameObject splitGo = new GameObject("VerticalSplit", typeof(Image));
            splitGo.transform.SetParent(pipGo.transform, false);
            Image splitImage = splitGo.GetComponent<Image>();
            splitImage.color = new Color(1f, 1f, 1f, 0.5f);
            splitImage.raycastTarget = false;
            AnchorCenter(splitImage.rectTransform, 1f / canvas.scaleFactor, OcularSizeUnits);

            // Horizontal sight line (1 physical pixel, crosshair) — line of
            // sight, drawn above the views/below filter dims so it stays
            // subtle and never interferes with the lenses.
            GameObject horizGo = new GameObject("HorizontalSight", typeof(Image));
            horizGo.transform.SetParent(pipGo.transform, false);
            Image horizImage = horizGo.GetComponent<Image>();
            horizImage.color = new Color(1f, 1f, 1f, 0.5f);
            horizImage.raycastTarget = false;
            AnchorCenter(horizImage.rectTransform, OcularSizeUnits, 1f / canvas.scaleFactor);

            // Shade filters dim the scope view (v1.5): separate left (horizon)
            // and right (sun) half dimmers - like the separate shade glasses
            // of a real sextant.
            GameObject dimLeftGo = new GameObject("FilterDimLeft", typeof(Image));
            dimLeftGo.transform.SetParent(pipGo.transform, false);
            result.FilterDimLeftImage = dimLeftGo.GetComponent<Image>();
            result.FilterDimLeftImage.color = new Color(0f, 0f, 0f, 0f);
            result.FilterDimLeftImage.raycastTarget = false;
            RectTransform dimLeftRt = result.FilterDimLeftImage.rectTransform;
            dimLeftRt.anchorMin = new Vector2(0f, 0f);
            dimLeftRt.anchorMax = new Vector2(0.5f, 1f);
            dimLeftRt.offsetMin = Vector2.zero;
            dimLeftRt.offsetMax = Vector2.zero;

            GameObject dimRightGo = new GameObject("FilterDimRight", typeof(Image));
            dimRightGo.transform.SetParent(pipGo.transform, false);
            result.FilterDimRightImage = dimRightGo.GetComponent<Image>();
            result.FilterDimRightImage.color = new Color(0f, 0f, 0f, 0f);
            result.FilterDimRightImage.raycastTarget = false;
            RectTransform dimRightRt = result.FilterDimRightImage.rectTransform;
            dimRightRt.anchorMin = new Vector2(0.5f, 0f);
            dimRightRt.anchorMax = new Vector2(1f, 1f);
            dimRightRt.offsetMin = Vector2.zero;
            dimRightRt.offsetMax = Vector2.zero;

            // One-shot placement proof: screen-space corners of both dim
            // halves (matches the FILTERSIDE logs when a side is pressed).
            try
            {
                Vector3[] corners = new Vector3[4];
                result.FilterDimLeftImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tl = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tl[i] = corners[i];
                result.FilterDimRightImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tr = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tr[i] = corners[i];
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[RadioNav][Sextant] DIMRECT left=({0:F0},{1:F0})-({2:F0},{3:F0}) right=({4:F0},{5:F0})-({6:F0},{7:F0}) screen={8}x{9}",
                    tl[0].x, tl[0].y, tl[2].x, tl[2].y,
                    tr[0].x, tr[0].y, tr[2].x, tr[2].y, Screen.width, Screen.height));
            }
            catch { }

            // Glare flash inside the ocular only.
            GameObject glareGo = new GameObject("Glare", typeof(Image));
            glareGo.transform.SetParent(pipGo.transform, false);
            result.GlareImage = glareGo.GetComponent<Image>();
            result.GlareImage.color = new Color(1f, 1f, 1f, 0f);
            result.GlareImage.raycastTarget = false;
            Stretch(result.GlareImage.rectTransform);

            // Soft radial edge blending the ocular rim into the veil.
            GameObject ringGo = new GameObject("EdgeRing", typeof(Image));
            ringGo.transform.SetParent(pipGo.transform, false);
            Image ring = ringGo.GetComponent<Image>();
            result.RingTexture = SextantSprites.CreateRingTexture();
            ring.sprite = SextantSprites.CreateRingSprite(result.RingTexture);
            ring.raycastTarget = false;
            Stretch(ring.rectTransform);

            // Reading column hugging the circle's left edge (§13.3).
            GameObject columnGo = new GameObject("ReadingColumn");
            columnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform column = columnGo.AddComponent<RectTransform>();
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(1f, 0.5f);
            column.sizeDelta = new Vector2(ColumnWidthUnits, 180f);
            column.anchoredPosition = new Vector2(-OcularSizeUnits * 0.5f - ColumnGapUnits, 0f);

            result.AngleText = MakeLabel(canvasRoot, "AngleText", 22, TextAnchor.MiddleRight, Color.white);
            SetChild(column, result.AngleText, 160f);
            result.NameText = MakeLabel(canvasRoot, "NameText", 16, TextAnchor.MiddleRight, new Color(0.75f, 0.9f, 1f));
            SetChild(column, result.NameText, 116f);
            result.PainText = MakeLabel(canvasRoot, "PainText", 14, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.35f));
            result.PainText.text = PainMessage;
            result.PainText.enabled = false;
            // Pain warning is long; a wide centered line above the hint
            // fits it fully (the narrow left column used to clip the left
            // edge off-screen with Overflow). Must sit ABOVE the button row
            // (button tops end at y=193 from the bottom edge), so y=215.
            {
                RectTransform pr = result.PainText.rectTransform;
                pr.anchorMin = new Vector2(0.5f, 0f);
                pr.anchorMax = new Vector2(0.5f, 0f);
                pr.pivot = new Vector2(0.5f, 0f);
                pr.sizeDelta = new Vector2(1500f, 40f);
                pr.anchoredPosition = new Vector2(0f, HintYUnits + 155f);
            }
            result.FilterText = MakeLabel(canvasRoot, "FilterText", 14, TextAnchor.MiddleRight, new Color(0.85f, 0.85f, 0.85f));
            SetChild(column, result.FilterText, 36f);

            // Sun verification readout hugging the circle's right edge:
            // own-ephemeris altitude (reference for the arc at alignment).
            GameObject sunColumnGo = new GameObject("SunInfoColumn");
            sunColumnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform sunColumn = sunColumnGo.AddComponent<RectTransform>();
            sunColumn.anchorMin = new Vector2(0.5f, 0.5f);
            sunColumn.anchorMax = new Vector2(0.5f, 0.5f);
            sunColumn.pivot = new Vector2(0f, 0.5f);
            sunColumn.sizeDelta = new Vector2(SunColumnWidthUnits, 180f);
            sunColumn.anchoredPosition = new Vector2(OcularSizeUnits * 0.5f + ColumnGapUnits, 0f);

            result.SunAltText = MakeLabel(canvasRoot, "SunAltText", 14, TextAnchor.MiddleLeft, new Color(0.75f, 0.9f, 1f));
            SetChild(sunColumn, result.SunAltText, 160f);

            // Small-font body altitudes: Moon + nav stars above the horizon
            // (updated on a 1 s cadence, see SextantBodiesReadout).
            result.BodiesAltText = MakeLabel(canvasRoot, "BodiesAltText", 11, TextAnchor.UpperLeft, new Color(0.62f, 0.70f, 0.78f));
            SetChild(sunColumn, result.BodiesAltText, 130f);

            // Running-fix worksheet column (F-Tafel card): second column left
            // of the ocular; on narrow aspects (4:3, side zone < ~740 units)
            // it stacks below the reading column instead.
            GameObject fixColumnGo = new GameObject("FixColumn");
            fixColumnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform fixColumn = fixColumnGo.AddComponent<RectTransform>();
            float refWidthUnits = 1080f * Screen.width / Mathf.Max(1, Screen.height);
            float sideUnits = (refWidthUnits - OcularSizeUnits) * 0.5f;
            bool fixStacked = sideUnits <
                ColumnWidthUnits + FixColumnWidthUnits + 3f * ColumnGapUnits;
            fixColumn.anchorMin = new Vector2(0.5f, 0.5f);
            fixColumn.anchorMax = new Vector2(0.5f, 0.5f);
            fixColumn.sizeDelta = new Vector2(FixColumnWidthUnits, FixCardHeightUnits);
            if (fixStacked)
            {
                fixColumn.pivot = new Vector2(1f, 1f);
                fixColumn.anchoredPosition = new Vector2(
                    -OcularSizeUnits * 0.5f - ColumnGapUnits, -110f);
            }
            else
            {
                fixColumn.pivot = new Vector2(1f, 0.5f);
                fixColumn.anchoredPosition = new Vector2(
                    -OcularSizeUnits * 0.5f - ColumnGapUnits - ColumnWidthUnits - ColumnGapUnits, 0f);
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[RadioNav][Sextant] FIXLAYOUT refW={0:F0} side={1:F0} stacked={2}",
                refWidthUnits, sideUnits, fixStacked));

            result.FixCardText = MakeLabel(canvasRoot, "FixCardText", FixCardFontSize,
                TextAnchor.UpperLeft, new Color(0.82f, 0.88f, 0.82f));
            result.FixCardText.horizontalOverflow = HorizontalWrapMode.Wrap;
            result.FixCardText.text = "";
            result.FixCardText.rectTransform.SetParent(fixColumn, false);
            RectTransform fixCardRect = result.FixCardText.rectTransform;
            fixCardRect.anchorMin = Vector2.zero;
            fixCardRect.anchorMax = Vector2.one;
            fixCardRect.offsetMin = Vector2.zero;
            fixCardRect.offsetMax = Vector2.zero;

            // Bottom hint line.
            Text hint = MakeLabel(canvasRoot, "HintText", 13, TextAnchor.MiddleCenter,
                new Color(0.75f, 0.75f, 0.75f));
            hint.text = HintText;
            hint.transform.SetParent(canvasRoot.transform, false);
            RectTransform hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0.5f, 0f);
            hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1100f, 40f);
            hr.anchoredPosition = new Vector2(0f, HintYUnits);

            // No on-screen control buttons: zoom is +/- keys, arc reset is R
            // (see HintText). Mouse stays free for aiming.
            canvasRoot.SetActive(false);
            result.CanvasRoot = canvasRoot;
            Debug.Log("[RadioNav][Sextant] overlay built");
            return result;
        }

        private static Text MakeLabel(GameObject canvasRoot, string labelName, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(labelName, typeof(Text));
            go.transform.SetParent(canvasRoot.transform, false);
            Text t = go.GetComponent<Text>();
            t.font = GetOsFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        /// <summary>
        /// OS Arial for legacy UI.Text: TMP dynamic font assets did not pull
        /// Cyrillic glyphs from the OS font (in-game 2026-08-26, labels stayed
        /// boxes), while legacy Text renders them reliably. Static cache.
        /// </summary>
        private static Font GetOsFont()
        {
            if (osFont == null)
            {
                osFont = Font.CreateDynamicFontFromOSFont("Arial", 48);
                if (osFont == null)
                    Debug.LogWarning("[RadioNav][Sextant] OS Arial unavailable - labels will be blank");
            }
            return osFont;
        }

        private static void SetChild(RectTransform column, Text label, float y)
        {
            RectTransform r = label.rectTransform;
            r.SetParent(column, false);
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.offsetMin = new Vector2(0f, r.offsetMin.y);
            r.offsetMax = new Vector2(0f, r.offsetMax.y);
            r.sizeDelta = new Vector2(0f, label.fontSize * 1.8f);
            r.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>
        /// Loads the opaque view material from the mod's AssetBundle
        /// (RadioNavOpaqueView, shader RadioNav/UIOpaque). Returns null when
        /// unavailable — the views then keep the stock UI shader.
        /// </summary>
        private static Material ResolveOpaqueViewMaterial(ResourceManager resourceManager, UnityEngine.Object assetContext)
        {
            if (resourceManager == null)
            {
                Debug.LogWarning("[RadioNav][Sextant]" +
                    " resourceManager unavailable - ocular views keep the stock UI shader (alpha darkening possible)");
                return null;
            }

            Resource<Material> material =
                resourceManager.RetrieveAsset<Material>("RadioNavOpaqueView", assetContext, false);
            if (material.Valid && material.Asset != null)
                return material.Asset;

            Debug.LogWarning("[RadioNav][Sextant]" +
                " 'RadioNavOpaqueView' not found in bundle - ocular views keep the stock UI shader (alpha darkening possible)");
            return null;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void AnchorCenter(RectTransform r, float w, float h)
        {
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }
    }
}
