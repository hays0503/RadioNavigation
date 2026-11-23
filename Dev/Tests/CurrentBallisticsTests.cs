using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CurrentBallisticsTests
    {
        [Fact]
        public void WindDeflectsAirborneShellDownwindAndDropsToZeroInCalmAir()
        {
            var velocity = new Vector3(0f, 0f, 700f);
            var crosswind = new Vector3(18f, 0f, 0f);
            float kAir = CurrentBallisticsMath.ComputeShellAirDragFactor(9.5f);

            Vector3 calm = CurrentBallisticsMath.ComputeAirWindDeltaV(velocity, Vector3.zero, kAir, 0f, 0.02f);
            Assert.Equal(0f, calm.x);
            Assert.Equal(0f, calm.y);
            Assert.Equal(0f, calm.z);

            Vector3 dv = CurrentBallisticsMath.ComputeAirWindDeltaV(velocity, crosswind, kAir, 0f, 0.02f);
            Assert.True(dv.x > 0f, "Crosswind blowing +X must deflect north-bound shell toward +X");
            Assert.Equal(0f, dv.y);

            var headwind = new Vector3(0f, 0f, -18f);
            Vector3 dvHead = CurrentBallisticsMath.ComputeAirWindDeltaV(velocity, headwind, kAir, 0f, 0.02f);
            Assert.True(dvHead.z < 0f, "Headwind must decelerate projectile along -Z");
        }

        [Fact]
        public void AirborneHedgehogDriftsSignificantlyOverFlightTime()
        {
            var muzzleVel = new Vector3(0f, 35f, 72f);
            var crosswind = new Vector3(15f, 0f, 0f);
            Vector3 drift = CurrentBallisticsMath.EstimateShellWindDisplacementXZ(
                muzzleVel, crosswind, CurrentBallisticsMath.HedgehogAirDrag, 10f);

            Assert.InRange(drift.x, 4f, 30f);
            Assert.Equal(0f, drift.y);
        }

        [Fact]
        public void SubmergedDepthChargeConvergesToCurrentAtItsDepthWithoutOvershoot()
        {
            var deepCurrent = new Vector3(1.2f, 0f, -0.4f);
            var vel = Vector3.zero;
            const float drag = 3.0f;
            const float dt = 0.02f;

            for (int i = 0; i < 200; i++)
            {
                float damp = 1f - drag * dt;
                Vector3 dv = CurrentBallisticsMath.ComputeSubmergedWaterDeltaV(
                    new Vector3(vel.x * damp, vel.y * damp, vel.z * damp), deepCurrent, drag, dt);
                vel = new Vector3(vel.x * damp + dv.x, vel.y * damp + dv.y, vel.z * damp + dv.z);
            }

            Assert.InRange(Math.Abs(vel.x - deepCurrent.x), 0f, 0.01f);
            Assert.InRange(Math.Abs(vel.z - deepCurrent.z), 0f, 0.01f);
        }

        [Fact]
        public void LowDragSubmergedOrdnanceStillCouplesToWaterWithoutExceedingCurrent()
        {
            var current = new Vector3(0.8f, 0f, 0.3f);
            var vel = Vector3.zero;
            const float lowDrag = 0.1f;
            const float dt = 0.02f;

            for (int i = 0; i < 250; i++)
            {
                float damp = 1f - lowDrag * dt;
                Vector3 dv = CurrentBallisticsMath.ComputeSubmergedWaterDeltaV(
                    new Vector3(vel.x * damp, vel.y * damp, vel.z * damp), current, lowDrag, dt);
                vel = new Vector3(vel.x * damp + dv.x, vel.y * damp + dv.y, vel.z * damp + dv.z);
            }

            Assert.InRange(Math.Abs(vel.x - current.x), 0f, 0.02f);
            Assert.InRange(Math.Abs(vel.z - current.z), 0f, 0.02f);
        }

        [Fact]
        public void HeavyShellDeflectsLessThanLightFlakShell()
        {
            float light = CurrentBallisticsMath.ComputeShellAirDragFactor(CurrentBallisticsMath.EstimateMassFromCalibreMm(20f));
            float heavy = CurrentBallisticsMath.ComputeShellAirDragFactor(CurrentBallisticsMath.EstimateMassFromCalibreMm(150f));
            Assert.True(light > heavy, "Light AA shells must deflect more than heavy deck shells");
        }

        [Fact]
        public void StrongWindLowersAccuracyCeilingAndSlowsBracketingSymmetrically()
        {
            float calmDiff = CurrentBallisticsMath.ComputeWindDifficulty(2f);
            float stormDiff = CurrentBallisticsMath.ComputeWindDifficulty(20f);
            Assert.Equal(0f, calmDiff);
            Assert.Equal(1f, stormDiff);

            float calmMax = CurrentBallisticsMath.ComputeMaxBracketingAccuracy(calmDiff);
            float stormMax = CurrentBallisticsMath.ComputeMaxBracketingAccuracy(stormDiff);
            Assert.True(calmMax > stormMax);
            Assert.InRange(stormMax, 0.65f, 0.75f);

            float calmProgress = 0f;
            float stormProgress = 0f;
            for (int i = 0; i < 3; i++)
            {
                calmProgress = CurrentBallisticsMath.AdvanceWindBracketing(calmProgress, 0.5f, 1f, calmDiff);
                stormProgress = CurrentBallisticsMath.AdvanceWindBracketing(stormProgress, 0.5f, 1f, stormDiff);
            }
            Assert.True(calmProgress > stormProgress, "Strong wind must slow down bracketing convergence");

            float calmLearned = CurrentBallisticsMath.ComputeLearnedWindFraction(calmProgress, calmDiff);
            float stormLearned = CurrentBallisticsMath.ComputeLearnedWindFraction(stormProgress, stormDiff);
            Assert.True(calmLearned > stormLearned);
        }

        [Fact]
        public void WindCompensationShiftsAimUpwindOnlyAsBracketingLearns()
        {
            var spawn = Vector3.zero;
            var target = new Vector3(0f, 0f, 2000f);
            float len = Mathf.Sqrt(0.01f + 1f);
            var vanillaDir = new Vector3(0f, 0.1f / len, 1f / len);
            var windDrift = new Vector3(18f, 0f, 0f);

            Vector3 shot0 = CurrentBallisticsMath.ApplyWindCompensation(
                vanillaDir, spawn, target, windDrift, 0f, 0f, 0f, 0f);
            Assert.InRange(Math.Abs(shot0.x - vanillaDir.x), 0f, 1e-5f);

            Vector3 shotBracketed = CurrentBallisticsMath.ApplyWindCompensation(
                vanillaDir, spawn, target, windDrift, 0.8f, 0f, 0f, 0f);
            Assert.True(shotBracketed.x < 0f, "Positive +X wind drift requires aiming upwind (-X)");
        }

        [Fact]
        public void ManeuverOrLongPauseDegradesLearnedBracketing()
        {
            float afterGap = CurrentBallisticsMath.DecayProgressOnTimeGap(0.8f, 45f);
            Assert.True(afterGap < 0.8f);

            float afterZigzag = CurrentBallisticsMath.DecayProgressOnTargetManeuver(0.8f, 8f);
            Assert.True(afterZigzag < 0.8f);

            Assert.Equal(0.8f, CurrentBallisticsMath.DecayProgressOnTimeGap(0.8f, 5f));
            Assert.Equal(0.8f, CurrentBallisticsMath.DecayProgressOnTargetManeuver(0.8f, 1f));
        }

        [Fact]
        public void TorpedoLaunchBlendPreventsDoubleCountingOwnerDriftAtTubeExit()
        {
            Assert.Equal(0f, CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(true, true, 0f, 0f));
            float halfGyro = CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(true, true, 4.75f, 0.3f);
            Assert.InRange(halfGyro, 0.49f, 0.51f);
            Assert.Equal(1f, CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(true, true, 12f, 1.0f));

            Assert.Equal(0f, CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(false, true, 0f, 0f));
            float halfCart = CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(false, true, 10f, 0.8333333f);
            Assert.InRange(halfCart, 0.49f, 0.51f);
            Assert.Equal(1f, CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(false, true, 25f, 2.0f));

            Assert.Equal(1f, CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(true, false, 0f, 0f));
        }

        [Fact]
        public void TorpedoWaterDisplacementKeepsPatternCoMovingWithUniformCurrent()
        {
            var water = new Vector3(1.5f, 0f, -0.5f);
            Vector3 disp = CurrentBallisticsMath.ComputeTorpedoWaterDisplacement(water, 1f, 0.02f);
            Assert.InRange(Math.Abs(disp.x - 0.03f), 0f, 1e-5f);
            Assert.Equal(0f, disp.y);
            Assert.InRange(Math.Abs(disp.z + 0.01f), 0f, 1e-5f);

            Vector3 calm = CurrentBallisticsMath.ComputeTorpedoWaterDisplacement(Vector3.zero, 1f, 0.02f);
            Assert.Equal(0f, calm.x);
            Assert.Equal(0f, calm.z);
        }

        [Fact]
        public void ParachuteFlareCouplesSmoothlyToWindAndFallsBackCleanlyWithoutBody()
        {
            var wind = new Vector3(-6f, 0f, 9f);
            var vel = Vector3.zero;
            const float drag = 1.5f;
            const float dt = 0.02f;

            Vector3 calm = CurrentBallisticsMath.ComputeParachuteWindDeltaV(vel, Vector3.zero, drag, dt);
            Assert.Equal(0f, calm.x);
            Assert.Equal(0f, calm.y);
            Assert.Equal(0f, calm.z);

            for (int i = 0; i < 200; i++)
            {
                float damp = 1f - drag * dt;
                Vector3 step = CurrentBallisticsMath.ComputeParachuteWindDeltaV(vel, wind, drag, dt);
                vel = new Vector3(vel.x * damp + step.x, 0f, vel.z * damp + step.z);
            }

            // Asymptote with stock drag = coupling / (drag + coupling) * wind, around half to full wind without overshoot.
            Assert.InRange(vel.x, wind.x * 1.01f, wind.x * 0.45f);
            Assert.InRange(vel.z, wind.z * 0.45f, wind.z * 1.01f);

            Vector3 shift = CurrentBallisticsMath.ComputeWindDriftDisplacement(wind, 0.25f);
            Assert.InRange(Math.Abs(shift.x - (-0.6f)), 0f, 1e-5f);
            Assert.Equal(0f, shift.y);
            Assert.InRange(Math.Abs(shift.z - 0.9f), 0f, 1e-5f);
        }

        [Fact]
        public void SeabedContactGraceTicksHoldWhileTouchingOrSleepingAndClearOnLiftoff()
        {
            int ticks = CurrentBallisticsMath.AdvanceSeabedContactTicks(0, false, true);
            Assert.Equal(CurrentBallisticsMath.SeabedContactGraceTicks, ticks);

            // Sleeping Rigidbody keeps resting on seabed across physics steps.
            ticks = CurrentBallisticsMath.AdvanceSeabedContactTicks(ticks, true, false);
            Assert.Equal(CurrentBallisticsMath.SeabedContactGraceTicks, ticks);

            // Waking up without terrain contact clears grounded flag after grace window.
            for (int i = 0; i < CurrentBallisticsMath.SeabedContactGraceTicks; i++)
                ticks = CurrentBallisticsMath.AdvanceSeabedContactTicks(ticks, false, false);
            Assert.Equal(0, ticks);
        }
    }
}
