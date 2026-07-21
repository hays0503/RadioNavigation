using System;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    public class RadioHandler : BlockInputUI
    {
        public static RadioHandler Instance;
        public static event Action<bool> OnVisibilityChanged;
        public event Action<float> OnCursorAngleChanged;

        [SerializeField] private CanvasGroup canvasGroup;

        private float _cursorAngle;
        public float CursorAngle
        {
            get => _cursorAngle;
            set
            {
                if (_cursorAngle == value)
                    return;
                _cursorAngle = value;
                if (OnCursorAngleChanged != null)
                    OnCursorAngleChanged(value);
            }
        }

        [SerializeField]
        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                if (canvasGroup == null)
                {
                    Debug.LogError("[TestUI][RadioHandler] canvasGroup is null, cannot set visibility");
                    return;
                }
                canvasGroup.alpha = value ? 1f : 0f;
                canvasGroup.interactable = value;
                canvasGroup.blocksRaycasts = value;
                if (OnVisibilityChanged != null)
                    OnVisibilityChanged(value);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[TestUI][RadioHandler] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
            IsVisible = false;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || !IsVisible)
                return;

            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
                return;

            IsVisible = false;
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
        }
    }
}
