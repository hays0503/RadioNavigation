using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    internal static class RadioInstaller
    {
        internal static GameObject Instantiate(GameObject radio, Transform viewport)
        {
            if (radio == null) return null;

            if (radio.GetComponent<RadioHandler>() == null)
                radio.AddComponent<RadioHandler>();
            if (radio.GetComponent<CanvasGroup>() == null)
                radio.AddComponent<CanvasGroup>();
            if (radio.GetComponent<PanelDragHandler>() == null)
                radio.AddComponent<PanelDragHandler>();

            Transform cursor = radio.transform.Find("CursorSpinner");
            if (cursor == null)
                cursor = radio.transform.Find("Cursor");
            if (cursor != null && cursor.GetComponent<CursorHandler>() == null)
                cursor.gameObject.AddComponent<CursorHandler>();
            else if (cursor == null)
                Debug.LogError("[TestUI][Main] Cursor child not found (tried 'CursorSpinner' and 'Cursor')");

            Transform toggle = radio.transform.Find("Toggle");
            if (toggle != null && toggle.GetComponent<ToggleHandler>() == null)
                toggle.gameObject.AddComponent<ToggleHandler>();

            Transform ground = radio.transform.Find("Ground");
            Transform groundBg = ground?.Find("Background");
            Transform groundCm = groundBg?.Find("GroundCheckmark");
            Image cmImg = groundCm?.GetComponent<Image>();
            Debug.Log("[TestUI][Main] Ground exists=" + (ground != null)
                + " Background exists=" + (groundBg != null)
                + " Checkmark exists=" + (groundCm != null)
                + " [Ground] pos=" + (ground != null ? ground.localPosition.ToString() : "null")
                + " [Background] pos=" + (groundBg != null ? groundBg.localPosition.ToString() : "null")
                + " img=" + (groundBg != null && groundBg.GetComponent<Image>() != null)
                + " [Checkmark] pos=" + (groundCm != null ? groundCm.localPosition.ToString() : "null")
                + " sprite=" + (cmImg != null && cmImg.sprite != null ? cmImg.sprite.name : "no-sprite")
                + " imgEnabled=" + (cmImg != null ? cmImg.enabled.ToString() : "no-img")
                + " imgAlpha=" + (cmImg != null ? cmImg.color.a.ToString("F2") : "-"));
            if (ground != null && ground.GetComponent<GroundHandler>() == null)
                ground.gameObject.AddComponent<GroundHandler>();

            Transform expander = radio.transform.Find("Expander");
            if (expander != null && expander.GetComponent<ExpanderHandler>() == null)
                expander.gameObject.AddComponent<ExpanderHandler>();

            Transform radioZone = radio.transform.Find("RadioZone");
            if (radioZone != null && radioZone.GetComponent<ZoneHandler>() == null)
                radioZone.gameObject.AddComponent<ZoneHandler>();

            Transform volumeSpinner = radio.transform.Find("VolumeSpinner");
            if (volumeSpinner != null && volumeSpinner.GetComponent<VolumeSpinnerHandler>() == null)
                volumeSpinner.gameObject.AddComponent<VolumeSpinnerHandler>();

            Transform feedbackSpinner = radio.transform.Find("FeedbackSpinner");
            if (feedbackSpinner != null && feedbackSpinner.GetComponent<FeedbackSpinnerHandler>() == null)
                feedbackSpinner.gameObject.AddComponent<FeedbackSpinnerHandler>();

            radio.transform.SetParent(viewport, false);
            radio.transform.localPosition = new Vector3(300f, 100f, 0f);
            radio.SetActive(true);

            return radio;
        }
    }
}
