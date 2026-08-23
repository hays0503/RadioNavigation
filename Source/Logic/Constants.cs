namespace UBOAT.Mods.RadioNavigation
{
    // F28/ADR-005: named homes for the mod's semantic magic numbers.
    // Only values with clear meaning live here; per-class geometry constants
    // (CursorHandler angles, SpinnerHandlerBase offset) stay at their class.
    public static class Constants
    {
        // Beat-frequency oscillator: base and clamp range (Hz).
        public const float BfoBaseHz = 1000f;
        public const float BfoMinHz = 300f;
        public const float BfoMaxHz = 1800f;

        // Morse legibility: dot/dash tones split around the station beat
        // frequency so the two elements are distinguishable by pitch, not
        // only by duration; equisignal stays on the base beat. Dash is also
        // slightly louder for a second duration-independent cue.
        public const float DotPitchOffsetHz = 150f;
        public const float DashPitchOffsetHz = 150f;
        public const float DashGainBoost = 1.25f;

        // Receiver selectivity: Q-factor of the band-pass stage; feedback
        // raises Q by this many units per squared feedback value.
        public const float QualityFactorBase = 20f;
        public const float QualityFactorFeedbackPerUnit = 180f;

        // Station match: a decoded candidate must be within this many kHz of
        // a station to count as its callsign.
        public const float StationMatchDeltaKHz = 20f;

        // Knob art origin: cursor rotation is drawn at angle+90 deg.
        public const float CursorOffsetDeg = 90f;
    }
}
