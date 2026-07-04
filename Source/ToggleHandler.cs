using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class ToggleHandler : MonoBehaviour, IPointerClickHandler
    {
        private Toggle toggleComp;
        private GameObject onImage;
        private GameObject offImage;
        private RectTransform toggleRect;
        private Image dialImage;
        private Color originalDialColor;

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
            toggleRect = GetComponent<RectTransform>();

            Transform bg = transform.Find("Background");
            if (bg != null)
            {
                onImage = bg.Find("on")?.gameObject;
                offImage = bg.Find("off")?.gameObject;
            }

            Transform dial = transform.parent.Find("ciferbllat");
            if (dial != null)
            {
                dialImage = dial.GetComponent<Image>();
                if (dialImage != null)
                    originalDialColor = dialImage.color;
            }

            Debug.Log("[TestUI][Toggle] Start"
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage)
                + " toggle.graphic=" + (toggleComp.graphic != null ? toggleComp.graphic.name : "null")
                + " toggle.isOn=" + toggleComp.isOn
                + " dialColor=" + (dialImage != null ? dialImage.color.ToString() : "no-dial"));

            toggleComp.isOn = false;
            toggleComp.interactable = false;
            toggleComp.onValueChanged.AddListener(OnToggleChanged);

            OnToggleChanged(false);
        }

        private string ImgState(GameObject go) => go != null ? go.activeSelf.ToString() : "null";

        public void OnPointerClick(PointerEventData eventData)
        {
            Vector3[] corners = new Vector3[4];
            toggleRect.GetWorldCorners(corners);

            Debug.Log("[TestUI][Toggle] OnPointerClick"
                + " screenPos=" + eventData.position
                + " isOn=" + toggleComp.isOn
                + " corners=" + corners[0] + " " + corners[1] + " " + corners[2] + " " + corners[3]
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage));
        }

        private void OnToggleChanged(bool isOn)
        {
            if (onImage != null)
            {
                onImage.SetActive(isOn);
                Image img = onImage.GetComponent<Image>();
                if (img != null) img.enabled = isOn;
            }
            if (offImage != null)
            {
                offImage.SetActive(!isOn);
                Image img = offImage.GetComponent<Image>();
                if (img != null) img.enabled = !isOn;
            }

            if (dialImage != null)
            {
                Color onColor = new Color32(0xFF, 0xEC, 0xB6, 0xFF);
                dialImage.color = isOn ? onColor : originalDialColor;
            }

            Debug.Log("[TestUI][Toggle] OnToggleChanged"
                + " isOn=" + isOn
                + " on=" + DebugImg(onImage)
                + " off=" + DebugImg(offImage)
                + " dialColor=" + (dialImage != null ? dialImage.color.ToString() : "no-dial"));
        }

        private void OnDestroy()
        {
            if (toggleComp != null)
                toggleComp.onValueChanged.RemoveListener(OnToggleChanged);
        }
    }
}
