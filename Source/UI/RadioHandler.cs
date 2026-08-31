using System;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class RadioHandler : BlockInputUI
    {
        public static RadioHandler Instance;
        // F90: OnVisibilityChanged was never subscribed to anywhere — removed.
        // F192: OnCursorAngleChanged was also never subscribed to — removed.
        public static event Action<float, int> OnFrequencyChanged;

        [SerializeField] private CanvasGroup canvasGroup;

        private ZoneHandler zoneHandler;
        private float currentAngle;
        private int currentZoneIndex;

        private float cursorAngle;
        public float CursorAngle
        {
            get => cursorAngle;
            set
            {
                // F147: lerp-smoothed angles may differ by tiny rounding;
                // exact float equality would spuriously re-emit or skip.
                if (Mathf.Approximately(cursorAngle, value))
                    return;
                cursorAngle = value;
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
                Debug.Log("[RadioNav][RadioHandler] visible=" + value
                    + " alpha=" + canvasGroup.alpha
                    + " active=" + gameObject.activeInHierarchy);
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

            zoneHandler = GetComponentInChildren<ZoneHandler>(true);

            if (zoneHandler.IsNotNull())
                zoneHandler.OnZoneChanged += OnZoneChanged;

            IsVisible = false;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || !IsVisible)
                return;

            var selected = UnityEngine.EventSystems.EventSystem.current.IsNotNull()
                ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
                : null;

            // F133: the cursor spinner keeps itself selected; Escape must still
            // close the panel when the selection is inside this panel.
            if (selected.IsNotNull() && !selected.transform.IsChildOf(transform))
                return;

            IsVisible = false;
        }

        private void OnZoneChanged(int index)
        {
            currentZoneIndex = index;
            EmitFrequency();
        }

        private void EmitFrequency()
        {
            currentAngle = cursorAngle + Constants.CursorOffsetDeg;
            float freq = DialConfig.AngleToFrequency(currentAngle, currentZoneIndex);
            OnFrequencyChanged?.Invoke(freq, currentZoneIndex);
        }
    }
}
