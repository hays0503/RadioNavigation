using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// A debug aim probe: a reproducible angular ACCELERATION applied on top of
    /// whatever the player is doing, so a motion-locked defect can be
    /// reproduced without a mouse (author's request, 2026-10-02: "для отладки
    /// добавь вектор ускорения ... игра будет крутить мышкой").
    ///
    /// Why an acceleration and not an angle: the artefacts under investigation
    /// only appear while the view is SWINGING, and a bridge cannot deliver mouse
    /// motion. An acceleration reproduces the swing exactly - it can be started,
    /// held, repeated and timed - and the resulting angular rate is what the eye
    /// shadow and the picture both react to.
    ///
    /// Three properties make it safe to ship in a mod:
    ///   - it is an OFFSET, not an override, so the player's real aim is never
    ///     replaced and simply reappears underneath;
    ///   - it runs on a TIMER and forces itself back to rest when the timer is
    ///     out, so a forgotten probe cannot leave the view spinning;
    ///   - it is inert at zero acceleration, which is the default, so a normal
    ///     session never touches it.
    ///
    /// Pure and Unity-free, so every hazard below is unit-tested rather than
    /// discovered on screen.
    /// </summary>
    public static class SextantAimProbe
    {
        /// <summary>PlayerPrefs: yaw acceleration, deg/s^2 (0 = off).</summary>
        public const string YawAccelPrefsKey = "RadioNav.ProbeYawAccel";

        /// <summary>PlayerPrefs: pitch acceleration, deg/s^2 (0 = off).</summary>
        public const string PitchAccelPrefsKey = "RadioNav.ProbePitchAccel";

        /// <summary>PlayerPrefs: how long the probe may run, seconds.</summary>
        public const string SecondsPrefsKey = "RadioNav.ProbeSeconds";

        /// <summary>
        /// PlayerPrefs: the run counter. The probe fires on a CHANGE of this
        /// number, not on the vector merely being set.
        ///
        /// This is the difference between a measurement and a mode. A timer alone
        /// is not a safety property: when a probe's time runs out the poll sees
        /// an armed preference with an idle state and starts it again, so a
        /// forgotten preference would swing the view in bursts forever. Requiring
        /// a fresh counter means one swing per deliberate act, and the forgotten
        /// case really does stop.
        /// </summary>
        public const string RunPrefsKey = "RadioNav.ProbeRun";

        /// <summary>
        /// Default run time. Deliberately short: a probe is a measurement, not
        /// a mode, and every second longer is a second in which a forgotten
        /// preference can move the view while the author is doing something else.
        /// </summary>
        public const double DefaultSeconds = 4.0;

        /// <summary>Hard ceiling on the run time, whatever the preference says.</summary>
        public const double MaxSeconds = 30.0;

        /// <summary>
        /// Velocity retained per second. Without any decay the probe would keep
        /// accelerating until it hit the angle clamp and stay pinned there; a
        /// small bleed turns "hold the vector" into a swing that eases off, which
        /// is what a hand does and therefore what has to be reproduced.
        /// </summary>
        public const double VelocityBleedPerSecond = 0.35;

        /// <summary>
        /// Largest offset the probe may reach, in degrees, per axis. The aim is
        /// 0..360, so an unbounded integral would wrap the view through the
        /// poles and the picture would spin instead of swinging.
        /// </summary>
        public const double MaxAngleDegrees = 80.0;

        /// <summary>Ceiling on the probe's angular velocity, deg/s.</summary>
        public const double MaxRateDegPerSec = 220.0;

        /// <summary>
        /// Ceiling on the requested acceleration, deg/s^2. Above this the probe
        /// reaches MaxRateDegPerSec sooner and then behaves identically, so the
        /// clamp only keeps a mistyped preference from being absurd - it is NOT
        /// a limit on how fast the view can go (that is MaxRateDegPerSec).
        /// </summary>
        public const int MaxAccelerationUnits = 4000;

        /// <summary>
        /// PlayerPrefs: degrees per second of CONTINUOUS rotation.
        ///
        /// Why this mode exists. The author asked to "rotate continuously, 5x360
        /// degrees". The swing mode cannot do that and never could: it is an
        /// OFFSET that returns to the player's own aim, and the angle is clamped
        /// at 80 degrees, so it produces repeated 80-degree flicks, not turns.
        /// The keyboard trim cannot either - AddYawTrim wraps at 180, so it
        /// oscillates. This mode is the only way to keep turning: the yaw
        /// accumulates without bound and the current angle is readable, so a
        /// capture can be tied to an exact heading.
        ///
        /// Zero means off, and the same fresh-run rule applies, so a forgotten
        /// preference stops instead of spinning forever.
        /// </summary>
        public const string SpinRatePrefsKey = "RadioNav.ProbeSpinRate";

        /// <summary>PlayerPrefs: seconds the spin may run (same key as the swing).</summary>
        public static double SpinRateFrom(int tenths)
        {
            if (tenths == 0)
                return 0d;
            double value = tenths / 10d;
            if (value > MaxRateDegPerSec)
                return MaxRateDegPerSec;
            if (value < -MaxRateDegPerSec)
                return -MaxRateDegPerSec;
            return value;
        }

        public struct State
        {
            public double Yaw;
            public double Pitch;
            public double YawRate;
            public double PitchRate;
            public double SecondsLeft;
            /// <summary>
            /// Total degrees turned by the spin mode, unwrapped. The swing clamps
            /// its offset, so it can never report a total; the spin accumulates
            /// here instead, which is how a capture can be tied to "the third
            /// full turn" rather than to a heading that wrapped.
            /// </summary>
            public double SpinTotal;

            public bool Moving
            {
                get
                {
                    return Math.Abs(Yaw) > 1e-6 || Math.Abs(Pitch) > 1e-6
                        || Math.Abs(YawRate) > 1e-6 || Math.Abs(PitchRate) > 1e-6;
                }
            }

            public static State Rest
            {
                get
                {
                    var state = new State();
                    state.Yaw = 0d;
                    state.Pitch = 0d;
                    state.YawRate = 0d;
                    state.PitchRate = 0d;
                    state.SecondsLeft = 0d;
                    state.SpinTotal = 0d;
                    return state;
                }
            }
        }

        /// <summary>
        /// Preference value to degrees per second squared.
        ///
        /// Two defects were found by USING the probe on the author's own machine
        /// (2026-10-02), both of which the unit tests had happily locked in:
        ///
        ///   - the unit was ambiguous. The key is read as an int and was
        ///     divided by 1000, so "120" meant 0.12 deg/s^2 - a value so small
        ///     that six seconds of swing produced 0.93 degrees. The telemetry
        ///     prints accel=, so the wrong unit was visible and still read as
        ///     plausible. Plain degrees per second squared removes the
        ///     thousandths trap entirely.
        ///   - a NEGATIVE value returned zero, which silently made leftward and
        ///     downward probing impossible - and lateral motion is exactly what
        ///     the reported artefact needed. The test
        ///     AProbeIsInertUnlessArmed asserted AccelerationFrom(-5) == 0, i.e.
        ///     it pinned the bug. Only zero means "off" now.
        /// </summary>
        public static double AccelerationFrom(int units)
        {
            if (units == 0)
                return 0d;
            if (units > MaxAccelerationUnits)
                return MaxAccelerationUnits;
            if (units < -MaxAccelerationUnits)
                return -MaxAccelerationUnits;
            return units;
        }

        /// <summary>Clamps the run time into (0, MaxSeconds].</summary>
        public static double SecondsFrom(int seconds)
        {
            if (seconds <= 0)
                return 0d;
            double value = seconds > MaxSeconds ? MaxSeconds : seconds;
            return value;
        }

        /// <summary>
        /// True when the requested run is a fresh request rather than the same one
        /// seen a second earlier. Zero never fires: "no counter" means "no probe",
        /// so an untouched installation cannot be moved by a stale preference.
        /// </summary>
        public static bool IsFreshRun(int requestedRun, int consumedRun)
        {
            if (requestedRun <= 0)
                return false;
            return requestedRun != consumedRun;
        }

        /// <summary>
        /// Arms the SPIN mode. Separate from <see cref="Armed"/> because that one
        /// reads "both accelerations are zero" as "off", and a spin has no
        /// acceleration at all - it runs at a constant rate. Reusing Armed would
        /// silently refuse to arm the continuous mode, which is exactly what
        /// happened: the spin tests reported a total of 0 deg because the state
        /// came back as Rest on the very first step.
        /// </summary>
        public static State ArmedSpin(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0d)
                return State.Rest;
            var state = State.Rest;
            state.SecondsLeft = seconds;
            return state;
        }

        /// <summary>Loads a preference pair into a resting state, or Rest if off.</summary>
        public static State Armed(double yawAccel, double pitchAccel, double seconds)
        {
            if (double.IsNaN(yawAccel) || double.IsNaN(pitchAccel) || double.IsNaN(seconds))
                return State.Rest;
            if (seconds <= 0d)
                return State.Rest;
            if (Math.Abs(yawAccel) < 1e-9 && Math.Abs(pitchAccel) < 1e-9)
                return State.Rest;
            var state = State.Rest;
            state.SecondsLeft = seconds;
            return state;
        }

        /// <summary>
        /// One step of the probe. Acceleration builds velocity, velocity bleeds
        /// and integrates into an offset, and the offset is clamped so the swing
        /// cannot run away. The timer is spent every step, and when it runs out
        /// the state returns to <see cref="State.Rest"/> so the view returns to
        /// the player's own aim with no residue.
        /// </summary>
        public static State Advance(State state, double yawAccel, double pitchAccel, double dt)
        {
            if (state.SecondsLeft <= 0d)
                return State.Rest;
            if (double.IsNaN(dt) || dt <= 0d)
                return state;
            if (double.IsNaN(yawAccel) || double.IsInfinity(yawAccel))
                yawAccel = 0d;
            if (double.IsNaN(pitchAccel) || double.IsInfinity(pitchAccel))
                pitchAccel = 0d;

            state.SecondsLeft -= dt;
            if (state.SecondsLeft <= 0d)
                return State.Rest;

            double bleed = VelocityBleedPerSecond * dt;
            state.YawRate = (state.YawRate + yawAccel * dt) * (1d - bleed);
            state.PitchRate = (state.PitchRate + pitchAccel * dt) * (1d - bleed);
            state.YawRate = Clamp(state.YawRate, -MaxRateDegPerSec, MaxRateDegPerSec);
            state.PitchRate = Clamp(state.PitchRate, -MaxRateDegPerSec, MaxRateDegPerSec);

            state.Yaw = Clamp(state.Yaw + state.YawRate * dt, -MaxAngleDegrees, MaxAngleDegrees);
            state.Pitch = Clamp(state.Pitch + state.PitchRate * dt, -MaxAngleDegrees, MaxAngleDegrees);

            // Hitting the angle stop must not leave velocity pointing into it, or
            // the probe would grind against the clamp for the rest of its life.
            if (Math.Abs(state.Yaw) >= MaxAngleDegrees - 1e-9)
                state.YawRate = 0d;
            if (Math.Abs(state.Pitch) >= MaxAngleDegrees - 1e-9)
                state.PitchRate = 0d;
            return state;
        }

        /// <summary>
        /// One step of CONTINUOUS rotation at a constant rate.
        ///
        /// Deliberately different from <see cref="Advance"/> in two ways, both
        /// forced by what the swing mode physically cannot do:
        ///
        ///   - the angle is NOT clamped. The swing must return to the player's
        ///     aim, so its offset is bounded at 80 degrees and it can only
        ///     flicks. The spin is the mode the author asked for ("rotate
        ///     continuously, 5x360 degrees"), so it has to keep turning; the
        ///     total is kept separately in SpinTotal because the view angle
        ///     itself wraps at 360.
        ///   - there is no acceleration ramp. A constant rate is what "turn at
        ///     this speed" means, and ramping would make the angular velocity
        ///     depend on when the probe was started, which is exactly the
        ///     unpredictability that broke the earlier attempts to measure.
        ///
        /// The timer is still honoured, so a forgotten preference cannot turn
        /// the view forever.
        /// </summary>
        public static State AdvanceSpin(State state, double yawRateDegPerSec, double dt)
        {
            if (state.SecondsLeft <= 0d)
                return State.Rest;
            if (double.IsNaN(dt) || dt <= 0d)
                return state;
            if (double.IsNaN(yawRateDegPerSec) || double.IsInfinity(yawRateDegPerSec))
                return state;

            state.SecondsLeft -= dt;
            if (state.SecondsLeft <= 0d)
                return State.Rest;

            double delta = yawRateDegPerSec * dt;
            if (double.IsNaN(delta) || double.IsInfinity(delta))
                return state;
            state.Yaw = (float)BodyRowFormatter.Wrap180(state.Yaw + delta);
            state.YawRate = yawRateDegPerSec;
            state.SpinTotal += delta;
            return state;
        }

        /// <summary>
        /// PlayerPrefs: also drive the VIEW camera with the probe (1 = yes).
        ///
        /// Off by default and it has to stay that way: with it on the probe stops
        /// being a diagnostic overlay on the instrument's own cameras and starts
        /// turning what the player is looking through. No diagnostic may do that
        /// unless it was asked to.
        /// </summary>
        public const string ViewSpinPrefsKey = "RadioNav.ProbeViewSpin";

        /// <summary>
        /// Whether the preference asks for the view camera to be driven.
        /// Anything non-zero is yes, matching the other probe preferences: a
        /// mistyped value should not silently read as "off".
        /// </summary>
        public static bool ViewSpinFrom(int value)
        {
            return value != 0;
        }

        /// <summary>
        /// The angle to write to the view camera: the pose captured when the
        /// probe STARTED, plus the probe's offset.
        ///
        /// The home pose is a parameter rather than read from the camera because
        /// reading it back every frame would integrate the offset into the
        /// player's own aim - the view would walk away and never return, and the
        /// spin would grow without bound. Driving and restoring go through this
        /// one function, so "the probe stops where it started" is the SAME
        /// arithmetic as "the probe turns the view" and cannot drift apart.
        /// Wrapped like the game's euler, because a full turn is a turn.
        /// </summary>
        public static double ViewAngle(double homeDeg, double offsetDeg)
        {
            if (double.IsNaN(homeDeg) || double.IsNaN(offsetDeg))
                return 0d;
            if (double.IsInfinity(homeDeg) || double.IsInfinity(offsetDeg))
                return 0d;
            return BodyRowFormatter.Wrap180(homeDeg + offsetDeg);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
                return 0d;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
