namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Converts a sampled held-key state into a rising edge. Synchronize is
    /// used when an input owner becomes enabled so a key already held at that
    /// lifecycle boundary is not mistaken for a new press.
    /// </summary>
    public sealed class HeldKeyEdge
    {
        private bool wasDown;

        public bool Advance(bool nowDown)
        {
            bool pressed = nowDown && !wasDown;
            wasDown = nowDown;
            return pressed;
        }

        public void Synchronize(bool nowDown)
        {
            wasDown = nowDown;
        }
    }
}
