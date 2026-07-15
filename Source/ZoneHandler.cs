using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class ZoneHandler : MonoBehaviour
    {
        private static readonly string[] ZoneNames = { "Zone1", "Zone2", "Zone3", "Zone4", "Zone5" };

        [SerializeField] private float visibleAlpha = 1f;
        [SerializeField] private float hiddenAlpha = 0f;

        private Image[] zoneImages;
        private int currentIndex;

        private void Start()
        {
            zoneImages = new Image[ZoneNames.Length];
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                Transform t = transform.Find(ZoneNames[i]);
                if (t != null)
                {
                    zoneImages[i] = t.GetComponent<Image>();
                    SetupClickHandler(t.gameObject);
                }

                SetAlpha(i, i == 0 ? visibleAlpha : hiddenAlpha);
            }
            currentIndex = 0;

            Debug.Log("[TestUI][ZoneHandler] Started, zone=" + ZoneNames[currentIndex]);
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
                    Debug.Log("[TestUI][ZoneHandler] LMB click on " + child.name + " -> next");
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
                    Debug.Log("[TestUI][ZoneHandler] RMB click on " + child.name + " -> previous");
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
            Debug.Log("[TestUI][ZoneHandler] switched to " + ZoneNames[currentIndex]);
        }

        private void PreviousZone()
        {
            if (!CanSwitchZone())
                return;
            SetAlpha(currentIndex, hiddenAlpha);
            currentIndex = (currentIndex - 1 + ZoneNames.Length) % ZoneNames.Length;
            SetAlpha(currentIndex, visibleAlpha);
            Debug.Log("[TestUI][ZoneHandler] switched to " + ZoneNames[currentIndex]);
        }

        private bool CanSwitchZone()
        {
            Transform parent = transform.parent;
            if (parent == null)
                return false;

            Transform toggleT = parent.Find("Toggle");
            if (toggleT == null)
                return false;
            Toggle powerToggle = toggleT.GetComponent<Toggle>();
            if (powerToggle == null || !powerToggle.isOn)
                return false;

            Transform groundT = parent.Find("Ground");
            if (groundT == null)
                return false;
            Transform bgT = groundT.Find("Background");
            if (bgT == null)
                return false;
            Image groundBg = bgT.GetComponent<Image>();
            if (groundBg == null || groundBg.color.a <= 0.5f)
                return false;

            return true;
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
