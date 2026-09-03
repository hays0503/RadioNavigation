using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Chronometer expand/scale subsystem (P5.3 CH-3): the mod-owned
    /// invisible scale sensor (52x52 hitbox on the chronometer face), the
    /// 2.5x expand toggle with per-frame double-click protection, and the
    /// scale snapshot/restore. Extracted from ChronometerOverlayHandler
    /// (SRP: the scale feature is not the overlay orchestrator's logic).
    /// The owner supplies the drag container and receives the cursor
    /// refresh callback (hover changes and the expand toggle both re-emit
    /// the cursor command).
    /// </summary>
    internal sealed class ChronometerScaleSubsystem
    {
        private ChronometerScaleControl control;
        private Vector3 originalScale;
        private Vector3 attachedScale;
        private bool scaleCaptured;
        private bool expanded;
        private int lastToggleFrame = -1;

        /// <summary>True while the chronometer face is expanded 2.5x.</summary>
        public bool Expanded
        {
            get { return expanded; }
        }

        public bool ScaleCaptured
        {
            get { return scaleCaptured; }
        }

        /// <summary>
        /// Creates the mod-owned scale sensor under the chronometer
        /// container (no-op when it already exists).
        /// </summary>
        public void EnsureControl(RectTransform chronometerDragContainer,
            ChronometerOverlayHandler owner)
        {
            if (control.IsNotNull())
                return;

            GameObject go = new GameObject(
                "ChronometerScaleControl",
                typeof(RectTransform),
                typeof(Image),
                typeof(ChronometerScaleControl));
            go.transform.SetParent(chronometerDragContainer, false);

            RectTransform controlRect = go.GetComponent<RectTransform>();
            controlRect.anchorMin = new Vector2(0.5f, 0.5f);
            controlRect.anchorMax = new Vector2(0.5f, 0.5f);
            controlRect.pivot = new Vector2(0.5f, 0.5f);
            controlRect.anchoredPosition = Vector2.zero;
            controlRect.sizeDelta = new Vector2(52f, 52f);

            Image controlImage = go.GetComponent<Image>();
            controlImage.color = new Color(0f, 0f, 0f, 0f);
            controlImage.raycastTarget = true;

            control = go.GetComponent<ChronometerScaleControl>();
            control.SetOwner(owner);
        }

        /// <summary>
        /// Destroys the sensor and clears the reference so a later raise
        /// recreates it fresh. Used on restore (CH-3 leak fix: the mod-owned
        /// sensor must not survive inside the native hierarchy) and teardown.
        /// </summary>
        public void ReleaseControl()
        {
            if (control.IsNotNull())
                Object.Destroy(control.gameObject);
            control = null;
        }

        /// <summary>Cancels an in-flight map drag on the sensor.</summary>
        public void CancelMapDrag()
        {
            if (control.IsNotNull())
                control.CancelMapDrag();
        }

        /// <summary>
        /// Captures the chronometer's current scale as the restore target
        /// (first capture wins; also clears the expanded flag).
        /// </summary>
        public void Capture(RectTransform chronometerDragContainer)
        {
            if (scaleCaptured)
                return;

            originalScale = chronometerDragContainer.localScale;
            attachedScale = originalScale;
            scaleCaptured = true;
            expanded = false;
        }

        /// <summary>
        /// Re-captures the attached scale after a reparent (the container
        /// may have been rescaled by the new parent).
        /// </summary>
        public void CaptureAttached(RectTransform chronometerDragContainer)
        {
            attachedScale = chronometerDragContainer.localScale;
        }

        /// <summary>
        /// Toggles the 2.5x expansion with per-frame double-click
        /// protection; captures the scale first when the snapshot is still
        /// missing. Returns true when the state actually flipped (the
        /// caller refreshes the cursor so the policy re-emits the arrows).
        /// </summary>
        public bool Toggle(RectTransform chronometerDragContainer)
        {
            if (lastToggleFrame == Time.frameCount)
                return false;

            if (chronometerDragContainer.IsNull())
                return false;

            if (!scaleCaptured)
            {
                Capture(chronometerDragContainer);
                attachedScale = chronometerDragContainer.localScale;
            }

            lastToggleFrame = Time.frameCount;
            expanded = !expanded;
            chronometerDragContainer.localScale = expanded
                ? attachedScale * 2.5f
                : attachedScale;
            return true;
        }

        /// <summary>
        /// Restores the attached (non-expanded) scale and clears the hover
        /// request so the cursor falls back to the game default.
        /// </summary>
        public void Reset(RectTransform chronometerDragContainer,
            ChronometerOverlayHandler owner)
        {
            if (!scaleCaptured || chronometerDragContainer.IsNull())
                return;

            chronometerDragContainer.localScale = attachedScale;
            expanded = false;
            owner.SetScaleHover(false);
            owner.SetMoveHover(false);
        }

        /// <summary>Clears the snapshot (restore path).</summary>
        public void ClearSnapshot()
        {
            scaleCaptured = false;
            originalScale = Vector3.zero;
            attachedScale = Vector3.zero;
        }

        /// <summary>The captured original scale (restore to native parent).</summary>
        public Vector3 OriginalScale
        {
            get { return originalScale; }
        }
    }
}
