using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    internal static class RadioInstaller
    {
        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp.IsNull())
                comp = go.AddComponent<T>();
            return comp;
        }

        internal static GameObject Instantiate(GameObject radio, Transform viewport)
        {
            if (radio.IsNull()) return null;

            EnsureComponent<RadioHandler>(radio);
            EnsureComponent<CanvasGroup>(radio);
            EnsureComponent<PanelDragHandler>(radio);

            // F26: prefab child names centralized in PrefabNames.
            Transform cursor = radio.transform.Find(PrefabNames.Cursor);
            if (cursor.IsNull())
                cursor = radio.transform.Find("Cursor");
            if (cursor.IsNotNull())
                EnsureComponent<CursorHandler>(cursor.gameObject);
            else
                Debug.LogError("[RadioNav][Main] Cursor child not found (tried 'CursorSpinner' and 'Cursor')");

            Transform toggle = radio.transform.Find(PrefabNames.Toggle);
            if (toggle.IsNotNull())
                EnsureComponent<ToggleHandler>(toggle.gameObject);

            Transform ground = radio.transform.Find(PrefabNames.Ground);
            Transform groundBg = ground?.Find(PrefabNames.Background);
            Transform groundCm = groundBg?.Find(PrefabNames.GroundCheckmark);
#if DEBUG
            Image cmImg = groundCm?.GetComponent<Image>();
            Debug.Log("[RadioNav][Main] Ground exists=" + (ground.IsNotNull())
                + " Background exists=" + (groundBg.IsNotNull())
                + " Checkmark exists=" + (groundCm.IsNotNull())
                + " [Ground] pos=" + (ground.IsNotNull() ? ground.localPosition.ToString() : "null")
                + " [Background] pos=" + (groundBg.IsNotNull() ? groundBg.localPosition.ToString() : "null")
                + " img=" + (groundBg.IsNotNull() && groundBg.GetComponent<Image>().IsNotNull())
                + " [Checkmark] pos=" + (groundCm.IsNotNull() ? groundCm.localPosition.ToString() : "null")
                + " sprite=" + (cmImg.IsNotNull() && cmImg.sprite.IsNotNull() ? cmImg.sprite.name : "no-sprite")
                + " imgEnabled=" + (cmImg.IsNotNull() ? cmImg.enabled.ToString() : "no-img")
                + " imgAlpha=" + (cmImg.IsNotNull() ? cmImg.color.a.ToString("F2") : "-"));
#endif
            if (ground.IsNotNull())
                EnsureComponent<GroundHandler>(ground.gameObject);

            Transform expander = radio.transform.Find(PrefabNames.Expander);
            if (expander.IsNotNull())
                EnsureComponent<ExpanderHandler>(expander.gameObject);

            Transform radioZone = radio.transform.Find(PrefabNames.RadioZone);
            if (radioZone.IsNotNull())
                EnsureComponent<ZoneHandler>(radioZone.gameObject);

            Transform volumeSpinner = radio.transform.Find(PrefabNames.VolumeSpinner);
            if (volumeSpinner.IsNotNull())
                EnsureComponent<VolumeSpinnerHandler>(volumeSpinner.gameObject);

            Transform feedbackSpinner = radio.transform.Find(PrefabNames.FeedbackSpinner);
            if (feedbackSpinner.IsNotNull())
                EnsureComponent<FeedbackSpinnerHandler>(feedbackSpinner.gameObject);

            if (radio.GetComponent<SonneController>().IsNull())
            {
                EnsureComponent<AudioSource>(radio);
                radio.AddComponent<SonneController>();
            }

            EnsureComponent<InformationPanelHandler>(radio);
            EnsureComponent<ChronometerOverlayHandler>(radio);

            Transform debugAnglePanel = radio.transform.Find(PrefabNames.DebugAnglePanel);
            if (debugAnglePanel.IsNotNull())
                EnsureComponent<DebugAngleHandler>(debugAnglePanel.gameObject);

            radio.transform.SetParent(viewport, false);
            // F188: (300, 100) in pixels pushed the panel off-screen on low
            // resolutions; center it so it is visible on any viewport.
            radio.transform.localPosition = Vector3.zero;
            radio.SetActive(true);

            return radio;
        }
    }
}
