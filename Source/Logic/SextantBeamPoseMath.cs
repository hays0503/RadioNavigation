using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Orientation of one sextant beam (pitch = height aim, yaw = rotation aim)
    /// in degrees, in the same convention Unity's <c>Transform.eulerAngles</c>
    /// reports: yaw measured about +Y, pitch positive when the nose is DOWN.
    /// </summary>
    public struct SextantBeamPose
    {
        public double PitchDeg;
        public double YawDeg;

        public SextantBeamPose(double pitchDeg, double yawDeg)
        {
            PitchDeg = pitchDeg;
            YawDeg = yawDeg;
        }

        public override string ToString()
        {
            return "(" + PitchDeg.ToString("F3") + "," + YawDeg.ToString("F3") + ")";
        }
    }

    /// <summary>
    /// Signed error of a rendered source against the orientation it is required
    /// to carry. All three axes are zero for a correct render:
    ///  - PitchErrorDeg: height desync (the beam aimed above/below the gaze);
    ///  - YawErrorDeg: rotation desync (the beam turned away from the gaze);
    ///  - ArcErrorDeg: the index/horizon angular gap deviating from the alidade.
    /// </summary>
    public struct SextantBeamError
    {
        public double PitchErrorDeg;
        public double YawErrorDeg;
        public double ArcErrorDeg;

        public bool IsClean(double tolerance)
        {
            return Math.Abs(PitchErrorDeg) <= tolerance
                && Math.Abs(YawErrorDeg) <= tolerance
                && Math.Abs(ArcErrorDeg) <= tolerance;
        }
    }

    /// <summary>
    /// Where each sextant beam must point (SEXTANT_PLAN §17, author report
    /// 2026-09-30: "the look vector and the sextant vector are different").
    ///
    /// The instrument is swung with the eye: BOTH beams carry the observer's
    /// yaw AND pitch, and the alidade adds the angular gap between them. Two
    /// real desyncs were found live on that day and are what this type exists
    /// to prevent:
    ///  1. the beams' pitch used to be pinned to 0 (a hard-coded level
    ///     "horizon" and -arc), so looking up or down left the ocular staring
    ///     straight ahead while the observer looked elsewhere;
    ///  2. inside the boat the ocular image is drawn by a per-half interior
    ///     CLONE camera that the game's VirtualSceneRenderer drives from the
    ///     game's own Interior Camera, which does not follow the look — a
    ///     measured 49-53 deg yaw gap that made the PIP "look somewhere else".
    ///     The clone must therefore carry the SAME pose as its own PIP beam.
    ///
    /// Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class SextantBeamPoseMath
    {
        /// <summary>
        /// Wraps an angle into (-180, 180]. Unity's Euler keeps pitch on this
        /// interval, so `gazePitch - arc` must be wrapped too or a large alidade
        /// would compare unequal against the transform it produced.
        /// </summary>
        public static double NormalizeDeg(double deg)
        {
            if (double.IsNaN(deg) || double.IsInfinity(deg))
                throw new ArgumentOutOfRangeException("deg");
            if (deg > -180d && deg <= 180d)
                return deg;
            double wrapped = deg % 360d;
            if (wrapped <= -180d)
                wrapped += 360d;
            else if (wrapped > 180d)
                wrapped -= 360d;
            return wrapped;
        }

        /// <summary>
        /// Pitch of one beam. Both beams share the gaze pitch; the index beam
        /// is additionally offset by the alidade (positive arc = alidade raised
        /// = index beam looking DOWN in Unity's positive-X-is-down pitch).
        /// </summary>
        public static double BeamPitch(double gazePitchDeg, double arcDeg, bool indexBeam)
        {
            if (double.IsNaN(gazePitchDeg) || double.IsInfinity(gazePitchDeg))
                throw new ArgumentOutOfRangeException("gazePitchDeg");
            if (double.IsNaN(arcDeg) || double.IsInfinity(arcDeg))
                throw new ArgumentOutOfRangeException("arcDeg");
            return NormalizeDeg(indexBeam ? gazePitchDeg - arcDeg : gazePitchDeg);
        }

        /// <summary>
        /// Yaw of either beam: the observer's, taken as-is. The alidade never
        /// enters here on purpose — turning the drum must not swing the beam
        /// sideways, and that is why this method takes no alidade argument.
        /// </summary>
        public static double BeamYaw(double gazeYawDeg)
        {
            if (double.IsNaN(gazeYawDeg) || double.IsInfinity(gazeYawDeg))
                throw new ArgumentOutOfRangeException("gazeYawDeg");
            return NormalizeDeg(gazeYawDeg);
        }

        /// <summary>The horizon (left) beam: the gaze, untouched.</summary>
        public static SextantBeamPose HorizonBeam(double gazePitchDeg, double gazeYawDeg)
        {
            return new SextantBeamPose(
                BeamPitch(gazePitchDeg, 0d, false), BeamYaw(gazeYawDeg));
        }

        /// <summary>The index (right) beam: the gaze, displaced by the alidade.</summary>
        public static SextantBeamPose IndexBeam(double gazePitchDeg, double gazeYawDeg, double arcDeg)
        {
            return new SextantBeamPose(
                BeamPitch(gazePitchDeg, arcDeg, true), BeamYaw(gazeYawDeg));
        }

        /// <summary>
        /// Pose the interior clone of one half must carry: identical to its own
        /// PIP beam. The clone is a render source, not a third beam — anything
        /// else is the desync the author reported (it used to track the game's
        /// Interior Camera, ~49-53 deg off the gaze).
        /// </summary>
        public static SextantBeamPose InteriorClonePose(
            double gazePitchDeg, double gazeYawDeg, double arcDeg, bool indexBeam)
        {
            return indexBeam
                ? IndexBeam(gazePitchDeg, gazeYawDeg, arcDeg)
                : HorizonBeam(gazePitchDeg, gazeYawDeg);
        }

        /// <summary>
        /// Angular gap opened by the alidade, measured as
        /// <c>horizon.Pitch - index.Pitch</c>: positive when the index beam was
        /// driven below the horizon beam, which is what a positive arc does.
        /// </summary>
        public static double BeamPitchGap(double arcDeg)
        {
            if (double.IsNaN(arcDeg) || double.IsInfinity(arcDeg))
                throw new ArgumentOutOfRangeException("arcDeg");
            return NormalizeDeg(arcDeg);
        }

        /// <summary>
        /// Measures a rendered source against the contract: pitch/yaw against
        /// the gaze, plus the gap against the alidade for the index beam.
        /// This is the shape the live probes use, so the unit tests and the
        /// in-game verification cannot drift apart.
        /// </summary>
        public static SextantBeamError BeamError(
            double gazePitchDeg, double gazeYawDeg, double arcDeg, bool indexBeam,
            double renderedPitchDeg, double renderedYawDeg)
        {
            SextantBeamPose required = indexBeam
                ? IndexBeam(gazePitchDeg, gazeYawDeg, arcDeg)
                : HorizonBeam(gazePitchDeg, gazeYawDeg);
            double gap = indexBeam
                ? NormalizeDeg(HorizonBeam(gazePitchDeg, gazeYawDeg).PitchDeg - required.PitchDeg)
                : 0d;
            return new SextantBeamError
            {
                PitchErrorDeg = NormalizeDeg(renderedPitchDeg - required.PitchDeg),
                YawErrorDeg = NormalizeDeg(renderedYawDeg - required.YawDeg),
                ArcErrorDeg = indexBeam ? NormalizeDeg(gap - arcDeg) : 0d
            };
        }
    }
}
