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

        private string StateLog()
        {
            RectTransform bgRt = bgImage.IsNotNull() ? bgImage.rectTransform : null;
            RectTransform grRt = GetComponent<RectTransform>();
            GameObject bgGo = bgImage.IsNotNull() ? bgImage.gameObject : null;

            return "[Ground] active=" + gameObject.activeSelf
                + " grPos=" + (grRt.IsNotNull() ? grRt.anchoredPosition.ToString() : "no-rt")
                + " grSize=" + (grRt.IsNotNull() ? grRt.sizeDelta.ToString() : "no-rt")

                + " [Background] active=" + (bgGo.IsNotNull() ? bgGo.activeSelf.ToString() : "no-bg")
                + " imgEnabled=" + (bgImage.IsNotNull() ? bgImage.enabled.ToString() : "no-img")
                + " imgAlpha=" + (bgImage.IsNotNull() ? bgImage.color.a.ToString("F2") : "-")
                + " imgR=" + (bgImage.IsNotNull() ? bgImage.color.r.ToString("F2") : "-")
                + " imgG=" + (bgImage.IsNotNull() ? bgImage.color.g.ToString("F2") : "-")
                + " imgB=" + (bgImage.IsNotNull() ? bgImage.color.b.ToString("F2") : "-")
                + " raycast=" + (bgImage.IsNotNull() ? bgImage.raycastTarget.ToString() : "-")
                + " pos=" + (bgRt.IsNotNull() ? bgRt.anchoredPosition.ToString() : "no-rt")
                + " size=" + (bgRt.IsNotNull() ? bgRt.sizeDelta.ToString() : "no-rt")

                + " [GroundCheckmark] active=" + (groundCheckmark.IsNotNull() ? groundCheckmark.gameObject.activeSelf.ToString() : "null")
                + " imgEnabled=" + (groundCheckmark.IsNotNull() ? groundCheckmark.enabled.ToString() : "-")
                + " sprite=" + (groundCheckmark.IsNotNull() && groundCheckmark.sprite.IsNotNull() ? groundCheckmark.sprite.name : "no-sprite")
                + " alpha=" + (groundCheckmark.IsNotNull() ? groundCheckmark.color.a.ToString("F2") : "-")
                + " r=" + (groundCheckmark.IsNotNull() ? groundCheckmark.color.r.ToString("F2") : "-")
                + " g=" + (groundCheckmark.IsNotNull() ? groundCheckmark.color.g.ToString("F2") : "-")
                + " b=" + (groundCheckmark.IsNotNull() ? groundCheckmark.color.b.ToString("F2") : "-")
                + " raycast=" + (groundCheckmark.IsNotNull() ? groundCheckmark.raycastTarget.ToString() : "-")
                + " pos=" + (groundCheckmark.IsNotNull() ? groundCheckmark.rectTransform.anchoredPosition.ToString() : "-")

                + " [Toggle] found=" + (radioToggle.IsNotNull())
                + " interactable=" + (radioToggle?.interactable ?? false)
                + " isOn=" + (radioToggle?.isOn ?? false);
        }

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

#if DEBUG
            Debug.Log("[RadioNav][Ground] Start " + StateLog());
#endif
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

#if DEBUG
            Debug.Log("[RadioNav][Ground] onClick screenPos=" + eventData.position + " " + StateLog());
#endif
        }
    }
}
