using System;
using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The eye-pupil shadow ("lune dans le scope") — the six variants of the
    /// reference figure the author supplied (2026-10-01):
    ///   rest -&gt; uniform thin ring (д), left -&gt; crescent right (г),
    ///   right -&gt; crescent left (в), up -&gt; crescent below (а),
    ///   down -&gt; crescent above (б), diagonal -&gt; the sum of both axes.
    ///
    /// The property that matters is not a pixel comparison but the SIDE: a
    /// single "is it visible" test would pass with the crescent on the wrong
    /// edge, and the crescent on the wrong edge is exactly what makes the
    /// effect read as a bug instead of as optics. Thickness is checked to grow
    /// monotonically with the eye offset, which is what the reference figure
    /// shows as the crescent swelling away from the movement.
    ///
    /// The driver is the angular RATE of the view (author 2026-10-02), not the
    /// mouse axes: a still view must have no shadow at all.
    /// </summary>
    public class SextantLuneMathTests
    {
        private const double Epsilon = 1e-9;
        private const double ApertureRim = 1.0;   // the aperture edge in shader units
        private const double Diagonal = 0.7071067811865476;

        private static SextantLuneOffset Offset(double x, double y)
        {
            return SextantLuneMath.Clamp(x, y, SextantLuneMath.MaxOffsetUnits);
        }

        private static SextantLuneOffset FromMotion(double yaw, double pitch)
        {
            return SextantLuneMath.FromCameraMotion(yaw, pitch, SextantLuneMath.MaxOffsetUnits,
                SextantLuneMath.DeadZoneDegPerSec, SextantLuneMath.RateFullDegPerSec);
        }

        private static SextantLuneState Step(SextantLuneState state, SextantLuneOffset target, double dt)
        {
            return SextantLuneMath.Step(state, target, dt, SextantLuneMath.MaxOffsetUnits);
        }

        [Fact]
        public void Rest_ShowsAUniformThinRingAtTheRim()
        {
            SextantLuneOffset rest = Offset(0d, 0d);

            Assert.False(SextantLuneMath.IsShadowed(0d, 0d, rest));
            Assert.False(SextantLuneMath.IsShadowed(0.5d, 0.5d, rest));

            double[] axes = { 1d, -1d, 0d, 0d };
            double[] partners = { 0d, 0d, 1d, -1d };
            for (int i = 0; i < axes.Length; i++)
            {
                double x = axes[i] * ApertureRim;
                double y = partners[i] * ApertureRim;
                Assert.True(SextantLuneMath.IsShadowed(x, y, rest),
                    "rest must shade the rim uniformly at (" + x + "," + y + ")");
            }
            foreach (double[] dir in new[] { new[] { 1d, 0d }, new[] { 0d, 1d }, new[] { Diagonal, Diagonal } })
            {
                Assert.Equal(SextantLuneMath.RimThickness(dir[0], dir[1], rest),
                    SextantLuneMath.RimThickness(1d, 0d, rest), 9);
            }

            double thickness = 1d - SextantLuneMath.ShadowRadiusUnits;
            Assert.True(thickness > 0d && thickness < 0.03,
                "the resting ring must stay thin, got " + thickness);
        }

        [Theory]
        // the view turns -> the crescent must form on the OPPOSITE rim
        [InlineData(40d, 0d, -1d, 0d)]   // right      -> crescent left  (в)
        [InlineData(-40d, 0d, 1d, 0d)]   // left       -> crescent right (г)
        // Unity's euler pitch grows when looking DOWN, and the crescent is
        // opposite the movement: looking down puts it at the top.
        [InlineData(0d, 40d, 0d, 1d)]    // look down  -> crescent above (б)
        [InlineData(0d, -40d, 0d, -1d)]  // look up    -> crescent below (а)
        public void CrescentFormsOnTheOppositeSide(double yawRate, double pitchRate,
            double shadowX, double shadowY)
        {
            SextantLuneOffset offset = FromMotion(yawRate, pitchRate);

            Assert.True(SextantLuneMath.IsShadowed(shadowX * ApertureRim, shadowY * ApertureRim, offset),
                "expected the shadow at the rim toward (" + shadowX + "," + shadowY + ")");
            Assert.False(SextantLuneMath.IsShadowed(-shadowX * ApertureRim, -shadowY * ApertureRim, offset),
                "the rim toward the movement must stay clear");

            // The crescent is a LUNE, not a bite: it must fade out towards the
            // sides instead of swallowing the field.
            double depth = SextantLuneMath.RimThickness(shadowX, shadowY, offset);
            Assert.True(depth > 0.05 && depth < 0.75,
                "the crescent must be a crescent, got depth " + depth);
            Assert.Equal(0d, SextantLuneMath.RimThickness(-shadowX, -shadowY, offset), 9);
        }

        [Fact]
        public void DiagonalMovement_CombinesBothAxes()
        {
            SextantLuneOffset offset = FromMotion(-40d, -40d);

            // The view turned left and up, so the crescent sits right and down.
            double far = SextantLuneMath.RimThickness(Diagonal, -Diagonal, offset);
            double near = SextantLuneMath.RimThickness(-Diagonal, Diagonal, offset);
            Assert.True(far > 0.03, "the diagonal crescent must be visible, got " + far);
            Assert.Equal(0d, near, 9);

            double onX = SextantLuneMath.RimThickness(1d, 0d, offset);
            double onY = SextantLuneMath.RimThickness(0d, -1d, offset);
            Assert.True(onX > 0d, "the diagonal must reach the x edge");
            Assert.True(onY > 0d, "the diagonal must reach the y edge");

            // Both axes really contribute: the same per-axis rate on one axis
            // alone eats LESS than the pair does.
            double single = SextantLuneMath.RimThickness(-1d, 0d, FromMotion(-40d, 0d));
            Assert.True(far > single,
                "the diagonal must add the second axis: " + far + " vs " + single);
        }

        [Fact]
        public void ThicknessGrowsMonotonicallyWithTheRate()
        {
            double previous = -1d;
            for (int step = 1; step <= 8; step++)
            {
                double rate = SextantLuneMath.RateFullDegPerSec * step / 8d;
                // Turning left puts the crescent on the right, hence the probe.
                double thickness = SextantLuneMath.RimThickness(1d, 0d, FromMotion(-rate, 0d));
                Assert.True(thickness >= previous - Epsilon,
                    "thickness must not shrink: " + previous + " -> " + thickness);
                previous = thickness;
            }
            Assert.True(previous > 0.05,
                "a full-rate movement must eat a visible crescent, got " + previous);
        }

        [Fact]
        public void AStillViewHasNoShadowAtAll()
        {
            // The author's rule: the cursor is locked, the camera cannot turn,
            // the ocular cannot turn with it, so there can be no shadow.
            Assert.Equal(0d, FromMotion(0d, 0d).Magnitude, 9);
            // ...and neither has anything under the dead zone (sensor noise, a
            // barely drifting hull), which would otherwise pin a permanent
            // sliver of shadow that reads as dirt on the glass.
            Assert.Equal(0d, FromMotion(0.2d, 0.2d).Magnitude, 9);
            Assert.Equal(0d, FromMotion(-0.35d, -0.35d).Magnitude, 9);
            Assert.True(FromMotion(0.4d, 0d).Magnitude > 0d,
                "just past the dead zone the shadow must appear");
        }

        [Fact]
        public void TheAlidadeCannotReachTheShadow()
        {
            // The author's ruling: slewing the drum moves the instrument, not
            // the view, so it must not produce a lune. It is not an input at
            // all, which is why the signature has no arc term — a test that
            // tried to prove it with geometry would only prove the arithmetic.
            // What matters is that nothing in the mapping reads a drum rate, so
            // this pins the shape of the call: one rate per axis, no third.
            int parameters = 0;
            foreach (System.Reflection.MethodInfo m in typeof(SextantLuneMath)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name == "FromCameraMotion")
                    parameters = m.GetParameters().Length;
            }
            Assert.Equal(5, parameters);
        }

        [Fact]
        public void ResponseSaturatesAndKeepsItsSign()
        {
            double full = FromMotion(SextantLuneMath.RateFullDegPerSec, 0d).Magnitude;
            double far = FromMotion(SextantLuneMath.RateFullDegPerSec * 20d, 0d).Magnitude;
            Assert.Equal(full, far, 9);
            Assert.Equal(SextantLuneMath.MaxOffsetUnits, full, 6);

            // The square root is what makes the swell visible without the flick
            // saturating: a quarter of the range must already be past ~45 % of
            // the travel, where a linear response would give 25 %.
            double half = FromMotion(SextantLuneMath.RateFullDegPerSec * 0.25d, 0d).Magnitude;
            Assert.True(half > full * 0.45, "the response must be concave, got " + half + " of " + full);
        }

        [Fact]
        public void Smoothing_AttacksFastAndHoldsThenReleasesSlowly()
        {
            SextantLuneState rest = SextantLuneState.Rest;
            SextantLuneOffset target = FromMotion(60d, 0d);

            SextantLuneState attacked = Step(rest, target, 0.05);
            Assert.True(attacked.Offset.Magnitude > target.Magnitude * 0.35,
                "a 50 ms flick must already show a good part of the crescent, got "
                    + attacked.Offset + " of " + target);

            double held = attacked.Offset.Magnitude;
            SextantLuneState afterStop = attacked;
            for (int i = 0; i < 200; i++)
            {
                if (afterStop.ReleaseHoldSeconds + 0.01 >= SextantLuneMath.ReleaseHoldSeconds)
                    break;
                afterStop = Step(afterStop, SextantLuneOffset.Zero, 0.01);
            }
            Assert.Equal(held, afterStop.Offset.Magnitude, 9);
            Assert.InRange(afterStop.ReleaseHoldSeconds, 0.1, SextantLuneMath.ReleaseHoldSeconds);

            double released = afterStop.Offset.Magnitude;
            for (int i = 0; i < 10; i++)
                released = Step(afterStop, SextantLuneOffset.Zero, 0.05).Offset.Magnitude;
            Assert.True(released > held * 0.5,
                "the shadow must linger after the hold, dropped to " + released + " of " + held);

            SextantLuneState settling = afterStop;
            for (int i = 0; i < 400; i++)
                settling = Step(settling, SextantLuneOffset.Zero, 0.05);
            Assert.True(settling.Offset.Magnitude < 1e-3, "the shadow must settle back to the ring");
        }

        [Fact]
        public void DeceleratingSweepDoesNotFreezeTheShadow()
        {
            // The hold is for a DEAD input only. With a rate-driven target every
            // deceleration dips under the current offset, and holding there
            // would stutter the shadow at its maximum on every smooth stop.
            SextantLuneState state = Step(SextantLuneState.Rest, FromMotion(60d, 0d), 0.2);
            double before = state.Offset.Magnitude;
            Assert.True(before > 0d, "the sweep must first open the crescent");

            // The view is still turning, only slower: the target is non-zero.
            SextantLuneState slowing = Step(state, FromMotion(5d, 0d), 0.05);
            Assert.Equal(0d, slowing.ReleaseHoldSeconds, 9);
            Assert.True(slowing.Offset.Magnitude < before,
                "a slower view must still let the shadow close, not hold");

            // A dead input is what starts the timer.
            SextantLuneState stopped = Step(slowing, SextantLuneOffset.Zero, 0.05);
            Assert.True(stopped.ReleaseHoldSeconds > 0d, "a dead input must start the lag");
        }

        [Fact]
        public void MovingAgain_CancelsTheHoldImmediately()
        {
            SextantLuneState moving = Step(SextantLuneState.Rest, FromMotion(60d, 0d), 0.1);
            Assert.Equal(0d, moving.ReleaseHoldSeconds, 9);

            SextantLuneState stopping = Step(moving, SextantLuneOffset.Zero, 0.05);
            Assert.True(stopping.ReleaseHoldSeconds > 0d, "the stop must start the lag");

            SextantLuneState again = Step(stopping, FromMotion(0d, 60d), 0.05);
            Assert.Equal(0d, again.ReleaseHoldSeconds, 9);
            Assert.True(again.Offset.Magnitude > stopping.Offset.Magnitude * 0.8,
                "a fresh push must not be swallowed by the release: "
                    + again.Offset + " after " + stopping.Offset);
        }

        [Fact]
        public void AtRestTheStateFreezesInsteadOfCountingForever()
        {
            SextantLuneState rest = SextantLuneState.Rest;
            for (int i = 0; i < 100; i++)
                rest = Step(rest, SextantLuneOffset.Zero, 0.016);
            Assert.Equal(0d, rest.ReleaseHoldSeconds, 9);
            Assert.Equal(0d, rest.Offset.Magnitude, 9);

            SextantLuneState settling = new SextantLuneState(Offset(0.1d, 0d), 0d);
            for (int i = 0; i < 2000; i++)
                settling = Step(settling, SextantLuneOffset.Zero, 0.05);
            Assert.True(settling.Offset.Magnitude < 1e-6,
                "the shadow must reach a true stop, got " + settling.Offset);
            SextantLuneState frozen = Step(settling, SextantLuneOffset.Zero, 0.016);
            Assert.Equal(0d, frozen.ReleaseHoldSeconds, 9);
        }

        [Fact]
        public void Step_RejectsNonPositiveOrNaNTime()
        {
            SextantLuneState state = new SextantLuneState(Offset(0.1d, 0d), 0.1);
            SextantLuneState zero = Step(state, SextantLuneOffset.Zero, 0d);
            Assert.Equal(state.Offset.Magnitude, zero.Offset.Magnitude, 9);
            Assert.Equal(state.ReleaseHoldSeconds, zero.ReleaseHoldSeconds, 9);
            SextantLuneState nan = Step(state, SextantLuneOffset.Zero, double.NaN);
            Assert.Equal(state.ReleaseHoldSeconds, nan.ReleaseHoldSeconds, 9);
        }

        [Fact]
        public void TheLitWallIsASmoothRampAndNotAThreshold()
        {
            // The author's artefact: bright broken arcs hugging the rim on strong
            // mouse movement. The cause was a hard knee in the wall's response to
            // the scene (saturate(luma * 4) gated at 0.25), so on textured cloud
            // the wall appeared only where the scene crossed that level - a
            // dashed arc. The invariant is not "the wall is dimmer" but "the wall
            // has no seam": the response must be continuous AND monotone, so no
            // scene texture can break it into pieces.
            double previous = double.NaN;
            double slope = 0d;
            const int steps = 4000;
            for (int i = 0; i <= steps; i++)
            {
                double luma = i / (double)steps;
                double response = SextantLuneMath.WallResponse(luma);
                if (i > 0)
                {
                    double delta = response - previous;
                    Assert.True(delta >= -1e-12,
                        "the wall must not darken as the scene brightens: " + previous + " -> " + response);
                    slope = delta;
                }
                previous = response;
            }

            // A gate shows up as a slope that jumps by orders of magnitude
            // somewhere; a ramp does not. The widest step over 1/4000 of the
            // luminance range is a hard bound on the local slope.
            double steepest = 0d;
            for (int i = 1; i <= steps; i++)
            {
                double lo = SextantLuneMath.WallResponse((i - 1) / (double)steps);
                double hi = SextantLuneMath.WallResponse(i / (double)steps);
                steepest = Math.Max(steepest, hi - lo);
            }
            Assert.True(steepest < 0.01,
                "the wall response must not jump: steepest step " + steepest);

            // Bounded, with a floor so a black sea does not glow and a ceiling
            // of exactly 1 so a white cloud cannot overdrive it.
            Assert.Equal(SextantLuneMath.WallResponseFloor, SextantLuneMath.WallResponse(0d), 9);
            Assert.Equal(1d, SextantLuneMath.WallResponse(1d), 9);
            Assert.Equal(1d, SextantLuneMath.WallResponse(4d), 9);
            Assert.True(SextantLuneMath.WallResponse(0d) * SextantLuneMath.RimLight < 0.02,
                "the floor must stay invisible on a black scene");
            Assert.True(SextantLuneMath.WallResponse(0.25) > SextantLuneMath.WallResponseFloor + 0.2,
                "a mid-bright scene must actually light the wall");

            // And the crude gate it replaced is gone for good: a hard knee at
            // 0.25 with a 0.2 floor would read 0.2 below and 0.2 above it.
            Assert.NotEqual(
                SextantLuneMath.WallResponse(0.2499), SextantLuneMath.WallResponse(0.2501), 6);
            // Negative and NaN are treated as black, NOT as garbage: a NaN here
            // would propagate into the material and blank the whole field.
            Assert.Equal(SextantLuneMath.WallResponse(0d), SextantLuneMath.WallResponse(-3d), 9);
            Assert.Equal(SextantLuneMath.WallResponse(0d), SextantLuneMath.WallResponse(double.NaN), 9);
            Assert.Equal(SextantLuneMath.WallResponse(0d), SextantLuneMath.WallResponse(double.PositiveInfinity), 9);
        }

        [Fact]
        public void TheLitWallStaysInsideTheShadersOwnRange()
        {
            // The pref is in thousandths and the shader declares Range(0, 0.5);
            // a value above the ceiling would be silently clamped by Unity, so
            // the contract is checked here rather than discovered on screen.
            Assert.True(SextantLuneMath.RimLight >= 0d && SextantLuneMath.RimLight <= SextantLuneMath.RimLightMax,
                "the default must sit inside the shader range");
            Assert.InRange(SextantLuneMath.RimLight, 0.05, 0.2);
            Assert.Equal(0.5, SextantLuneMath.RimLightMax, 9);
        }

        [Theory]
        // The author's A/B depends on every one of these: 0 switches the lit wall
        // off, a normal value passes through, and an over-large one must not
        // exceed the shader's own Range or Unity would clamp it silently.
        [InlineData(0, 0d)]
        [InlineData(45, 0.045)]
        [InlineData(90, 0.09)]
        [InlineData(120, 0.12)]
        [InlineData(500, 0.5)]
        [InlineData(5000, 0.5)]
        [InlineData(-90, 0d)]
        [InlineData(int.MinValue, 0d)]
        public void TheWallKnobIsClampedToTheShadersRange(int thousandths, double expected)
        {
            Assert.Equal(expected, SextantLuneMath.RimLightFrom(thousandths), 9);
        }

        [Fact]
        public void TheWallKnobCanActuallySwitchTheWallOff()
        {
            // If 0 were treated as "unset" it would fall back to the default and
            // the author's five-second A/B would silently do nothing - which is
            // the one thing the knob exists to prevent.
            Assert.Equal(0d, SextantLuneMath.RimLightFrom(0), 9);
            Assert.True(SextantLuneMath.RimLightFrom(0) < SextantLuneMath.RimLight,
                "0 must be darker than the default, not equal to it");
        }

        [Fact]
        public void TheWallKnobPreservesTheDefaultForAbsentValues()
        {
            // The stored integer is the value in thousandths of the default, so a
            // round trip has to be exact or the live pref would drift.
            Assert.Equal(SextantLuneMath.RimLight,
                SextantLuneMath.RimLightFrom((int)(SextantLuneMath.RimLight * 1000d)), 9);
        }

        [Fact]
        public void DefaultStrength_IsTheAuthorsNinetyFivePercent()
        {
            Assert.Equal(0.95, SextantLuneMath.DefaultStrength, 6);
            Assert.InRange(SextantLuneMath.DefaultStrength, 0.9, 1.0);
        }

        [Fact]
        public void TravelIsClampedToTheApertureAndRejectsNaN()
        {
            SextantLuneOffset huge = SextantLuneMath.Clamp(50d, -50d, SextantLuneMath.MaxOffsetUnits);
            Assert.True(huge.Magnitude <= SextantLuneMath.MaxOffsetUnits + Epsilon,
                "offset must stay clamped, got " + huge);
            Assert.True(SextantLuneMath.IsShadowed(-ApertureRim, ApertureRim, huge),
                "clamping keeps the direction, so the crescent side survives");

            Assert.Equal(0d, SextantLuneMath.Clamp(double.NaN, 1d, 0.15d).Magnitude, 9);
            // A degenerate bound must not disable the clamp.
            Assert.True(SextantLuneMath.Clamp(9d, 0d, 0d).Magnitude > 0d,
                "a non-positive travel must fall back to the default, not vanish");
        }

        [Fact]
        public void TheCrescentStaysInsideTheRimAtFullTravel()
        {
            // 0.34 travelled 35 % of the radius and read as a black bite; the
            // default travel keeps the far boundary just outside the field so
            // the shape still reads as a lune.
            SextantLuneOffset full = FromMotion(SextantLuneMath.RateFullDegPerSec * 4d, 0d);
            double near = 1d - SextantLuneMath.ShadowRadiusUnits + full.Magnitude;
            Assert.True(near < 0.4,
                "the shadow must not eat more than 40 % of the radius, got " + near);
        }

        [Fact]
        public void ShadowDistance_IsContinuousAcrossTheBoundary()
        {
            SextantLuneOffset offset = FromMotion(40d, 0d);
            // Probes derived from the geometry, not hard-coded: with the travel
            // dialable in PlayerPrefs a fixed radius stops meaning anything.
            double boundary = SextantLuneMath.ShadowRadiusUnits - offset.Magnitude;
            double a = SextantLuneMath.ShadowDistance(-(boundary + 0.05d), 0d, offset);
            double b = SextantLuneMath.ShadowDistance(-(boundary - 0.05d), 0d, offset);
            Assert.True(a > 0d && b < 0d, "the boundary must fall between these points");

            double previous = SextantLuneMath.ShadowDistance(-1.2d, 0d, offset);
            for (int i = 1; i <= 240; i++)
            {
                double x = -1.2d + (2.4d * i / 240d);
                double now = SextantLuneMath.ShadowDistance(x, 0d, offset);
                Assert.True(Math.Abs(now - previous) < 0.05,
                    "the field must stay smooth, jumped to " + now);
                previous = now;
            }
        }

        [Fact]
        public void BothHalvesShareOneApertureSpace()
        {
            // The two RawImages are half-width quads whose UVs are halved by
            // uvRect, so each side renders p.x in [-1,0] and [0,+1] of the SAME
            // unit disc. The one property that can fail is a SEAM: if the halves
            // ever disagreed about coordinates again, the shadow would jump at
            // the split line and the crescent would stop being one crescent.
            SextantLuneOffset offset = FromMotion(40d, 0d);
            for (double y = -0.9; y <= 0.9; y += 0.15)
            {
                double left = SextantLuneMath.ShadowDistance(-0.002d, y, offset);
                double right = SextantLuneMath.ShadowDistance(0.002d, y, offset);
                // The field is smooth, so the step across the split is bounded
                // by the field's own gradient over 0.004 of the radius.
                Assert.True(Math.Abs(left - right) < 0.02,
                    "the field must be continuous across the split at y=" + y
                        + ": " + left + " vs " + right);
            }
        }
    }
}
