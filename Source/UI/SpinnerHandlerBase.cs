using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public abstract class SpinnerHandlerBase : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public event Action<float> OnValueChanged;

        [SerializeField] protected float minAngle = 0f;
        [SerializeField] protected float maxAngle = SpinnerScale.MaxAngle;
        [SerializeField] protected float startAngle = 0f;
        [SerializeField] protected float sensitivity = 0.5f;

        protected RectTransform spinnerRect;
        protected bool isDragging;
        protected int activePointerId = -1;
        protected float currentAngle;

        public float Angle => currentAngle;

        // F15: restore a persisted angle; clamps to the valid range and
        // applies the visual rotation like a drag would.
        public void SetAngle(float angle)
        {
            currentAngle = ClampTargetAngle(angle);
            if (spinnerRect.IsNotNull())
                spinnerRect.localRotation = Quaternion.Euler(0, 0, -currentAngle);
            OnAngleUpdated(currentAngle);
        }

        protected abstract string DebugTag { get; }

        protected virtual float GetAngleOffset() => 90f;
        protected virtual bool UseSmoothing() => true;
        protected virtual float ClampTargetAngle(float raw)
        {
            if (float.IsNaN(raw))
                return minAngle;
            return Mathf.Clamp(raw, minAngle, maxAngle);
        }
        protected virtual bool HandleDragDirection(Vector2 dir)
        {
            if (dir.y.IsNegative())
                return false;
            return true;
        }
        protected virtual void OnAngleUpdated(float angle)
        {
            OnValueChanged?.Invoke(angle);
        }

        protected virtual void Awake()
        {
            spinnerRect = GetComponent<RectTransform>();
            if (spinnerRect.IsNull())
            {
                Debug.LogError("[RadioNav][" + DebugTag + "] RectTransform not found, disabling");
                enabled = false;
                return;
            }
            spinnerRect.pivot = new Vector2(0.5f, 0.5f);

            Image img = GetComponent<Image>();
            if (img.IsNotNull())
                img.raycastTarget = true;

            currentAngle = Mathf.Clamp(startAngle, minAngle, maxAngle);
            spinnerRect.localRotation = Quaternion.Euler(0, 0, -currentAngle);

            if (minAngle > maxAngle)
                Debug.LogError("[RadioNav][" + DebugTag + "] minAngle > maxAngle — knob is dead");
            if (sensitivity.IsNonPositive() || float.IsNaN(sensitivity))
                Debug.LogError("[RadioNav][" + DebugTag + "] non-positive sensitivity — smoothing snaps to target");

            Debug.Log("[RadioNav][" + DebugTag + "] Start angle=" + currentAngle
                + " min=" + minAngle + " max=" + maxAngle);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (!RadioPowerBus.IsInteractable) return;
            if (spinnerRect.IsNull())
            {
                Debug.LogError("[RadioNav][" + DebugTag + "] spinnerRect not initialized");
                return;
            }
            isDragging = true;
            activePointerId = eventData.pointerId;
        }

        // F149: if the object (or a parent) is disabled mid-drag, OnEndDrag
        // never fires and the stale pointer id would lock out future drags.
        protected virtual void OnEnable()
        {
            isDragging = false;
            activePointerId = -1;
        }

        protected bool IsActiveDrag(PointerEventData eventData) =>
            isDragging && eventData.pointerId == activePointerId;

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsActiveDrag(eventData))
                return;
            if (!RadioPowerBus.IsInteractable) return;

            Vector2 dir = eventData.position - (Vector2)spinnerRect.position;
            if (!HandleDragDirection(dir))
                return;

            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + GetAngleOffset();
            float targetAngle = ClampTargetAngle(angle);

            if (UseSmoothing())
            {
                // F72: frame-rate independent smoothing factor; raw Lerp(t)
                // per frame is ~2x faster at 60 FPS than at 30 FPS.
                // Degenerate factor (non-positive/NaN sensitivity, NaN dt)
                // would extrapolate away from the target — snap instead.
                float t = 1f - Mathf.Exp(-sensitivity * Time.deltaTime * 60f);
                if (float.IsNaN(t) || t <= 0f || t > 1f)
                    currentAngle = targetAngle;
                else
                    currentAngle = Mathf.Lerp(currentAngle, targetAngle, t);
            }
            else
                currentAngle = targetAngle;

            spinnerRect.localRotation = Quaternion.Euler(0, 0, -currentAngle);
            OnAngleUpdated(currentAngle);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId) return;
            isDragging = false;
            activePointerId = -1;
        }
    }
}
