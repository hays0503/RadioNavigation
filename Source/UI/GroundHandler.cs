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
            RectTransform bgRt = bgImage != null ? bgImage.rectTransform : null;
            RectTransform grRt = GetComponent<RectTransform>();
            GameObject bgGo = bgImage != null ? bgImage.gameObject : null;

            return "[Ground] active=" + gameObject.activeSelf
                + " grPos=" + (grRt != null ? grRt.anchoredPosition.ToString() : "no-rt")
                + " grSize=" + (grRt != null ? grRt.sizeDelta.ToString() : "no-rt")

                + " [Background] active=" + (bgGo != null ? bgGo.activeSelf.ToString() : "no-bg")
                + " imgEnabled=" + (bgImage != null ? bgImage.enabled.ToString() : "no-img")
                + " imgAlpha=" + (bgImage != null ? bgImage.color.a.ToString("F2") : "-")
                + " imgR=" + (bgImage != null ? bgImage.color.r.ToString("F2") : "-")
                + " imgG=" + (bgImage != null ? bgImage.color.g.ToString("F2") : "-")
                + " imgB=" + (bgImage != null ? bgImage.color.b.ToString("F2") : "-")
                + " raycast=" + (bgImage != null ? bgImage.raycastTarget.ToString() : "-")
                + " pos=" + (bgRt != null ? bgRt.anchoredPosition.ToString() : "no-rt")
                + " size=" + (bgRt != null ? bgRt.sizeDelta.ToString() : "no-rt")

                + " [GroundCheckmark] active=" + (groundCheckmark != null ? groundCheckmark.gameObject.activeSelf.ToString() : "null")
                + " imgEnabled=" + (groundCheckmark != null ? groundCheckmark.enabled.ToString() : "-")
                + " sprite=" + (groundCheckmark != null && groundCheckmark.sprite != null ? groundCheckmark.sprite.name : "no-sprite")
                + " alpha=" + (groundCheckmark != null ? groundCheckmark.color.a.ToString("F2") : "-")
                + " r=" + (groundCheckmark != null ? groundCheckmark.color.r.ToString("F2") : "-")
                + " g=" + (groundCheckmark != null ? groundCheckmark.color.g.ToString("F2") : "-")
                + " b=" + (groundCheckmark != null ? groundCheckmark.color.b.ToString("F2") : "-")
                + " raycast=" + (groundCheckmark != null ? groundCheckmark.raycastTarget.ToString() : "-")
                + " pos=" + (groundCheckmark != null ? groundCheckmark.rectTransform.anchoredPosition.ToString() : "-")

                + " [Toggle] found=" + (radioToggle != null)
                + " interactable=" + (radioToggle?.interactable ?? false)
                + " isOn=" + (radioToggle?.isOn ?? false);
        }

        private void Start()
        {
            Transform bgT = transform.Find("Background");
            bgImage = bgT != null ? bgT.GetComponent<Image>() : null;
            if (bgImage == null)
            {
                Debug.LogError("[RadioNav][GroundHandler] Background Image not found, disabling");
                enabled = false;
                return;
            }
            if (bgImage != null)
            {
                Color c = bgImage.color;
                c.a = DimAlpha;
                bgImage.color = c;
            }

            if (bgT != null)
            {
                Transform cm = bgT.Find("GroundCheckmark");
                if (cm != null)
                {
                    groundCheckmark = cm.GetComponent<Image>();
                    if (groundCheckmark != null)
                    {
                        Color c = groundCheckmark.color;
                        c.a = 0f;
                        groundCheckmark.color = c;
                        groundCheckmark.raycastTarget = true;
                    }
                }
            }

            if (transform.parent != null)
                radioToggle = transform.parent.Find("Toggle")?.GetComponent<Toggle>();
            else
                Debug.LogError("[RadioNav][GroundHandler] transform.parent is null");
            if (radioToggle != null)
            {
                radioToggle.isOn = false;
                radioToggle.interactable = false;
            }
            ToggleHandler.SetGroundActive(false);

#if DEBUG
            Debug.Log("[RadioNav][Ground] Start " + StateLog());
#endif
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            bool isActive = bgImage != null && bgImage.color.a > ActiveAlphaThreshold;

            if (isActive && radioToggle != null && radioToggle.isOn)
            {
                radioToggle.isOn = false;
                radioToggle.interactable = false;
            }

            if (bgImage != null)
            {
                Color c = bgImage.color;
                c.a = isActive ? DimAlpha : MaxAlpha;
                bgImage.color = c;
            }

            if (groundCheckmark != null)
            {
                Color c = groundCheckmark.color;
                c.a = isActive ? 0f : 1f;
                groundCheckmark.color = c;
            }

            if (radioToggle != null)
                radioToggle.interactable = !isActive;

            ToggleHandler.SetGroundActive(!isActive);

            if (OnValueChanged != null)
                OnValueChanged(!isActive);

#if DEBUG
            Debug.Log("[RadioNav][Ground] onClick screenPos=" + eventData.position + " " + StateLog());
#endif
        }
    }
}
