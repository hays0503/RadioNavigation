using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class DebugAngleHandler : MonoBehaviour
    {
        [SerializeField] private TMP_Text beaconText;
        [SerializeField] private Slider slider;

        private float _angle;
        private bool _isUpdating;

        public float Angle
        {
            get => _angle;
            set
            {
                value = Mathf.Clamp(value, 0f, 360f);
                if (Mathf.Approximately(_angle, value))
                    return;
                _angle = value;
                OnAngleChanged?.Invoke(_angle);
                UpdateUI();
            }
        }

        public event Action<float> OnAngleChanged;

        private void Start()
        {
            beaconText = GetComponentInChildren<TMP_Text>(true);
            slider = GetComponentInChildren<Slider>(true);

            if (beaconText == null)
                Debug.LogError("[TestUI][DebugAngle] TMP_Text not found in children");
            if (slider == null)
                Debug.LogError("[TestUI][DebugAngle] Slider not found in children");

            if (slider != null)
                slider.onValueChanged.AddListener(OnSliderChanged);

            Angle = 0f;
        }

        private void OnSliderChanged(float value)
        {
            if (_isUpdating)
                return;
            Angle = value;
        }

        private void UpdateUI()
        {
            _isUpdating = true;

            if (beaconText != null)
                beaconText.text = _angle.ToString("F1") + "\u00B0";

            if (slider != null)
                slider.value = _angle;

            _isUpdating = false;
        }

        private void OnDestroy()
        {
            if (slider != null)
                slider.onValueChanged.RemoveListener(OnSliderChanged);
        }
    }
}
