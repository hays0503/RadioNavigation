using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    public class RadioHandler : BlockInputUI
    {
        public static RadioHandler Instance;

        [SerializeField] private CanvasGroup canvasGroup;

        [SerializeField]
        private float _cursorAngle;
        public float CursorAngle
        {
            get => _cursorAngle;
            set
            {
                if (_cursorAngle == value)
                    return;
                _cursorAngle = value;
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
                canvasGroup.alpha = value ? 1f : 0f;
                canvasGroup.interactable = value;
                canvasGroup.blocksRaycasts = value;
            }
        }

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        private void Start()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
            IsVisible = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && IsVisible)
                IsVisible = false;
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
        }
    }
}
