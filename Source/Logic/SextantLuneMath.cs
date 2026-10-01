using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The observer's eye offset from the optic axis, in aperture radii
    /// (1.0 = the aperture rim). Pure data.
    /// </summary>
    public struct SextantLuneOffset
    {
        public double X;
        public double Y;

        public SextantLuneOffset(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static SextantLuneOffset Zero
        {
            get { return new SextantLuneOffset(0d, 0d); }
        }

        public double Magnitude
        {
            get { return Math.Sqrt(X * X + Y * Y); }
        }

        public override string ToString()
        {
            return "(" + X.ToString("F4") + "," + Y.ToString("F4") + ")";
        }
    }

    /// <summary>
    /// The shadow's animated state: where it is now and how long the input has
    /// been gone. It has to be state, not a value, because the stop lag is a
    /// TIMER — a stateless step could not tell "just stopped" from "has been
    /// idle for a second", and that lag is the whole point.
    /// </summary>
    public struct SextantLuneState
    {
        public SextantLuneOffset Offset;
        public double ReleaseHoldSeconds;

        public SextantLuneState(SextantLuneOffset offset, double releaseHoldSeconds)
        {
            Offset = offset;
            ReleaseHoldSeconds = releaseHoldSeconds;
        }

        public static SextantLuneState Rest
        {
            get { return new SextantLuneState(SextantLuneOffset.Zero, 0d); }
        }
    }

    /// <summary>
    /// The "lune in the scope" shadow (classic gunner/telescope cue, see the
    /// optical literature: an eye displaced from the exit pupil loses part of
    /// the field behind the tube's inner wall, and the lost part reads as a
    /// curved black crescent hugging the rim).
    ///
    /// Modelled exactly the way it appears in a real instrument, because the
    /// mod already carries the physical error that causes it: the aim is RAW
    /// plus a washout offset, so the observer's hand runs ahead of the frozen
    /// beam and the difference IS an eye offset. The shadow therefore appears
    /// OPPOSITE to the movement, which is what the reference figure shows:
    ///   rest            -> a uniform thin ring at the rim      (вариант д)
    ///   move left       -> crescent on the right                (вариант г)
    ///   move right      -> crescent on the left                 (вариант в)
    ///   move up         -> crescent at the bottom               (вариант а)
    ///   move down       -> crescent at the top                  (вариант б)
    ///   diagonal        -> the sum of the two axes (one formula)
    ///
    /// Coordinates are the shader's shared full-ocular space: x is -1 at the
    /// left rim, +1 at the right rim, y likewise, so the offset is expressed
    /// in the SAME units for both halves (their uvRects tile that space).
    /// Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class SextantLuneMath
    {
        /// <summary>
        /// The old mirror preference (RadioNav.LuneInvert) and the half
        /// selector (RadioNav.LuneHalves) are GONE, not merely unused: the live
        /// A/B settled the side (aim left -&gt; crescent right) and the shadow now
        /// spans both halves because the offset is one field over one aperture
        /// space. A preference nobody decides anything with is a trap for the
        /// next reader.
        /// </summary>
        /// <remarks>Deliberately no constant is left behind for them.</remarks>

        /// <summary>PlayerPrefs: shadow depth in percent (0 disables it).</summary>
        public const string StrengthPrefsKey = "RadioNav.LuneStrength";

        /// <summary>
        /// PlayerPrefs: how far the shadow may travel, in percent of an aperture
        /// radius. The default is 15 %: the shadow's NEAR boundary sits at
        /// <c>0.985 - offset</c>, so a 34 % travel eats a third of the radius and
        /// reads as a wide black bite instead of a lune. At 15 % the far boundary
        /// (0.985 + 0.15 = 1.135) only just leaves the field, which is what
        /// keeps the shape a crescent. Raised live by the author if the full
        /// bite is preferred.
        /// </summary>
        public const string MaxUnitsPrefsKey = "RadioNav.LuneMax";

        /// <summary>
        /// PlayerPrefs: aim rate that opens the full crescent, in deg/s. The
        /// spread between a swell (1-5 deg/s) and a hard flick (60-100 deg/s) is
        /// about thirty-fold, which one linear gain cannot serve, hence the
        /// square-root response in <see cref="FromCameraMotion"/>.
        /// </summary>
        public const string RateFullPrefsKey = "RadioNav.LuneRate";

        /// <summary>
        /// PlayerPrefs: rates below this are treated as "not moving" (deg/s).
        /// Without it, sensor noise and a barely-drifting hull would keep a
        /// permanent sliver of shadow, which reads as dirt on the glass rather
        /// than as optics.
        /// </summary>
        public const string DeadZonePrefsKey = "RadioNav.LuneDead";

        /// <summary>
        /// PlayerPrefs: VERIFICATION probe. 0 (the default) means "follow the
        /// player's aim". Any value 1..100 forces the shadow to that percentage
        /// of <see cref="MaxUnits"/> instead, which is how the crescent's shape
        /// and its edges are measured on a paused frame without a mouse (the
        /// bridge cannot deliver one, and the keyboard alone only reaches 30 %
        /// of the travel). Off by default; the telemetry prints it so a captured
        /// frame is self-describing.
        /// </summary>
        public const string ProbePrefsKey = "RadioNav.LuneProbe";

        /// <summary>
        /// How far the obstruction centre may travel, in aperture radii, when no
        /// preference overrides it (see <see cref="MaxUnitsPrefsKey"/>).
        /// </summary>
        public const double MaxOffsetUnits = 0.15;

        /// <summary>Aim rate that maps to the full travel, in deg/s.</summary>
        public const double RateFullDegPerSec = 70.0;

        /// <summary>Rates below this count as "not moving", in deg/s.</summary>
        public const double DeadZoneDegPerSec = 0.35;

        /// <summary>
        /// Radius of the obstruction disc at rest. Just under 1 so a centred eye
        /// still sees the thin uniform ring of shade (вариант д) instead of a
        /// completely clear field.
        /// </summary>
        public const double ShadowRadiusUnits = 0.985;

        /// <summary>
        /// Default shadow depth (author 2026-10-01: "не прозрачность луны бы
        /// повысил, чтобы до 95 %"). At rest this is also the depth of the thin
        /// uniform ring, so a deeper value darkens the rim all the way round.
        /// </summary>
        public const double DefaultStrength = 0.95;

        /// <summary>
        /// Eye inertia: the shadow snaps in quickly (a flick of the mouse) and
        /// relaxes more slowly, like a real eye returning to centre. Without
        /// the asymmetry a moving cursor would leave a permanent smear.
        /// </summary>
        public const double AttackSeconds = 0.08;

        /// <summary>Relaxation time constant back to a centred eye.</summary>
        public const double ReleaseSeconds = 0.45;

        /// <summary>
        /// Dead time before the shadow starts letting go (author 2026-10-01:
        /// "небольшое запазнение, которое пропадает спустя время"). While the
        /// aim is being steered the offset is pushed out hard; the instant the
        /// input dies the crescent KEEPS its size for this long and only then
        /// decays, which is what the eye does after a flick — the hand stops,
        /// the shadow does not.
        /// </summary>
        public const double ReleaseHoldSeconds = 0.18;

        /// <summary>
        /// Clamps an offset to the aperture-safe disc and rejects NaN. The bound
        /// is a parameter, not the constant, so the author can dial the travel
        /// in PlayerPrefs without the maths and the runtime disagreeing.
        /// </summary>
        public static SextantLuneOffset Clamp(double x, double y, double maxUnits)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
                return SextantLuneOffset.Zero;
            if (double.IsNaN(maxUnits) || maxUnits <= 0d)
                maxUnits = MaxOffsetUnits;
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= maxUnits || magnitude <= 0d)
                return new SextantLuneOffset(x, y);
            double scale = maxUnits / magnitude;
            return new SextantLuneOffset(x * scale, y * scale);
        }

        /// <summary>
        /// Maps the angular rate of the view to the shadow's target offset
        /// (author 2026-10-02: the shadow follows the CAMERA, not the cursor —
        /// with the cursor locked the camera cannot turn, the ocular cannot turn
        /// with it, and there can be no shadow). A rate is the right signal
        /// precisely because it is zero on its own when the view stops.
        ///
        /// The response is a square root, not a line: the hull's own swing
        /// (1-5 deg/s) and a hard flick (60-100 deg/s) are thirty-fold apart, and
        /// a linear gain either hides the swell or saturates on the sea. Unity's
        /// euler pitch grows when looking DOWN, so the vertical axis is negated
        /// to keep one sign convention: a positive offset always means "the
        /// observer moved that way, so the crescent is on the opposite side".
        ///
        /// The aim is measured BEFORE the washout stabilization
        /// (SextantPipPose), so the alidade never reaches here: slewing the drum
        /// moves the instrument, not the view (author: "убрать совсем").
        /// </summary>
        public static SextantLuneOffset FromCameraMotion(
            double yawRateDegPerSec, double pitchRateDegPerSec,
            double maxUnits, double deadZoneDegPerSec, double rateFullDegPerSec)
        {
            double x = Axis(Sanitize(yawRateDegPerSec), maxUnits, deadZoneDegPerSec, rateFullDegPerSec);
            // eulerAngles.x grows downwards, and the crescent must be opposite
            // the movement, so the sign is flipped here once and only here.
            double y = -Axis(Sanitize(pitchRateDegPerSec), maxUnits, deadZoneDegPerSec, rateFullDegPerSec);
            return Clamp(x, y, maxUnits);
        }

        private static double Axis(double rate, double maxUnits, double deadZone, double rateFull)
        {
            if (double.IsNaN(deadZone) || deadZone < 0d)
                deadZone = DeadZoneDegPerSec;
            if (double.IsNaN(rateFull) || rateFull <= deadZone)
                rateFull = RateFullDegPerSec;
            if (double.IsNaN(maxUnits) || maxUnits <= 0d)
                maxUnits = MaxOffsetUnits;
            // Signed: the response follows the MAGNITUDE, the sign says which way
            // the observer went. A "rate <= 0 means nothing" shortcut here would
            // silently kill the whole half of the field in that direction —
            // which is exactly what the first version of this did.
            double magnitude = rate < 0d ? -rate : rate;
            if (magnitude <= deadZone)
                return 0d;
            double fraction = magnitude / rateFull;
            if (fraction > 1d)
                fraction = 1d;
            double travel = maxUnits * Math.Sqrt(fraction);
            return rate < 0d ? -travel : travel;
        }

        private static double Sanitize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        /// <summary>
        /// One smoothing step toward the target: hard attack while the view is
        /// turning, and — only once the input is DEAD — a dead time followed by
        /// a soft decay (see <see cref="AttackSeconds"/>,
        /// <see cref="ReleaseHoldSeconds"/>, <see cref="ReleaseSeconds"/>).
        ///
        /// The hold engages on a dead input, not on any target that happens to
        /// be smaller than the current offset. With a rate-driven target every
        /// deceleration dips below the offset, and holding there would freeze the
        /// shadow at its maximum on each smooth stop — a stutter exactly where
        /// the motion should read as continuous.
        /// </summary>
        public static SextantLuneState Step(SextantLuneState state, SextantLuneOffset target,
            double dt, double maxUnits)
        {
            if (double.IsNaN(dt) || dt <= 0d)
                return state;

            // At rest on both sides the state must FREEZE, not keep counting:
            // the hold timer changing every frame would make the runtime
            // rewrite an unchanged material and log a line at 1 Hz forever.
            if (target.Magnitude <= 1e-12 && state.Offset.Magnitude <= 1e-12)
                return SextantLuneState.Rest;

            bool idle = target.Magnitude <= 1e-12;
            double tau;
            if (idle)
            {
                double hold = state.ReleaseHoldSeconds + dt;
                if (hold < ReleaseHoldSeconds)
                    // Frozen: the crescent keeps its size while the eye settles.
                    return new SextantLuneState(state.Offset, hold);
                state.ReleaseHoldSeconds = ReleaseHoldSeconds;
                tau = ReleaseSeconds;
            }
            else
            {
                state.ReleaseHoldSeconds = 0d;
                // A live but shrinking target tracks down smoothly (no hold).
                tau = target.Magnitude > state.Offset.Magnitude ? AttackSeconds : ReleaseSeconds;
            }

            double blend = 1d - Math.Exp(-dt / tau);
            return new SextantLuneState(
                Clamp(
                    state.Offset.X + (target.X - state.Offset.X) * blend,
                    state.Offset.Y + (target.Y - state.Offset.Y) * blend, maxUnits),
                state.ReleaseHoldSeconds);
        }

        /// <summary>
        /// Centre of the obstruction disc, i.e. the exit pupil the observer is
        /// looking through. The offset is already "the way the observer moved",
        /// so no mirror setting exists any more: the side was settled by the
        /// reference figure (movement left, crescent right) and the invert
        /// preference that used to hedge it was removed with the rest of the
        /// speculative switches.
        /// </summary>
        public static SextantLuneOffset ObstructionCentre(SextantLuneOffset offset)
        {
            return offset;
        }

        /// <summary>
        /// Signed shadow field at a point of the aperture, in aperture radii:
        /// negative inside the clear field, positive inside the shadow. Zero is
        /// exactly on the shadow boundary, so the shader can antialias around it
        /// with fwidth and the tests can reason about sides without pixels.
        /// </summary>
        public static double ShadowDistance(double px, double py, SextantLuneOffset offset)
        {
            SextantLuneOffset centre = ObstructionCentre(offset);
            double dx = px - centre.X;
            double dy = py - centre.Y;
            return Math.Sqrt(dx * dx + dy * dy) - ShadowRadiusUnits;
        }

        /// <summary>True where the field is lost to the tube wall.</summary>
        public static bool IsShadowed(double px, double py, SextantLuneOffset offset)
        {
            return ShadowDistance(px, py, offset) > 0d;
        }

        /// <summary>
        /// Depth of the shadow along one direction of the aperture, in aperture
        /// radii: how far the crescent eats inward from the rim before the field
        /// becomes clear again. 0 when the rim is clear in that direction (the
        /// crescent does not reach there), 1 when the whole ray is shadowed.
        /// </summary>
        public static double RimThickness(double dirX, double dirY, SextantLuneOffset offset)
        {
            const int steps = 1024;
            for (int i = 0; i <= steps; i++)
            {
                double r = 1d - (i / (double)steps);
                if (!IsShadowed(dirX * r, dirY * r, offset))
                    return 1d - r;
            }
            return 1d;
        }
    }
}
