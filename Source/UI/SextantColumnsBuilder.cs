using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Builds the sextant overlay text columns (§13.3): the reading column
    /// hugging the circle's left edge (angle/name/pain/filter), the sun
    /// verification column on the right edge, and the running-fix worksheet
    /// column (F-Tafel card, stacked below on narrow aspects). Extracted
    /// from SextantOverlay.Build (SRP: text columns vs lens assembly).
    /// Pure builder: fills the shared Result DTO, owns nothing.
    /// </summary>
    internal static class SextantColumnsBuilder
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const float FixCardHeightUnits = 560f;
        private const int FixCardFontSize = 11;
        private const float SunColumnWidthUnits = 440f;

        private const string PainMessage =
            "\u041D\u0415 \u041C\u041E\u0413\u0423, \u0413\u041B\u0410\u0417\u0410\u041C \u0411\u041E\u041B\u042C\u041D\u041E \u2014 " +
            "\u0423\u0421\u0422\u0410\u041D\u041E\u0412\u0418\u0422\u0415 \u0424\u0418\u041B\u042C\u0422\u0420";

        private static Font osFont;

        /// <summary>
        /// Builds the reading, sun and fix columns under canvasRoot. The fix
        /// column placement (stacked vs side-by-side) comes from
        /// SextantOverlayLayout, unit-tested offline.
        /// </summary>
        public static void BuildColumns(GameObject canvasRoot, SextantOverlay.Result result)
        {
            // Reading column hugging the circle's left edge (§13.3).
            GameObject columnGo = new GameObject("ReadingColumn");
            columnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform column = columnGo.AddComponent<RectTransform>();
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(1f, 0.5f);
            column.sizeDelta = new Vector2(SextantOverlayLayout.ColumnWidthUnits, 180f);
            column.anchoredPosition = new Vector2(
                -SextantOverlayLayout.OcularSizeUnits * 0.5f - SextantOverlayLayout.ColumnGapUnits, 0f);

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
                pr.anchoredPosition = new Vector2(0f, SextantOverlayLayout.HintYUnits + 155f);
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
            sunColumn.anchoredPosition = new Vector2(
                SextantOverlayLayout.OcularSizeUnits * 0.5f + SextantOverlayLayout.ColumnGapUnits, 0f);

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
            var placement = SextantOverlayLayout.ComputeFixColumnPlacement(
                (float)Screen.width, (float)Screen.height);
            fixColumn.anchorMin = new Vector2(0.5f, 0.5f);
            fixColumn.anchorMax = new Vector2(0.5f, 0.5f);
            fixColumn.sizeDelta = new Vector2(SextantOverlayLayout.FixColumnWidthUnits, FixCardHeightUnits);
            fixColumn.pivot = new Vector2(placement.pivotX, placement.pivotY);
            fixColumn.anchoredPosition = new Vector2(placement.anchorX, placement.anchorY);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                LogPrefix + " FIXLAYOUT stacked={0} pivot=({1:F1},{2:F1}) anchor=({3:F0},{4:F0})",
                placement.stacked, placement.pivotX, placement.pivotY,
                placement.anchorX, placement.anchorY));

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
        }

        /// <summary>
        /// Shared label factory (also used by the root hint line): legacy
        /// UI.Text with the OS Arial for reliable Cyrillic glyphs.
        /// </summary>
        internal static Text MakeLabel(GameObject canvasRoot, string labelName, int fontSize, TextAnchor anchor, Color color)
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
                    Debug.LogWarning(LogPrefix + " OS Arial unavailable - labels will be blank");
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
    }
}
