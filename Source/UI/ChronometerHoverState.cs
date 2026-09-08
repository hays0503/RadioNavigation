namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Chronometer zone hover-request state behind
    /// ChronometerOverlayHandler (P5.3 slice, CH). The handler guards
    /// against active drags and forwards to the cursor backend + panel;
    /// the flag store and the emit-only-on-change forward live here so
    /// the sequences run offline in ChronometerHoverStateTests. Pure C#.
    /// </summary>
    public sealed class ChronometerHoverState
    {
        private bool scaleHover;
        private bool moveValid;
        private bool moveHover;
        private bool insideCached;

        public bool ScaleHover => scaleHover;

        public bool MoveHover => moveHover;

        public void SetScaleHover(bool value)
        {
            scaleHover = value;
        }

        public void SetMoveHover(bool value)
        {
            moveHover = value;
            moveValid = true;
        }

        public bool TryConsumeForward(out bool inside)
        {
            inside = moveValid && moveHover;
            if (inside == insideCached)
                return false;
            insideCached = inside;
            return true;
        }
    }
}
