namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Hold tracker for body identification (CELESTIAL_REALISM_PLAN P5).
    /// The old code added the tick to one timer for ANY body under the
    /// reticle, so a crowded field (Crux, Orion) confirmed a chain of
    /// different stars. Here the hold belongs to a KEY (kind:hip:name): a
    /// different key must persist for the grace window before the tracker
    /// switches, a single missed tick decays instead of snapping to zero,
    /// and the full hold is required on the same body. Pure state, no Unity.
    /// </summary>
    public sealed class HoldTracker
    {
        public const float HoldSeconds = 2.5f;
        public const float TickSeconds = 0.2f;
        public const float GraceSeconds = 0.6f;
        private const float DecayPerSecond = 2.0f;

        private string key = "";
        private string pendingKey = "";
        private float hold;
        private float grace;

        /// <summary>Key currently being held ("" while none).</summary>
        public string Key
        {
            get { return key; }
        }

        /// <summary>Hold progress in [0,1] — drives the on-screen bar.</summary>
        public float Progress
        {
            get { return hold >= HoldSeconds ? 1f : hold / HoldSeconds; }
        }

        /// <summary>True once the same body has been held for HoldSeconds.</summary>
        public bool Confirmed
        {
            get { return key.Length > 0 && hold >= HoldSeconds; }
        }

        /// <summary>Resets everything (sextant open, pain, hull gate).</summary>
        public void Reset()
        {
            key = "";
            pendingKey = "";
            hold = 0f;
            grace = 0f;
        }

        /// <summary>
        /// Feeds one identification tick. <paramref name="foundKey"/> is the
        /// key of the body under the reticle ("" when none). Returns true when
        /// the hold just completed on this tick (rising edge only).
        /// </summary>
        public bool Tick(string foundKey, float dt)
        {
            if (dt <= 0f)
                return false;
            if (foundKey == null)
                foundKey = "";

            if (foundKey.Length == 0)
            {
                // Lost the body: decay instead of snapping, clear once spent.
                hold -= dt * DecayPerSecond;
                pendingKey = "";
                grace = 0f;
                if (hold <= 0f)
                {
                    hold = 0f;
                    key = "";
                }
                return false;
            }

            if (key.Length == 0)
            {
                key = foundKey;
                hold = 0f;
                pendingKey = "";
                grace = 0f;
            }
            else if (foundKey != key)
            {
                // A different body: only switch if it persists past the grace
                // window (a jitter frame must not restart the hold).
                if (foundKey != pendingKey)
                {
                    pendingKey = foundKey;
                    grace = 0f;
                }
                grace += dt;
                if (grace >= GraceSeconds)
                {
                    key = foundKey;
                    pendingKey = "";
                    grace = 0f;
                    hold = 0f;
                }
                return false; // the original hold is frozen, not extended
            }

            bool wasConfirmed = hold >= HoldSeconds;
            hold += dt;
            if (hold > HoldSeconds)
                hold = HoldSeconds;
            return !wasConfirmed && hold >= HoldSeconds;
        }
    }
}
