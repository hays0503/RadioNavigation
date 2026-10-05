using System;
using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Covers the class of defects behind the author report of 2026-09-30
    /// ("the look vector and the sextant vector are different", then "when I
    /// turn the sextant on the PIP looks to the right"): ANY height (pitch) or
    /// rotation (yaw) desync between where the observer looks and where a render
    /// source of the ocular points.
    ///
    /// Two real instances are pinned here:
    ///  - the beams' pitch was hard-wired to 0 / -arc, so looking up or down
    ///    left the ocular staring ahead (pitch desync);
    ///  - the interior clone that actually draws the picture inside the boat
    ///    took the game's Interior Camera pose, a measured 49-53 deg yaw gap
    ///    (rotation desync).
    ///
    /// The sweep style matters: a single example would not have caught either,
    /// because both were correct at arc = 0 with a level head.
    /// </summary>
    public class SextantBeamPoseMathTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>
        /// Round-tripped angles (a pitch minus an arc and back) differ by a few
        /// ULP, so the gap assertions compare with a tolerance instead of exact
        /// equality — the invariant is the angle, not its bit pattern.
        /// </summary>
        private static void AssertClose(double expected, double actual, int places = 12)
        {
            Assert.True(Math.Abs(expected - actual) <= Math.Pow(10, -places),
                "expected " + expected + " but got " + actual);
        }

        private static double[] GazePitchCases()
        {
            // Level, nose up/down, near the Euler wrap on both sides.
            return new[] { 0d, -4.97d, 12.78d, -90d, 90d, 179.5d, -179.5d, 180d };
        }

        private static double[] GazeYawCases()
        {
            return new[] { 0d, 45.25d, 83.27d, 183.4d, 236.47d, 359.9d };
        }

        private static double[] ArcCases()
        {
            return new[] { 0d, 0.016667d, -0.016667d, 1.5d, 11.005d, -11.005d, 89.9d, 120d };
        }

        [Fact]
        public void EveryBeam_CarriesTheGazeHeightAndRotation()
        {
            foreach (double gazePitch in GazePitchCases())
            {
                foreach (double gazeYaw in GazeYawCases())
                {
                    foreach (double arc in ArcCases())
                    {
                        foreach (bool index in new[] { true, false })
                        {
                            SextantBeamPose beam = index
                                ? SextantBeamPoseMath.IndexBeam(gazePitch, gazeYaw, arc)
                                : SextantBeamPoseMath.HorizonBeam(gazePitch, gazeYaw);

                            // Rotation desync: the alidade must never swing the
                            // beam sideways, and both beams must point where the
                            // observer looks.
                            Assert.Equal(SextantBeamPoseMath.NormalizeDeg(gazeYaw), beam.YawDeg);

                            // Height desync: the observer's pitch is the
                            // baseline. The alidade may only add the documented
                            // gap, never replace it with a hard-coded level.
                            double requiredPitch = index
                                ? SextantBeamPoseMath.NormalizeDeg(gazePitch - arc)
                                : SextantBeamPoseMath.NormalizeDeg(gazePitch);
                            Assert.Equal(requiredPitch, beam.PitchDeg);

                            // And the mesh with the measurement helper used by
                            // the live probes must report a clean render.
                            SextantBeamError error = SextantBeamPoseMath.BeamError(
                                gazePitch, gazeYaw, arc, index, beam.PitchDeg, beam.YawDeg);
                            Assert.True(error.IsClean(Tolerance),
                                "beam " + beam + " vs gaze (" + gazePitch + "," + gazeYaw
                                + ") arc=" + arc + " index=" + index
                                + " errors=(" + error.PitchErrorDeg + ","
                                + error.YawErrorDeg + "," + error.ArcErrorDeg + ")");
                        }
                    }
                }
            }
        }

        [Fact]
        public void InteriorClone_MustCarryItsOwnBeamNotTheEngineCamera()
        {
            // The live failure: the clone reported the engine's Interior Camera
            // pose while the gaze was 53 deg away. Reproduced here as the exact
            // measured triple from the paused frame of 2026-09-30.
            const double gazePitch = 40.95d;
            const double gazeYaw = 236.47d;
            const double engineCameraPitch = 39.25d;
            const double engineCameraYaw = 183.48d;

            SextantBeamError cloneError = SextantBeamPoseMath.BeamError(
                gazePitch, gazeYaw, 11.005d, true, engineCameraPitch, engineCameraYaw);
            Assert.False(cloneError.IsClean(1d), "the engine pose must not pass as the clone's pose");
            Assert.True(Math.Abs(cloneError.YawErrorDeg) > 50d,
                "expected the measured ~53 deg yaw desync, got " + cloneError.YawErrorDeg);

            // The contract the fix installs: the clone carries its own beam.
            foreach (double arc in ArcCases())
            {
                foreach (bool index in new[] { true, false })
                {
                    SextantBeamPose required = SextantBeamPoseMath.InteriorClonePose(
                        gazePitch, gazeYaw, arc, index);
                    SextantBeamPose beam = index
                        ? SextantBeamPoseMath.IndexBeam(gazePitch, gazeYaw, arc)
                        : SextantBeamPoseMath.HorizonBeam(gazePitch, gazeYaw);
                    Assert.Equal(beam.PitchDeg, required.PitchDeg);
                    Assert.Equal(beam.YawDeg, required.YawDeg);
                }
            }
        }

        [Fact]
        public void AlidadeGap_IsExactlyTheAlidade_AndZeroIsNoGap()
        {
            foreach (double arc in ArcCases())
            {
                SextantBeamPose horizon = SextantBeamPoseMath.HorizonBeam(7d, 100d);
                SextantBeamPose index = SextantBeamPoseMath.IndexBeam(7d, 100d, arc);
                AssertClose(SextantBeamPoseMath.BeamPitchGap(arc),
                    SextantBeamPoseMath.NormalizeDeg(horizon.PitchDeg - index.PitchDeg));
            }

            SextantBeamPose zero = SextantBeamPoseMath.IndexBeam(12.78d, 75.03d, 0d);
            SextantBeamPose level = SextantBeamPoseMath.HorizonBeam(12.78d, 75.03d);
            Assert.Equal(level.PitchDeg, zero.PitchDeg);
            Assert.Equal(level.YawDeg, zero.YawDeg);
        }

        [Fact]
        public void BeamsThatShareAPose_ReportNoDesync()
        {
            // The regression shape: identical inputs must produce an identical
            // pair, whatever the alidade does.
            for (int i = -360; i <= 360; i += 5)
            {
                double yaw = i;
                SextantBeamPose left = SextantBeamPoseMath.HorizonBeam(0d, yaw);
                SextantBeamPose right = SextantBeamPoseMath.HorizonBeam(0d, yaw);
                Assert.Equal(left.PitchDeg, right.PitchDeg);
                Assert.Equal(left.YawDeg, right.YawDeg);
                Assert.True(SextantBeamPoseMath.BeamError(0d, yaw, 0d, false,
                    left.PitchDeg, left.YawDeg).IsClean(Tolerance));
            }
        }

        [Fact]
        public void NormalizeDeg_WrapsOntoUnitysInterval()
        {
            Assert.Equal(0d, SextantBeamPoseMath.NormalizeDeg(360d));
            Assert.Equal(-1d, SextantBeamPoseMath.NormalizeDeg(-1d), 6);
            Assert.Equal(179.5d, SextantBeamPoseMath.NormalizeDeg(179.5d));
            Assert.Equal(-179.5d, SextantBeamPoseMath.NormalizeDeg(180.5d));
            Assert.Equal(-170d, SextantBeamPoseMath.NormalizeDeg(190d));
            Assert.Equal(170d, SextantBeamPoseMath.NormalizeDeg(-190d));
            Assert.Equal(180d, SextantBeamPoseMath.NormalizeDeg(540d));

            // Wrapping must keep the alidade gap intact at the seam.
            SextantBeamPose low = SextantBeamPoseMath.IndexBeam(-179d, 10d, -11.005d);
            Assert.Equal(SextantBeamPoseMath.NormalizeDeg(-167.995d), low.PitchDeg, 6);
        }

        [Fact]
        public void NonFiniteInputs_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SextantBeamPoseMath.BeamPitch(double.NaN, 0d, false));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SextantBeamPoseMath.BeamPitch(0d, double.PositiveInfinity, true));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SextantBeamPoseMath.BeamYaw(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SextantBeamPoseMath.BeamPitchGap(double.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SextantBeamPoseMath.NormalizeDeg(double.NaN));
        }
    }
}
