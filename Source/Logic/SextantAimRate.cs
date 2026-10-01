namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Angular rate of the direction the observer is asking to look
    /// along, in degrees per second.</summary>
    public struct SextantAimRateSample
    {
        public double Yaw;
        public double Pitch;

        public SextantAimRateSample(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        public double Magnitude
        {
            get
            {
                double y = Yaw < 0d ? -Yaw : Yaw;
                double p = Pitch < 0d ? -Pitch : Pitch;
                return y > p ? y : p;
            }
        }
    }

    /// <summary>
    /// Turns the per-frame aim pose into the angular RATE the observer is
    /// swinging the instrument at. This is what the eye shadow must follow,
    /// and it replaces reading the mouse axes directly (author 2026-10-02:
    /// "луна должна двигаться от движения камеры, а не от движения курсора —
    /// курсор включён, камера не может двигаться, а значит и окуляр не может
    /// двигаться, и луны быть не может").
    ///
    /// Why rate and not position: what the author requires is that a still view
    /// has no shadow. A rate falls to zero on its own the moment the view stops
    /// turning — no filtering, no thresholds to tune — and it is zero while the
    /// game blocks the camera, which is exactly the case the author reported.
    ///
    /// Pure and Unity-free, because every hazard below is a silent one:
    ///   - Unity's eulerAngles wraps 360 -> 0, so a naive difference spikes by
    ///     360 deg/s once per revolution. Both axes go through Wrap180.
    ///   - A camera teleport (FPP <-> periscope, a scene jump, the R reset that
    ///     zeroes the aim trim) is a real discontinuity of the aim, not an eye
    ///     motion; the frame is dropped and the latch re-anchored.
    ///   - The first frame after opening has no previous pose at all.
    ///   - dt from unscaledDeltaTime is unreliable across a hitch (large) or two
    ///     frames inside a millisecond (tiny); both ends are refused.
    ///
    /// The rate is measured on the aim BEFORE the washout stabilization
    /// (SextantStabilization), i.e. on "where does the observer want to look".
    /// Measuring the stabilized output instead would make the shadow vanish
    /// exactly while the aim freeze holds the view — the case where the hand is
    /// moving and the washout offset (the model's real eye displacement) is at
    /// its largest.
    /// </summary>
    public static class SextantAimRate
    {
        /// <summary>
        /// Rates above this are a discontinuity, not a hand: 900 deg/s is well
        /// past the fastest possible flick (a hard whip at 10 fps is ~200 deg/s)
        /// while a teleport is thousands. Expressed as a RATE, not as a
        /// per-frame step, so it does not reject legitimate fast movement when
        /// the frame rate drops.
        /// </summary>
        public const double RejectRateDegPerSec = 900.0;

        /// <summary>Two frames inside a millisecond: refuse the division.</summary>
        public const double MinDeltaSeconds = 1e-4;

        /// <summary>A hitch longer than this: the pose moved, not the observer.</summary>
        public const double MaxDeltaSeconds = 0.25;

        /// <summary>The latch: the previous aim pose and whether it exists.</summary>
        public struct State
        {
            public bool Anchored;
            public double Yaw;
            public double Pitch;

            public static State Fresh
            {
                get
                {
                    var state = new State();
                    state.Anchored = false;
                    state.Yaw = 0d;
                    state.Pitch = 0d;
                    return state;
                }
            }
        }

        /// <summary>Forgets the anchor — call on the open edge and on reset, so
        /// the first frame after either is not read as a jump.</summary>
        public static State Reset()
        {
            return State.Fresh;
        }

        /// <summary>
        /// Latches the new pose and returns the rate since the previous one.
        /// The pose is always latched, even for a refused or dropped frame, so a
        /// single bad frame cannot poison the following ones.
        /// </summary>
        public static State Advance(State state, double yawDeg, double pitchDeg, double dt,
            out SextantAimRateSample rate)
        {
            rate = new SextantAimRateSample(0d, 0d);
            if (double.IsNaN(yawDeg) || double.IsNaN(pitchDeg))
                return state;

            if (!state.Anchored)
            {
                state.Anchored = true;
                state.Yaw = yawDeg;
                state.Pitch = pitchDeg;
                return state;
            }

            if (double.IsNaN(dt) || dt < MinDeltaSeconds || dt > MaxDeltaSeconds)
            {
                // Re-anchor without dividing: the delta spans an unknown time.
                state.Yaw = yawDeg;
                state.Pitch = pitchDeg;
                return state;
            }

            // eulerAngles is 0..360, so 359.9 -> 0.1 must read as +0.2 deg.
            double dYaw = BodyRowFormatter.Wrap180(yawDeg - state.Yaw);
            double dPitch = BodyRowFormatter.Wrap180(pitchDeg - state.Pitch);

            state.Yaw = yawDeg;
            state.Pitch = pitchDeg;

            rate = new SextantAimRateSample(dYaw / dt, dPitch / dt);
            if (rate.Magnitude > RejectRateDegPerSec)
                rate = new SextantAimRateSample(0d, 0d);
            return state;
        }
    }
}
