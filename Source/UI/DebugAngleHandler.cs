using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public class DebugAngleHandler : MonoBehaviour
    {
        [SerializeField] private TMP_Text beaconText;
        [SerializeField] private Slider slider;

        private float angle;
        private bool isUpdating;

        public float Angle
        {
            get => angle;
            set
            {
                value = Mathf.Clamp(value, 0f, 360f);
                if (Mathf.Approximately(angle, value))
                    return;
                angle = value;
                OnAngleChanged?.Invoke(angle);
                UpdateUI();
            }
        }

        public event Action<float> OnAngleChanged;

        private void Start()
        {
            beaconText = GetComponentInChildren<TMP_Text>(true);
            slider = GetComponentInChildren<Slider>(true);

            if (beaconText.IsNull())
                Debug.LogError("[RadioNav][DebugAngle] TMP_Text not found in children");
            if (slider.IsNull())
                Debug.LogError("[RadioNav][DebugAngle] Slider not found in children");

            if (slider.IsNotNull())
                slider.onValueChanged.AddListener(OnSliderChanged);

            Angle = 0f;
        }

        private void OnSliderChanged(float value)
        {
            if (isUpdating)
                return;
            Angle = value;
        }

        private void UpdateUI()
        {
            isUpdating = true;

            if (beaconText.IsNotNull())
                beaconText.text = angle.ToString("F1") + "\u00B0";

            if (slider.IsNotNull())
                slider.value = angle;

            isUpdating = false;
        }

        private void OnDestroy()
        {
            if (slider.IsNotNull())
                slider.onValueChanged.RemoveListener(OnSliderChanged);
        }
    }
}
