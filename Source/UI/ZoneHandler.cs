using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public class ZoneHandler : MonoBehaviour
    {
        public event Action<int> OnZoneChanged;

        private static readonly string[] ZoneNames = { "Zone1", "Zone2", "Zone3", "Zone4", "Zone5" };

        [SerializeField] private float visibleAlpha = 1f;
        [SerializeField] private float hiddenAlpha = 0f;

        private Image[] zoneImages;
        private int currentIndex;
        public int CurrentIndex => currentIndex;
        private Transform parentTransform;

        private void Start()
        {
            parentTransform = transform.parent;

            zoneImages = new Image[ZoneNames.Length];
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                Transform t = transform.Find(ZoneNames[i]);
                if (t != null)
                {
                    zoneImages[i] = t.GetComponent<Image>();
                    SetupClickHandler(t.gameObject);
                }
                else
                {
                    Debug.LogWarning("[RadioNav][ZoneHandler] Zone child '" + ZoneNames[i] + "' not found in prefab");
                }

                SetAlpha(i, i == 0 ? visibleAlpha : hiddenAlpha);
            }
            currentIndex = 0;

            Debug.Log("[RadioNav][Zone] Started, zone=" + ZoneNames[currentIndex]);
        }

        private void SetupClickHandler(GameObject child)
        {
            EventTrigger trigger = child.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = child.AddComponent<EventTrigger>();

            EventTrigger.Entry leftClick = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            leftClick.callback.AddListener((data) =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped != null && ped.button == PointerEventData.InputButton.Left)
                {
                    Debug.Log("[RadioNav][Zone] LMB click on " + child.name + " -> next");
                    NextZone();
                }
            });
            trigger.triggers.Add(leftClick);

            EventTrigger.Entry rightClick = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            rightClick.callback.AddListener((data) =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped != null && ped.button == PointerEventData.InputButton.Right)
                {
                    Debug.Log("[RadioNav][Zone] RMB click on " + child.name + " -> previous");
                    PreviousZone();
                }
            });
            trigger.triggers.Add(rightClick);
        }

        private void NextZone()
        {
            if (!CanSwitchZone())
                return;
            SetAlpha(currentIndex, hiddenAlpha);
            currentIndex = (currentIndex + 1) % ZoneNames.Length;
            SetAlpha(currentIndex, visibleAlpha);
            if (OnZoneChanged != null)
                OnZoneChanged(currentIndex);
#if DEBUG
            Debug.Log("[RadioNav][Zone] switched to " + ZoneNames[currentIndex]);
#endif
        }

        private void PreviousZone()
        {
            if (!CanSwitchZone())
                return;
            SetAlpha(currentIndex, hiddenAlpha);
            currentIndex = (currentIndex - 1 + ZoneNames.Length) % ZoneNames.Length;
            SetAlpha(currentIndex, visibleAlpha);
            if (OnZoneChanged != null)
                OnZoneChanged(currentIndex);
#if DEBUG
            Debug.Log("[RadioNav][Zone] switched to " + ZoneNames[currentIndex]);
#endif
        }

        private bool CanSwitchZone()
        {
            return parentTransform != null && ToggleHandler.IsInteractable;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                Transform t = transform.Find(ZoneNames[i]);
                if (t != null)
                {
                    var trigger = t.GetComponent<EventTrigger>();
                    if (trigger != null)
                        trigger.triggers.Clear();
                }
            }
        }

        private void SetAlpha(int index, float alpha)
        {
            if (zoneImages == null || index < 0 || index >= zoneImages.Length)
                return;
            Image img = zoneImages[index];
            if (img == null)
                return;

            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }
    }
}
