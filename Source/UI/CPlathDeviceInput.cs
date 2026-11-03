using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public sealed partial class SextantController
    {
        private readonly CPlathHeldModel heldModel = new CPlathHeldModel();
        private bool heldAimWasDown;
        private bool applyingHeldShadeCounts;

        /// <summary>Z owns the prop; RMB owns only the existing optical view.</summary>
        private bool PollHandheldInput(bool takePressed)
        {
            if (takePressed)
            {
                if (heldModel.IsHeld || IsOpen) { Close(); heldModel.Stow(); }
                else TryTakeHeldModel();
                heldAimWasDown = Input.GetMouseButton(1);
                return true;
            }
            if (!heldModel.IsHeld) return false; // Existing render/sight probes keep their own lifecycle.
            if (CurrentModeName() != "FPP")
            {
                Close(); heldModel.Suspend(); return true;
            }
            bool aimDown = Input.GetMouseButton(1);
            if (aimDown && !heldAimWasDown) TryAimHeldModel();
            if (!aimDown && IsOpen) Close();
            heldAimWasDown = aimDown;
            heldModel.SetAiming(IsOpen);
            // Extra read branch. R retains the existing optical reset command.
            // V is sampled only while this instrument is held, outside its PIP.
            heldModel.SetReading(!IsOpen && Input.GetKey(KeyCode.V));
            if (!IsOpen)
            {
                inputPoller.PollArc(Time.unscaledDeltaTime, ref arcAngle, ShiftHeld(), logArcInputDelegate);
                PollDeviceFilters();
            }
            return false;
        }

        private void TryTakeHeldModel()
        {
            if (CurrentModeName() != "FPP")
            {
                Debug.Log(LogPrefix + " take ignored outside FPP"); return;
            }
            heldModel.Take();
        }

        private void TryAimHeldModel()
        {
            int left = leftFilterCount, right = rightFilterCount;
            TryOpen();
            if (!IsOpen) return;
            // Looking away must not mechanically raise every shade on the next peek.
            ApplyHeldShadeCounts(left, right);
        }

        private void PollDeviceFilters()
        {
            bool changed;
            if (heldModel.PollIndependentShades(out changed))
            {
                if (changed) ApplyHeldShadeCounts(heldModel.LeftCount, heldModel.RightCount);
            }
            else inputPoller.PollFilters(setFiltersDelegate);
        }

        private void ApplyHeldShadeCounts(int left, int right)
        {
            applyingHeldShadeCounts = true;
            try { SetFilters(left, true); SetFilters(right, false); }
            finally { applyingHeldShadeCounts = false; }
        }

        /// <summary>Reading-only mouse ownership; never true in PIP or other modes.</summary>
        internal bool CanUseReadPointer(CPlathHeldModel owner)
        { return ReferenceEquals(heldModel, owner) && owner.IsHeld && !IsOpen && CurrentModeName() == "FPP"; }

        internal void SyncReadPointerShades(CPlathHeldModel owner)
        {
            if (CanUseReadPointer(owner)) ApplyHeldShadeCounts(owner.LeftCount, owner.RightCount);
        }

        private void UpdateHeldModelPose()
        {
            if (!heldModel.NeedsUpdate) return; // Continue lowering after Z clears desired held state.
            if (CurrentModeName() != "FPP") { heldModel.Suspend(); return; }
            Camera view;
            if (!CameraResolver.TryResolve(out view)) { heldModel.Suspend(); return; }
            heldModel.UpdatePose(view, arcAngle, leftFilterCount, rightFilterCount, Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            Close(); heldModel.Suspend(); heldAimWasDown = false;
        }
    }
}
