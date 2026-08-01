using System;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class RadioHandler : BlockInputUI
    {
        public static RadioHandler Instance;
        public static event Action<bool> OnVisibilityChanged;
        public static event Action<float, int> OnFrequencyChanged;

        public event Action<float> OnCursorAngleChanged;

        [SerializeField] private CanvasGroup canvasGroup;

        private SonneController sonneController;
        private ZoneHandler zoneHandler;
        private float currentAngle;
        private int currentZoneIndex;

        private float cursorAngle;
        public float CursorAngle
        {
            get => cursorAngle;
            set
            {
                if (cursorAngle == value)
                    return;
                cursorAngle = value;
                if (OnCursorAngleChanged.IsNotNull())
                    OnCursorAngleChanged(value);
                EmitFrequency();
            }
        }

        [SerializeField]
        private bool isVisible;
        public bool IsVisible
        {
            get => isVisible;
            set
            {
                isVisible = value;
                if (canvasGroup.IsNull())
                {
                    Debug.LogError("[RadioNav][RadioHandler] canvasGroup is null, cannot set visibility");
                    return;
                }
                canvasGroup.alpha = value ? 1f : 0f;
                canvasGroup.interactable = value;
                canvasGroup.blocksRaycasts = value;
                if (OnVisibilityChanged.IsNotNull())
                    OnVisibilityChanged(value);
            }
        }

        private void Awake()
        {
            if (Instance.IsNotNull() && Instance != this)
            {
                Debug.LogWarning("[RadioNav][RadioHandler] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (zoneHandler.IsNotNull())
                zoneHandler.OnZoneChanged -= OnZoneChanged;
        }

        private void Start()
        {
            if (canvasGroup.IsNull())
                canvasGroup = GetComponent<CanvasGroup>();

            sonneController = GetComponent<SonneController>();
            zoneHandler = GetComponentInChildren<ZoneHandler>(true);

            if (zoneHandler.IsNotNull())
                zoneHandler.OnZoneChanged += OnZoneChanged;

            IsVisible = false;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || !IsVisible)
                return;

            if (UnityEngine.EventSystems.EventSystem.current.IsNotNull() &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.IsNotNull())
                return;

            IsVisible = false;
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
        }

        private void OnZoneChanged(int index)
        {
            currentZoneIndex = index;
            EmitFrequency();
        }

        private void EmitFrequency()
        {
            currentAngle = cursorAngle + 90f;
            float freq = DialConfig.AngleToFrequency(currentAngle, currentZoneIndex);
            if (OnFrequencyChanged.IsNotNull())
                OnFrequencyChanged(freq, currentZoneIndex);
        }

        public void NotifyCursorChanged()
        {
            EmitFrequency();
        }
    }
}
