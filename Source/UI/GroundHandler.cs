using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public class GroundHandler : MonoBehaviour, IPointerClickHandler
    {
        public event Action<bool> OnValueChanged;

        private Image bgImage;
        private Image groundCheckmark;
        private Toggle radioToggle;
        private const float DimAlpha = 0.4f;
        private const float MaxAlpha = 1f;
        internal const float ActiveAlphaThreshold = 0.5f;

        private void Start()
        {
            Transform bgT = transform.Find(PrefabNames.Background);
            bgImage = bgT.IsNotNull() ? bgT.GetComponent<Image>() : null;
            if (bgImage.IsNull())
            {
                Debug.LogError("[RadioNav][GroundHandler] Background Image not found, disabling");
                enabled = false;
                return;
            }
            if (bgImage.IsNotNull())
            {
                Color c = bgImage.color;
                c.a = DimAlpha;
                bgImage.color = c;
            }

            if (bgT.IsNotNull())
            {
                Transform cm = bgT.Find(PrefabNames.GroundCheckmark);
                if (cm.IsNotNull())
                {
                    groundCheckmark = cm.GetComponent<Image>();
                    if (groundCheckmark.IsNotNull())
                    {
                        Color c = groundCheckmark.color;
                        c.a = 0f;
                        groundCheckmark.color = c;
                        groundCheckmark.raycastTarget = false; // F85: decorative child Image must not block clicks
                    }
                }
            }

            if (transform.parent.IsNotNull())
                radioToggle = transform.parent.Find(PrefabNames.Toggle)?.GetComponent<Toggle>();
            else
                Debug.LogError("[RadioNav][GroundHandler] transform.parent is null");
            if (radioToggle.IsNotNull())
            {
                radioToggle.isOn = false;
                radioToggle.interactable = false;
            }
            ToggleHandler.SetGroundActive(false);
        }

        // F160: exposed so InformationPanelHandler reads the handler state
        // instead of duplicating the alpha heuristic with transform.Find.
        internal bool IsGroundEffectivelyActive() =>
            bgImage.IsNotNull() && bgImage.color.a > ActiveAlphaThreshold;

        public void OnPointerClick(PointerEventData eventData)
        {
            // F148: right/middle clicks must not toggle the ground state.
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            bool isActive = IsGroundEffectivelyActive();

            if (isActive && radioToggle.IsNotNull() && radioToggle.isOn)
            {
                radioToggle.isOn = false;
                radioToggle.interactable = false;
            }

            if (bgImage.IsNotNull())
            {
                Color c = bgImage.color;
                c.a = isActive ? DimAlpha : MaxAlpha;
                bgImage.color = c;
            }

            if (groundCheckmark.IsNotNull())
            {
                Color c = groundCheckmark.color;
                c.a = isActive ? 0f : 1f;
                groundCheckmark.color = c;
            }

            if (radioToggle.IsNotNull())
                radioToggle.interactable = !isActive;

            ToggleHandler.SetGroundActive(!isActive);

            if (OnValueChanged.IsNotNull())
                OnValueChanged(!isActive);
        }
    }
}
