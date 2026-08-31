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

        private static volatile bool isRadioOn;
        private static bool isInteractable;
        private static bool groundActive;

        public static bool IsRadioOn
        {
            get { return isRadioOn; }
            private set { isRadioOn = value; }
        }

        public static bool IsInteractable
        {
            get { return isInteractable; }
            private set { isInteractable = value; }
        }

        public static void SetGroundActive(bool active)
        {
            groundActive = active;
            IsInteractable = IsRadioOn && active;
        }

        private Toggle toggleComp;
        private GameObject onImage;
        private GameObject offImage;
        private RectTransform toggleRect;
        private Image dialImage;
        private Color originalDialColor;

        private static readonly Color DialActiveColor = new Color32(0xFF, 0xEC, 0xB6, 0xFF);

        private void Start()
        {
            toggleComp = GetComponent<Toggle>();
            if (toggleComp.IsNull())
            {
                Debug.LogError("[RadioNav][ToggleHandler] Toggle component not found, disabling");
                enabled = false;
                return;
            }
            toggleComp.graphic = null;

            toggleRect = GetComponent<RectTransform>();
            if (toggleRect.IsNull())
            {
                Debug.LogError("[RadioNav][ToggleHandler] RectTransform not found, disabling");
                enabled = false;
                return;
            }

            Transform bg = transform.Find(PrefabNames.Background);
            if (bg.IsNotNull())
            {
                onImage = bg.Find(PrefabNames.OnImage)?.gameObject;
                offImage = bg.Find(PrefabNames.OffImage)?.gameObject;
            }

            if (transform.parent.IsNull())
            {
                Debug.LogError("[RadioNav][ToggleHandler] transform.parent is null");
                // F07: without a parent the dial can never be resolved;
                // keep the handler disabled instead of idling active.
                enabled = false;
                return;
            }
            Transform dial = transform.parent.Find(PrefabNames.DialFace);
            if (dial.IsNotNull())
            {
                dialImage = dial.GetComponent<Image>();
                if (dialImage.IsNotNull())
                    originalDialColor = dialImage.color;
            }

            Debug.Log("[RadioNav][Toggle] Start"
                + " toggle.graphic=" + (toggleComp.graphic.IsNotNull() ? toggleComp.graphic.name : "null")
                + " toggle.isOn=" + toggleComp.isOn
                + " dialColor=" + (dialImage.IsNotNull() ? dialImage.color.ToString() : "no-dial"));

            toggleComp.onValueChanged.AddListener(OnToggleChanged);

            OnToggleChanged(toggleComp.isOn);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
        }

        private void OnToggleChanged(bool isOn)
        {
            IsRadioOn = isOn;
            IsInteractable = isOn && groundActive;

            if (onImage.IsNotNull())
            {
                if (onImage.activeSelf != isOn)
                    onImage.SetActive(isOn);
            }
            if (offImage.IsNotNull())
            {
                if (offImage.activeSelf == isOn)
                    offImage.SetActive(!isOn);
            }

            if (dialImage.IsNotNull())
            {
                dialImage.color = isOn ? DialActiveColor : originalDialColor;
            }

            if (OnValueChanged.IsNotNull())
                OnValueChanged(isOn);
        }

        private void OnDestroy()
        {
            // F61: static UI state must not leak into the next scene load.
            isRadioOn = false;
            isInteractable = false;
            groundActive = false;

            if (toggleComp.IsNotNull())
                toggleComp.onValueChanged.RemoveListener(OnToggleChanged);
        }
    }
}
