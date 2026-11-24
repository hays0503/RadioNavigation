using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure ballistic wind, submerged ordnance drift, and symmetric gun
    /// bracketing math (C# 7.0, allocation-free, unit-testable).
    ///
    /// Design rules:
    /// 1) Airborne shells, Hedgehogs, and depth charges experience identical
    ///    physical wind drift proportional to relative air velocity.
    /// 2) Submerged depth charges and Hedgehogs experience ocean current at
    ///    their instantaneous depth, with ZERO subsurface-current cheating
    ///    in AI ASW targeting (escort operators cannot observe deep currents).
    /// 3) Surface guns (!manualAim) learn wind compensation gradually across
    ///    shots on a target ("bracketing") using identical laws for enemy
    ///    warships and player AI crew; strong winds lower the accuracy
    ///    ceiling and slow down bracketing for both sides equally.
    /// </summary>
    internal static class CurrentBallisticsMath
    {
        internal const float HedgehogAirDrag = 0.00025f;
        internal const float MinSubmergedCoupling = 2.0f;
        internal const float MinParachuteWindCoupling = 0.85f;
        internal const float CalmWindThresholdMps = 3.0f;
        internal const float StormWindSpanMps = 17.0f;
        internal const int SeabedContactGraceTicks = 3;

        internal static float ComputeWindDifficulty(float windSpeedMps)
        {
            if (float.IsNaN(windSpeedMps) || float.IsInfinity(windSpeedMps) || windSpeedMps <= CalmWindThresholdMps)
                return 0f;
            float scaled = (windSpeedMps - CalmWindThresholdMps) / StormWindSpanMps;
            return scaled >= 1f ? 1f : scaled;
        }

        internal static float ComputeShellAirDragFactor(float shellMassKg)
        {
            float effectiveMass = shellMassKg > 0.1f && !float.IsNaN(shellMassKg) && !float.IsInfinity(shellMassKg)
                ? shellMassKg
                : 10f;
            float raw = 0.00038f * (float)Math.Pow(10.0 / Math.Max(effectiveMass, 0.5), 0.22);
            return Mathf.Clamp(raw, 0.00018f, 0.00055f);
        }

        internal static float EstimateMassFromCalibreMm(float calibreMm)
        {
            if (calibreMm <= 0f || float.IsNaN(calibreMm) || float.IsInfinity(calibreMm))
                return 10f;
            float ratio = calibreMm / 88f;
            return Mathf.Clamp(9.4f * (float)Math.Pow(ratio, 2.7), 0.12f, 60f);
        }

        internal static Vector3 ComputeAirWindDeltaV(Vector3 velocity, Vector3 windMps,
            float airDragFactor, float linearDrag, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float wx = windMps.x;
            float wz = windMps.z;
            if ((wx == 0f && wz == 0f) || float.IsNaN(wx) || float.IsNaN(wz))
                return Vector3.zero;

            float stepDt = dt > 0.1f ? 0.1f : dt;
            float clampedLinear = linearDrag > 0f ? linearDrag : 0f;
            float linearStep = Mathf.Clamp01(clampedLinear * stepDt);
            float dvx = wx * linearStep;
            float dvz = wz * linearStep;

            if (airDragFactor > 0f)
            {
                Vector3 quadAccel = ComputeQuadraticAirWindAccel(velocity, windMps, airDragFactor);
                dvx += quadAccel.x * stepDt;
                dvz += quadAccel.z * stepDt;
            }
            return new Vector3(dvx, 0f, dvz);
        }

        internal static Vector3 IntegrateAirWindDeltaV(Vector3 velocity, Vector3 windMps,
            float airDragFactor, float linearDrag, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float remaining = Math.Min(dt, 1f);
            Vector3 cur = velocity;
            while (remaining > 1e-6f)
            {
                float step = remaining > 0.1f ? 0.1f : remaining;
                Vector3 dv = ComputeAirWindDeltaV(cur, windMps, airDragFactor, linearDrag, step);
                cur = new Vector3(cur.x + dv.x, cur.y, cur.z + dv.z);
                remaining -= step;
            }
            return new Vector3(cur.x - velocity.x, 0f, cur.z - velocity.z);
        }

        internal static Vector3 ComputeQuadraticAirWindAccel(Vector3 velocity, Vector3 windMps, float airDragFactor)
        {
            if (airDragFactor <= 0f)
                return Vector3.zero;
            float wx = windMps.x;
            float wz = windMps.z;
            if ((wx == 0f && wz == 0f) || float.IsNaN(wx) || float.IsNaN(wz))
                return Vector3.zero;
            float vx = velocity.x;
            float vz = velocity.z;
            float relX = vx - wx;
            float relZ = vz - wz;
            float speedH = Mathf.Sqrt(vx * vx + vz * vz);
            float relSpeed = Mathf.Sqrt(relX * relX + relZ * relZ);
            return new Vector3(
                -airDragFactor * (relSpeed * relX - speedH * vx),
                0f,
                -airDragFactor * (relSpeed * relZ - speedH * vz));
        }

        internal static Vector3 ComputeSubmergedWaterDeltaV(Vector3 velocity, Vector3 waterMps,
            float bodyDrag, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float ux = waterMps.x;
            float uz = waterMps.z;
            if (float.IsNaN(ux) || float.IsNaN(uz))
                return Vector3.zero;

            float stepDt = dt > 0.1f ? 0.1f : dt;
            float safeDrag = bodyDrag > 0f ? bodyDrag : 0f;
            float baseAlpha = Mathf.Clamp01(safeDrag * stepDt);
            float dvx = ux * baseAlpha;
            float dvz = uz * baseAlpha;

            if (safeDrag < MinSubmergedCoupling)
            {
                float extraAlpha = Mathf.Clamp01((MinSubmergedCoupling - safeDrag) * stepDt);
                dvx += (ux - velocity.x) * extraAlpha;
                dvz += (uz - velocity.z) * extraAlpha;
            }
            return new Vector3(dvx, 0f, dvz);
        }

        internal static Vector3 ComputeParachuteWindDeltaV(Vector3 velocity, Vector3 windMps,
            float bodyDrag, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float wx = windMps.x;
            float wz = windMps.z;
            if ((wx == 0f && wz == 0f) || float.IsNaN(wx) || float.IsNaN(wz))
                return Vector3.zero;

            float stepDt = dt > 0.1f ? 0.1f : dt;
            float coupling = bodyDrag > MinParachuteWindCoupling ? bodyDrag : MinParachuteWindCoupling;
            float alpha = Mathf.Clamp01(coupling * stepDt);
            return new Vector3((wx - velocity.x) * alpha, 0f, (wz - velocity.z) * alpha);
        }

        internal static Vector3 IntegrateParachuteWindDeltaV(Vector3 velocity, Vector3 windMps,
            float bodyDrag, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float remaining = Math.Min(dt, 1f);
            Vector3 cur = velocity;
            while (remaining > 1e-6f)
            {
                float step = remaining > 0.1f ? 0.1f : remaining;
                Vector3 dv = ComputeParachuteWindDeltaV(cur, windMps, bodyDrag, step);
                cur = new Vector3(cur.x + dv.x, cur.y, cur.z + dv.z);
                remaining -= step;
            }
            return new Vector3(cur.x - velocity.x, 0f, cur.z - velocity.z);
        }

        internal static int AdvanceSeabedContactTicks(int currentTicks, bool sleeping, bool terrainContact)
        {
            if (terrainContact)
                return SeabedContactGraceTicks;
            if (currentTicks <= 0)
                return 0;
            return sleeping ? currentTicks : currentTicks - 1;
        }

        internal static float ComputeTorpedoLaunchWaterBlend(bool hasGyroAngle, bool hasOwnerShipVelocity,
            float passedDistanceMeters, float elapsedSeconds)
        {
            if (!hasOwnerShipVelocity)
                return 1f;
            if (hasGyroAngle)
                return passedDistanceMeters < 9.5f ? Mathf.Clamp01(passedDistanceMeters / 9.5f) : 1f;
            return Mathf.Clamp01(elapsedSeconds * 0.6f);
        }

        internal static Vector3 ComputeTorpedoWaterDisplacement(Vector3 waterMps, float blend, float dt)
        {
            return ScaleHorizontalStep(waterMps, blend, dt);
        }

        internal static Vector3 ComputeWindDriftDisplacement(Vector3 windMps, float dt)
        {
            return ScaleHorizontalStep(windMps, 1f, dt);
        }

        internal static Vector3 IntegrateWindDriftDisplacement(Vector3 windMps, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float remaining = Math.Min(dt, 1f);
            float tx = 0f, tz = 0f;
            while (remaining > 1e-6f)
            {
                float step = remaining > 0.1f ? 0.1f : remaining;
                Vector3 s = ScaleHorizontalStep(windMps, 1f, step);
                tx += s.x;
                tz += s.z;
                remaining -= step;
            }
            return new Vector3(tx, 0f, tz);
        }

        private static Vector3 ScaleHorizontalStep(Vector3 flowMps, float blend, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return Vector3.zero;
            float fx = flowMps.x;
            float fz = flowMps.z;
            if ((fx == 0f && fz == 0f) || float.IsNaN(fx) || float.IsNaN(fz))
                return Vector3.zero;
            float stepDt = dt > 0.1f ? 0.1f : dt;
            float scale = Mathf.Clamp01(blend) * stepDt;
            return new Vector3(fx * scale, 0f, fz * scale);
        }

        internal static Vector3 EstimateShellWindDisplacementXZ(Vector3 muzzleVelocity, Vector3 windMps,
            float airDragFactor, float flightTimeSeconds)
        {
            if (flightTimeSeconds <= 0f || float.IsNaN(flightTimeSeconds) || float.IsInfinity(flightTimeSeconds))
                return Vector3.zero;
            Vector3 a1 = ComputeQuadraticAirWindAccel(muzzleVelocity, windMps, airDragFactor);
            float dtMid = flightTimeSeconds * 0.5f;
            var midVel = new Vector3(
                muzzleVelocity.x + a1.x * dtMid,
                muzzleVelocity.y,
                muzzleVelocity.z + a1.z * dtMid);
            Vector3 a2 = ComputeQuadraticAirWindAccel(midVel, windMps, airDragFactor);
            float avgAx = 0.5f * (a1.x + a2.x);
            float avgAz = 0.5f * (a1.z + a2.z);
            float halfT2 = 0.5f * flightTimeSeconds * flightTimeSeconds;
            return new Vector3(avgAx * halfT2, 0f, avgAz * halfT2);
        }

        internal static float ComputeMaxBracketingAccuracy(float windDifficulty)
        {
            float clamped = Mathf.Clamp01(windDifficulty);
            return Mathf.Clamp01(1f - 0.30f * clamped);
        }

        internal static float ComputeBracketingRateMultiplier(float windDifficulty)
        {
            float clamped = Mathf.Clamp01(windDifficulty);
            return Mathf.Lerp(1f, 0.45f, clamped);
        }

        internal static float ComputeLearnedWindFraction(float windProgress, float windDifficulty)
        {
            float maxKnowledge = Mathf.Lerp(0.95f, 0.72f, Mathf.Clamp01(windDifficulty));
            return Mathf.Clamp01(windProgress) * maxKnowledge;
        }

        internal static float AdvanceWindBracketing(float currentProgress, float crewAccuracy,
            float magazineSize, float windDifficulty)
        {
            float clampedProgress = Mathf.Clamp01(currentProgress);
            float clampedCrew = Mathf.Clamp01(crewAccuracy);
            float rateMult = ComputeBracketingRateMultiplier(windDifficulty);
            float burstScale = magazineSize > 4f ? Mathf.Clamp(4f / magazineSize, 0.35f, 1f) : 1f;
            float stepGain = (0.18f + 0.16f * clampedCrew) * rateMult * burstScale;
            return Mathf.Clamp01(clampedProgress + (1f - clampedProgress) * stepGain);
        }

        internal static float DecayProgressOnTimeGap(float currentProgress, float elapsedSeconds)
        {
            if (elapsedSeconds <= 15f)
                return Mathf.Clamp01(currentProgress);
            return Mathf.Clamp01(currentProgress * Mathf.Exp(-(elapsedSeconds - 15f) / 30f));
        }

        internal static float DecayProgressOnTargetManeuver(float currentProgress, float deltaSpeedMps)
        {
            if (deltaSpeedMps <= 2.5f)
                return Mathf.Clamp01(currentProgress);
            float loss = Mathf.Clamp01((deltaSpeedMps - 2.5f) / 10f) * 0.45f;
            return Mathf.Clamp01(currentProgress * (1f - loss));
        }

        internal static Vector3 ApplyWindCompensation(Vector3 vanillaLaunchDir, Vector3 spawnPosition,
            Vector3 targetPoint, Vector3 predictedWindDriftXZ, float learnedFraction,
            float windDifficulty, float gaussX, float gaussY)
        {
            if (!TryNormalize(vanillaLaunchDir, out Vector3 dir))
                return vanillaLaunchDir;

            float htx = targetPoint.x - spawnPosition.x;
            float htz = targetPoint.z - spawnPosition.z;
            float range = Mathf.Sqrt(htx * htx + htz * htz);
            if (range > 1f)
                dir = CompensateAimVector(dir, range, predictedWindDriftXZ, learnedFraction);

            float gustSigma = 0.0085f * Mathf.Clamp01(windDifficulty) * (1f - 0.35f * Mathf.Clamp01(learnedFraction));
            if (gustSigma > 0f)
                dir = ApplyGustJitter(dir, gustSigma, gaussX, gaussY);

            return TryNormalize(dir, out Vector3 normalized) ? normalized : dir;
        }

        private static Vector3 CompensateAimVector(Vector3 dir, float range,
            Vector3 predictedWindDriftXZ, float learnedFraction)
        {
            float horizLen = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
            if (horizLen <= 1e-4f)
                return dir;

            // Project horizontal range along vanillaLaunchDir's (dir.x, dir.z)
            // so target-velocity lead and vanilla spread stay intact instead of
            // reverting to the unled bearing toward targetPoint.
            float vx = (dir.x / horizLen) * range;
            float vz = (dir.z / horizLen) * range;
            float cx = vx - learnedFraction * predictedWindDriftXZ.x;
            float cz = vz - learnedFraction * predictedWindDriftXZ.z;
            float compRange = Mathf.Sqrt(cx * cx + cz * cz);
            if (compRange <= 1f)
                return dir;

            float ax = cx / compRange;
            float az = cz / compRange;
            float sinElev = Mathf.Clamp(dir.y, -0.95f, 0.95f);
            float elevRad = (float)Math.Asin(sinElev);
            float rangeDelta = Mathf.Clamp((compRange - range) / range, -0.25f, 0.25f);
            float sin2 = (float)Math.Sin(2.0 * Math.Abs(elevRad));
            float newElev = Mathf.Clamp(elevRad + 0.5f * sin2 * rangeDelta, -0.35f, 1.35f);
            float cosNew = (float)Math.Cos(newElev);
            return new Vector3(ax * cosNew, (float)Math.Sin(newElev), az * cosNew);
        }

        private static Vector3 ApplyGustJitter(Vector3 dir, float gustSigma, float gaussX, float gaussY)
        {
            // Cross(up(0,1,0), dir) = (dir.z, 0, -dir.x)
            float rx = dir.z;
            float rz = -dir.x;
            float rLen = Mathf.Sqrt(rx * rx + rz * rz);
            if (rLen <= 1e-3f)
                return dir;
            rx /= rLen;
            rz /= rLen;
            // Cross(dir, right): right = (rx, 0, rz)
            float ux = dir.y * rz;
            float uy = dir.z * rx - dir.x * rz;
            float uz = -dir.y * rx;
            float sx = gaussX * gustSigma;
            float sy = gaussY * gustSigma * 0.45f;
            return new Vector3(
                dir.x + rx * sx + ux * sy,
                dir.y + uy * sy,
                dir.z + rz * sx + uz * sy);
        }

        private static bool TryNormalize(Vector3 v, out Vector3 unit)
        {
            float lenSq = v.x * v.x + v.y * v.y + v.z * v.z;
            if (lenSq <= 1e-8f)
            {
                unit = Vector3.zero;
                return false;
            }
            float inv = 1f / Mathf.Sqrt(lenSq);
            unit = new Vector3(v.x * inv, v.y * inv, v.z * inv);
            return true;
        }
    }
}
