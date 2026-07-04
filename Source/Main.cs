using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using HarmonyLib;
using System;
using System.Reflection;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Stories;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    [NonSerializedInGameState]
    public class Main : IUserMod
    {
        [Inject] private static ResourceManager resourceManager;

        public void OnLoaded()
        {
            try
            {
                var harmony = new Harmony("UBOAT.Mods.TestUI");
                var patchType = typeof(RadioStoryIntegration);
                var anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                var continueStory = typeof(RadioStory).GetMethod("ContinueStory", BindingFlags.Public | BindingFlags.Instance);
                if (continueStory != null)
                {
                    harmony.Patch(continueStory, postfix: new HarmonyMethod(patchType.GetMethod("InjectT3PLL38Decision", anyStatic)));
                    Debug.Log("[TestUI] Patched RadioStory.ContinueStory");
                }

                var retrieveString1 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string) });
                if (retrieveString1 != null)
                {
                    harmony.Patch(retrieveString1, prefix: new HarmonyMethod(patchType.GetMethod("OverrideLocaleKey", anyStatic)));
                    Debug.Log("[TestUI] Patched Locale.RetrieveString");
                }

                var retrieveString2 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string), typeof(string) });
                if (retrieveString2 != null)
                {
                    harmony.Patch(retrieveString2, prefix: new HarmonyMethod(patchType.GetMethod("OverrideLocaleKey", anyStatic)));
                    Debug.Log("[TestUI] Patched Locale.RetrieveString(string,string)");
                }

                var onDecision = typeof(RadioStory).GetMethod("OnDecisionSelected", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onDecision != null)
                {
                    harmony.Patch(onDecision, prefix: new HarmonyMethod(patchType.GetMethod("HandleCustomDecisions", anyStatic)));
                    Debug.Log("[TestUI] Patched RadioStory.OnDecisionSelected");
                }

                var onStopped = typeof(RadioStory).GetMethod("OnStopped", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onStopped != null)
                {
                    harmony.Patch(onStopped, postfix: new HarmonyMethod(patchType.GetMethod("HideRadioOnStop", anyStatic)));
                    Debug.Log("[TestUI] Patched RadioStory.OnStopped");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TestUI] Harmony patch failed: " + e);
            }

            SceneEventsListener.OnSceneStart += SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake += SceneEventsListener_OnSceneAwake;
        }

        private void SceneEventsListener_OnSceneAwake(Scene scene)
        {
            try
            {
                InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                Debug.LogError("[TestUI][Main] Injection failed (Awake)");
                Debug.LogException(e);
            }
        }

        private void SceneEventsListener_OnSceneStart(Scene scene)
        {
            try
            {
                if (scene.name != "Main Scene")
                    return;

                Transform viewport = GameObject.Find("/GUI/Viewport")?.transform;
                if (viewport == null)
                    return;

                GameObject radio = resourceManager.InstantiatePrefab("Radio");
                if (radio == null)
                    return;

                if (radio.GetComponent<RadioHandler>() == null)
                    radio.AddComponent<RadioHandler>();
                if (radio.GetComponent<CanvasGroup>() == null)
                    radio.AddComponent<CanvasGroup>();
                if (radio.GetComponent<PanelDragHandler>() == null)
                    radio.AddComponent<PanelDragHandler>();

                Transform cursor = radio.transform.Find("Cursor") ?? radio.transform.Find("cursor");
                if (cursor == null && radio.transform.childCount > 0)
                    cursor = radio.transform.GetChild(0);
                if (cursor != null)
                {
                    if (cursor.GetComponent<CursorHandler>() == null)
                        cursor.gameObject.AddComponent<CursorHandler>();
                }

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
                {
                    ground.gameObject.AddComponent<GroundHandler>();
                    Debug.Log("[TestUI][Main] GroundHandler added to " + ground.name);
                }

                Transform expander = radio.transform.Find("Expander");
                if (expander != null && expander.GetComponent<ExpanderHandler>() == null)
                    expander.gameObject.AddComponent<ExpanderHandler>();

                radio.transform.SetParent(viewport, false);
                radio.transform.localPosition = new Vector3(300f, 100f, 0f);
                radio.SetActive(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[TestUI][Main] Error: " + e.Message);
            }
        }
    }
}
