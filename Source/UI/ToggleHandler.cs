using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    [DefaultExecutionOrder(-1)]
    public class ToggleHandler : MonoBehaviour, IPointerClickHandler
    {
        public event Action<bool> OnValueChanged;

        public static bool IsRadioOn { get; private set; }
        public static bool IsInteractable { get; private set; }
        private static bool _groundActive;

        public static void SetGroundActive(bool active)
        {
            _groundActive = active;
            IsInteractable = IsRadioOn && active;
        }

        private Toggle toggleComp;
        private GameObject onImage;
        private GameObject offImage;
        private RectTransform toggleRect;
        private Image dialImage;
        private Color originalDialColor;

        private static readonly Color DialActiveColor = new Color32(0xFF, 0xEC, 0xB6, 0xFF);

        private string DebugImg(GameObject go)
        {
            if (go == null) return "null";
            Image img = go.GetComponent<Image>();
            string spriteName = (img != null && img.sprite != null) ? img.sprite.name : "no-sprite";
            float alpha = (img != null) ? img.color.a : -1f;
            bool imgEnabled = img != null && img.enabled;
            RectTransform rt = go.GetComponent<RectTransform>();
            Vector2 size = rt != null ? rt.sizeDelta : Vector2.zero;
            Vector2 anchoredPos = rt != null ? rt.anchoredPosition : Vector2.zero;
            return go.name
                + " active=" + go.activeSelf
                + " imgEnabled=" + imgEnabled
                + " sprite=" + spriteName
                + " alpha=" + alpha.ToString("F2")
                + " size=" + size
                + " anchoredPos=" + anchoredPos;
        }

        private void Start()
        {
            toggleComp = GetComponent<Toggle>();
            if (toggleComp == null)
            {
                Debug.LogError("[RadioNav][ToggleHandler] Toggle component not found, disabling");
                enabled = false;
                return;
            }
            toggleComp.graphic = null;

            toggleRect = GetComponent<RectTransform>();
            if (toggleRect == null)
            {
                Debug.LogError("[RadioNav][ToggleHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }

            Transform bg = transform.Find("Background");
            if (bg != null)
            {
                onImage = bg.Find("on")?.gameObject;
                offImage = bg.Find("off")?.gameObject;
            }

            if (transform.parent == null)
            {
                Debug.LogError("[RadioNav][ToggleHandler] transform.parent is null");
                return;
            }
            Transform dial = transform.parent.Find("ciferbllat");
            if (dial != null)
            {
                dialImage = dial.GetComponent<Image>();
                if (dialImage != null)
                    originalDialColor = dialImage.color;
            }

            Debug.Log("[RadioNav][Toggle] Start"
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage)
                + " toggle.graphic=" + (toggleComp.graphic != null ? toggleComp.graphic.name : "null")
                + " toggle.isOn=" + toggleComp.isOn
                + " dialColor=" + (dialImage != null ? dialImage.color.ToString() : "no-dial"));

            toggleComp.onValueChanged.AddListener(OnToggleChanged);

            OnToggleChanged(toggleComp.isOn);
        }

        private string ImgState(GameObject go) => go != null ? go.activeSelf.ToString() : "null";

        public void OnPointerClick(PointerEventData eventData)
        {
#if DEBUG
            Vector3[] corners = new Vector3[4];
            toggleRect.GetWorldCorners(corners);

            Debug.Log("[RadioNav][Toggle] OnPointerClick"
                + " screenPos=" + eventData.position
                + " isOn=" + toggleComp.isOn
                + " corners=" + corners[0] + " " + corners[1] + " " + corners[2] + " " + corners[3]
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage));
#endif
        }

        private void OnToggleChanged(bool isOn)
        {
            IsRadioOn = isOn;
            IsInteractable = isOn && _groundActive;

            if (onImage != null)
            {
                if (onImage.activeSelf != isOn)
                    onImage.SetActive(isOn);
            }
            if (offImage != null)
            {
                if (offImage.activeSelf == isOn)
                    offImage.SetActive(!isOn);
            }

            if (dialImage != null)
            {
                dialImage.color = isOn ? DialActiveColor : originalDialColor;
            }

            if (OnValueChanged != null)
                OnValueChanged(isOn);

#if DEBUG
            Debug.Log("[RadioNav][Toggle] OnToggleChanged"
                + " isOn=" + isOn
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage)
                + " dialColor=" + (dialImage != null ? dialImage.color.ToString() : "no-dial"));
#endif
        }

        private void OnDestroy()
        {
            if (toggleComp != null)
                toggleComp.onValueChanged.RemoveListener(OnToggleChanged);
        }
    }
}
