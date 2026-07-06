using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    public class VolumeSpinnerHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private float minAngle = 0f;
        [SerializeField] private float maxAngle = 140f;
        [SerializeField] private float startAngle = 0f;
        [SerializeField] private float sensitivity = 0.5f;

        private RectTransform spinnerRect;
        private bool isDragging;
        private float currentAngle;

        public float Angle => currentAngle;

        private void Start()
        {
            spinnerRect = GetComponent<RectTransform>();
            spinnerRect.pivot = new Vector2(0.5f, 0.5f);

            Image img = GetComponent<Image>();
            if (img != null)
                img.raycastTarget = true;

            currentAngle = Mathf.Clamp(startAngle, minAngle, maxAngle);
            spinnerRect.localRotation = Quaternion.Euler(0, 0, -currentAngle);

            Debug.Log("[TestUI][VolumeSpinner] Start angle=" + currentAngle
                + " min=" + minAngle + " max=" + maxAngle);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            isDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging)
                return;

            Vector2 dir = Input.mousePosition - spinnerRect.position;
            if (dir.y < 0)
                return;
            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + 90f;
            float targetAngle = Mathf.Clamp(angle, minAngle, maxAngle);
            currentAngle = Mathf.Lerp(currentAngle, targetAngle, sensitivity);

            spinnerRect.localRotation = Quaternion.Euler(0, 0, -currentAngle);

            Debug.Log("[TestUI][VolumeSpinner] angle=" + currentAngle);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
        }
    }
}
