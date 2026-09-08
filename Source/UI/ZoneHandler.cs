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
        // Restore may arrive before Start builds the images (sibling Start
        // order is undefined) — park it and apply once built.
        private int? pendingZone;

        // F15: restore a persisted zone index; no-op outside the valid range.
        public void SetZone(int index)
        {
            if (index.IsNegative() || index >= ZoneNames.Length)
                return;
            if (zoneImages.IsNull())
            {
                pendingZone = index;
                return;
            }
            ApplyZone(index);
        }
        private Transform parentTransform;

        private void Start()
        {
            parentTransform = transform.parent;

            zoneImages = new Image[ZoneNames.Length];
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                Transform t = transform.Find(ZoneNames[i]);
                if (t.IsNotNull())
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
            if (pendingZone.HasValue)
            {
                int saved = pendingZone.Value;
                pendingZone = null;
                ApplyZone(saved);
            }
        }

        private void SetupClickHandler(GameObject child)
        {
            EventTrigger trigger = child.GetComponent<EventTrigger>();
            if (trigger.IsNull())
                trigger = child.AddComponent<EventTrigger>();

            // F118: EventTrigger does not filter by mouse button at the
            // eventID level; a single PointerClick entry avoids double
            // callback invocations and the button dispatch is explicit.
            EventTrigger.Entry click = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            click.callback.AddListener((data) =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped.IsNull())
                    return;

                if (ped.button == PointerEventData.InputButton.Left)
                {
                    Debug.Log("[RadioNav][Zone] LMB click on " + child.name + " -> next");
                    NextZone();
                }
                else if (ped.button == PointerEventData.InputButton.Right)
                {
                    Debug.Log("[RadioNav][Zone] RMB click on " + child.name + " -> previous");
                    PreviousZone();
                }
            });
            trigger.triggers.Add(click);
        }

        private void NextZone()
        {
            if (!CanSwitchZone())
            {
                Debug.LogWarning("[RadioNav][Zone] switch blocked (radio not interactable)");
                return;
            }
            ApplyZone((currentIndex + 1) % ZoneNames.Length);
            Debug.Log("[RadioNav][Zone] switched to " + ZoneNames[currentIndex]);
        }

        private void PreviousZone()
        {
            if (!CanSwitchZone())
            {
                Debug.LogWarning("[RadioNav][Zone] switch blocked (radio not interactable)");
                return;
            }
            ApplyZone((currentIndex - 1 + ZoneNames.Length) % ZoneNames.Length);
            Debug.Log("[RadioNav][Zone] switched to " + ZoneNames[currentIndex]);
        }

        private void ApplyZone(int index)
        {
            SetAlpha(currentIndex, hiddenAlpha);
            currentIndex = index;
            SetAlpha(currentIndex, visibleAlpha);
            OnZoneChanged?.Invoke(currentIndex);
        }

        private bool CanSwitchZone()
        {
            return parentTransform.IsNotNull() && RadioPowerBus.IsInteractable;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                Transform t = transform.Find(ZoneNames[i]);
                if (t.IsNotNull())
                {
                    var trigger = t.GetComponent<EventTrigger>();
                    if (trigger.IsNotNull())
                        trigger.triggers.Clear();
                }
            }
        }

        private bool IsInvalidZoneIndex(int index) =>
            zoneImages.IsNull() || index.IsNegative() || index >= zoneImages.Length;

        private void SetAlpha(int index, float alpha)
        {
            if (IsInvalidZoneIndex(index))
                return;
            Image img = zoneImages[index];
            if (img.IsNull())
                return;

            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }
    }
}
