using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sextant key polling behind SextantController (SRP: input polling is
    /// not device logic). Owns the arc-input state (SextantArcInput) and
    /// the zoom steps (SextantZoom); the controller supplies the arc field,
    /// host services and worksheet/starId collaborators. Moved verbatim
    /// (key map, fine-drum repeat, filter sides). UnityEngine.Input-bound,
    /// so no unit tests — covered by live smoke. Needs UnityEngine.
    /// </summary>
    public sealed class SextantInputPoller
    {
        private readonly SextantArcInput arcInput;
        private readonly SextantWorksheet worksheet;
        private readonly SextantStarId starId;
        // The yaw trim's fine-step repeat reuses the arc's hold cycle (the
        // same press-hold rhythm); a separate instance keeps the timers apart.
        private readonly SextantArcInput yawRepeat = new SextantArcInput();

        // P6 burst: while a body is held the arc is tapped into the sampler
        // (0.5 s spacing); I commits the mean and the spread goes to the card.
        private const float BurstTapIntervalSeconds = 0.5f;
        private readonly SextantBurstSampler burst = new SextantBurstSampler();
        private float burstTimer;

        internal SextantInputPoller(SextantArcInput arcInput,
            SextantWorksheet worksheet, SextantStarId starId)
        {
            this.arcInput = arcInput;
            this.worksheet = worksheet;
            this.starId = starId;
        }

        public void PollArc(float dt, ref double arcAngle, bool shiftHeld, Action<float> logVector)
        {
            float dir = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) dir += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) dir -= 1f;
            if (dir == 0f)
            {
                arcInput.ResetFineHold();
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl)
            {
                // Fine adjustment: one vernier tick (0.1' = 1 quantum) per
                // press, hold repeats after a short delay - like turning the
                // drum; one tick equals one displayed digit.
                bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
                if (arcInput.AdvanceFineHold(dt, pressed))
                    arcAngle = SextantArcInput.StepFine(arcAngle, dir);
                return;
            }

            if (logVector != null)
                logVector(dt);

            // Leaving the fine mode drops its repeat timer, so the next
            // Ctrl press fires immediately (C4).
            arcInput.ResetFineHold();
            arcAngle = SextantArcInput.StepCoarse(arcAngle, dir, dt, shiftHeld);
        }

        /// <summary>
        /// Keyboard aim rotation (author request 2026-11-04, mirroring the
        /// bridge binocular's arrow rotation): left/right arrows rotate the
        /// view — coarse rate (Shift = faster) or, with Ctrl, one fine step
        /// per press with the drum's hold-repeat. The trim moves the frozen
        /// pose while the aim freeze is active (the star stays steady — the
        /// arc drum works the same way), so it never releases the freeze.
        /// </summary>
        public void PollYawTrim(float dt, bool shiftHeld)
        {
            float dir = 0f;
            if (Input.GetKey(KeyCode.LeftArrow)) dir -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) dir += 1f;
            if (dir == 0f)
            {
                yawRepeat.ResetFineHold();
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl)
            {
                bool pressed = Input.GetKeyDown(KeyCode.LeftArrow)
                    || Input.GetKeyDown(KeyCode.RightArrow);
                if (yawRepeat.AdvanceFineHold(dt, pressed))
                    SextantPipPose.AddYawTrim(SextantYawInput.DeltaFine(dir));
                return;
            }

            // Leaving the fine mode drops its repeat timer, so the next
            // Ctrl press fires immediately (C4).
            yawRepeat.ResetFineHold();
            SextantPipPose.AddYawTrim(SextantYawInput.DeltaCoarse(dir, dt, shiftHeld));
        }

        public void PollFilters(Action<int, bool> setFilters)        {
            // Ctrl toggles the LEFT (horizon) filter; plain keys the RIGHT
            // (sun) filter - like the separate shade glasses of a sextant.
            bool left = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) setFilters(1, left);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) setFilters(2, left);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) setFilters(3, left);
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) setFilters(0, left);
        }

        public void PollZoom(Action zoomIn, Action zoomOut, Action resetArc)
        {
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                if (zoomIn != null)
                    zoomIn();
            }
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                if (zoomOut != null)
                    zoomOut();
            }
            if (Input.GetKeyDown(KeyCode.R) && resetArc != null)
                resetArc();
        }

        /// <summary>
        /// Instrument index error (P6): C sights a known body and stores the
        /// offset (true altitude − drum reading); Shift+C clears it. Blocked
        /// while a series is active, so every sight of one fix shares the same
        /// instrument state.
        /// </summary>
        public void PollIndexError(double arcAngle)
        {
            if (!Input.GetKeyDown(KeyCode.C))
                return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift)
            {
                IndexErrorCorrection.Clear();
                return;
            }
            // Calibration needs a body actually being sighted: the sticky
            // selection alone (out of the cone) would skew the index error.
            // HasSelection is a cheap invariant guard (C is the only
            // persistent write in this file).
            if (worksheet.SeriesActive || !starId.HasSelection || !starId.InCone)
                return;

            // A NaN altitude (unknown) must never reach Save: the clamp
            // turns it into 0 and would silently wipe the correction.
            float alt = starId.IdentifiedAltDeg;
            if (float.IsNaN(alt))
                return;
            IndexErrorCorrection.Save(IndexErrorCorrection.SolveFromKnownAltitude(
                arcAngle, alt));
        }

        public void PollFix(double arcAngle)
        {
            // P6: tap the arc while a body is held; the tap spacing keeps the
            // burst from filling up in a couple of frames. Tap (not Add)
            // restarts the burst when the drum moved, so the spread never
            // mixes two settings.
            burstTimer -= Time.unscaledDeltaTime;
            if (starId.Confirmed)
            {
                if (burstTimer <= 0f)
                {
                    burstTimer = BurstTapIntervalSeconds;
                    burst.Tap(arcAngle);
                }
            }
            else
            {
                burst.Clear();
                burstTimer = 0f;
            }

            // I records a sight of the SELECTED body (sticky, 2026-11-04: it
            // survives leaving the cone, so the drum can be set first), O
            // starts a new worksheet series and drops the selection.
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (!starId.HasSelection)
                {
                    // Nothing confirmed yet: say why the aim has no sightable
                    // body (low / daylight / new Moon) instead of a bare
                    // "no sight"; the gate verdict is already computed.
                    worksheet.ReportBlockedSight(starId.LastBlock);
                    burst.Clear();
                    burstTimer = 0f;
                    return;
                }

                // The recorded Ho is the drum reading at THIS key press
                // (author 2026-09-22): the auto-taps are a quality meter for
                // the card (± σ, n) and never move the recorded value.
                double ho = arcAngle;
                double sigmaArcMin = 0.0;
                int samples = 1;
                int rejected = 0;
                if (burst.TryCommit(out _, out sigmaArcMin, out rejected))
                    samples = burst.Count - rejected;
                worksheet.CaptureObservation(starId.IdentifiedBody, ho,
                    sigmaArcMin, samples, rejected);
                burst.Clear();
                burstTimer = 0f;
            }
            if (Input.GetKeyDown(KeyCode.O))
            {
                worksheet.ResetSeries();
                starId.ClearSelection();
            }
        }
    }
}
