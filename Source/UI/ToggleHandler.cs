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

        private Toggle toggleComp;
        private GameObject onImage;
        private GameObject offImage;
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

            // NOTE: no RectTransform guard — a GameObject carrying Toggle
            // (a Graphic) always has one; Unity adds it automatically.
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

            Graphic toggleGraphic = toggleComp.graphic;
            Debug.Log("[RadioNav][Toggle] Start"
                + " toggle.graphic=" + (toggleGraphic != null ? toggleGraphic.name : "null")
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
            RadioPowerBus.SetRadioOn(isOn);

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

            OnValueChanged?.Invoke(isOn);
        }

        private void OnDestroy()
        {
            // F61: static UI state must not leak into the next scene load.
            RadioPowerBus.Reset();

            if (toggleComp.IsNotNull())
                toggleComp.onValueChanged.RemoveListener(OnToggleChanged);
        }
    }
}
