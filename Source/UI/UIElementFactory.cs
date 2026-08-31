using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Static factory for the information panel's uGUI elements (P5.7 IPH-1).
    /// Extracted from InformationPanelHandler: layout group setup, label and
    /// separator creation, plus the shared cached Arial/built-in font.
    /// </summary>
    public static class UIElementFactory
    {
        // F103: create the Arial font once and reuse it for every label
        // instead of calling Font.CreateDynamicFontFromOSFont per label.
        private static Font cachedArialFont;

        public static void EnsureLayout(Transform content)
        {
            if (content.GetComponent<VerticalLayoutGroup>().IsNull())
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(8, 8, 8, 8);
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;
            }

            if (content.GetComponent<ContentSizeFitter>().IsNull())
            {
                var csf = content.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        public static Text CreateLabel(Transform parent, string name, string defaultText)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(200, 20);

            var text = go.AddComponent<Text>();
            text.text = defaultText;
            // F173: "Arial" is an OS font name and may be missing; fall back
            // to Unity's built-in font so the panel never renders without one.
            if (cachedArialFont.IsNull())
            {
                cachedArialFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
                if (cachedArialFont.IsNull())
                {
                    UnityEngine.Debug.LogWarning("[RadioNav] OS font 'Arial' not found, falling back to built-in font");
                    cachedArialFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            text.font = cachedArialFont;
            text.fontSize = 14;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            return text;
        }

        public static void CreateSeparator(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 2);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.2f);
        }
    }
}
