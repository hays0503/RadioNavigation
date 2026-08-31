using TMPro;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Procedural "CONST ON/OFF" button on the radio panel (STAR_SKY_PLAN
    /// §13.6): toggles the constellation lines layer, mirrors the state in
    /// the label, and registers a DevConsole command as a third control
    /// path. The button is built at runtime (no bundle change), anchored to
    /// the panel's top-right corner; the hotkey F9 lives in
    /// ConstellationMesh.Update.
    /// </summary>
    public sealed class ConstellationToggleButton : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Constellations]";
        private const string ConsoleCommand = "RadioNavConstellations";
        private const float ButtonWidth = 118f;
        private const float ButtonHeight = 34f;
        private const float ButtonMargin = 8f;
        private const int LabelFontSize = 22;

        private static readonly Color ColorOn = new Color(0.94f, 0.75f, 0.25f, 1f);
        private static readonly Color ColorOff = new Color(0.55f, 0.58f, 0.55f, 1f);
        private static readonly Color ColorBackground = new Color(0.02f, 0.03f, 0.04f, 0.85f);

        private TextMeshProUGUI label;

        private void Start()
        {
            BuildButton();
            DevConsole.AddListener(ConsoleCommand, HandleConsoleCommand);
            RefreshLabel();
            Debug.Log(LogPrefix + " toggle button built; console command '" + ConsoleCommand + "' registered");
        }

        private void OnDestroy()
        {
            ConstellationMesh.EnabledChanged -= OnEnabledChanged;
        }

        private void BuildButton()
        {
            var root = new GameObject("RadioNavConstellationToggle", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-ButtonMargin, -ButtonMargin);
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

            var image = root.AddComponent<Image>();
            image.color = ColorBackground;
            image.raycastTarget = true;

            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(Toggle);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(root.transform, false);
            label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = NavMapMarkerBase.GetTooltipFont();
            label.fontSize = LabelFontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        }

        private void Toggle()
        {
            ConstellationMesh.SetEnabled(!ConstellationMesh.Enabled);
        }

        private void OnEnable()
        {
            ConstellationMesh.EnabledChanged += OnEnabledChanged;
            RefreshLabel();
        }

        private void OnEnabledChanged(bool value) => RefreshLabel();

        private void RefreshLabel()
        {
            if (label == null)
                return;

            label.text = ConstellationMesh.Enabled ? "CONST ON" : "CONST OFF";
            label.color = ConstellationMesh.Enabled ? ColorOn : ColorOff;
        }

        private void HandleConsoleCommand(string[] arguments)
        {
            if (arguments != null && arguments.Length > 0)
            {
                string arg = arguments[0].ToLowerInvariant();
                if (arg == "on" || arg == "1" || arg == "true")
                {
                    ConstellationMesh.SetEnabled(true);
                    return;
                }
                if (arg == "off" || arg == "0" || arg == "false")
                {
                    ConstellationMesh.SetEnabled(false);
                    return;
                }
            }

            Toggle();
        }
    }
}
