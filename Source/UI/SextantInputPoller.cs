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
                // Fine adjustment: one drum tick (0.2' = 1 quantum) per press,
                // hold repeats after a short delay - like turning the drum.
                bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
                if (arcInput.AdvanceFineHold(dt, pressed))
                    arcAngle = SextantArcInput.StepFine(arcAngle, dir);
                return;
            }

            if (logVector != null)
                logVector(dt);

            arcAngle = SextantArcInput.StepCoarse(arcAngle, dir, dt, shiftHeld);
        }

        public void PollFilters(Action<int, bool> setFilters)
        {
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

        public void PollFix(double arcAngle)
        {
            // I records a sight of the identified body (Sun, Moon or star),
            // O starts a new worksheet series.
            if (Input.GetKeyDown(KeyCode.I))
                worksheet.CaptureObservation(starId.IdentifiedBody, arcAngle);
            if (Input.GetKeyDown(KeyCode.O))
                worksheet.ResetSeries();
        }
    }
}
