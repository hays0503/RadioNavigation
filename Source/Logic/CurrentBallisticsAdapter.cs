using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using SceneFlare = UBOAT.Game.Scene.Effects.Flare;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Installs symmetric ballistic wind on shells/hedgehogs/depth-charges,
    /// depth-sampled ocean currents on underwater hedgehogs and depth charges
    /// (with zero subsurface knowledge given to ASW operators), and symmetric
    /// wind-bracketing rules for surface gunnery.
    /// </summary>
    public static class CurrentBallisticsAdapter
    {
        private const string LogPrefix = "[RadioNav][CURRENT][BALLISTICS] ";
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags DeclaredInstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static bool installed;
        private static bool torpedoInstalled;
        private static bool decoyInstalled;
        private static bool flareInstalled;
        private static FieldInfo shellsField;
        private static FieldInfo splashedField;
        private static FieldInfo calibreField;
        private static FieldInfo shellSpeedField;
        private static FieldInfo magazineSizeField;
        private static FieldInfo parentObservatorField;
        private static FieldInfo decoyBodyField;
        private static FieldInfo torpedoBodyField;
        private static FieldInfo torpedoDudField;
        private static FieldInfo torpedoGyroField;
        private static FieldInfo torpedoPassedDistField;
        private static FieldInfo torpedoLaunchTimeField;
        private static FieldInfo torpedoOwnerField;
        private static FieldInfo torpedoSpawnedField;
        private static GameTime gameTime;
        private static Weather weather;

        private static readonly Dictionary<Gun, GunBracketingState> Guns = new Dictionary<Gun, GunBracketingState>();
        private static readonly List<Gun> ExpiredGuns = new List<Gun>();
        private static readonly Dictionary<SceneFlare, Rigidbody> FlareBodies = new Dictionary<SceneFlare, Rigidbody>();
        private static readonly List<SceneFlare> ExpiredFlares = new List<SceneFlare>();

        private sealed class GunBracketingState
        {
            internal Entity LastTarget;
            internal Vector3 LastTargetVelocity;
            internal double LastShotGameSeconds;
            internal float WindProgress;
            internal int ShotsAtTarget;
        }

        private struct FireState
        {
            internal bool Tracked;
            internal float AccuracyBefore;
            internal float WindSpeedMps;
        }

        internal static void Reset()
        {
            installed = false;
            torpedoInstalled = false;
            decoyInstalled = false;
            flareInstalled = false;
            gameTime = null;
            weather = null;
            Guns.Clear();
            ExpiredGuns.Clear();
            FlareBodies.Clear();
            ExpiredFlares.Clear();
        }

        internal static void TryInstall(Harmony harmony)
        {
            bool wantBallistics = CurrentSettings.BallisticWindEnabled();
            bool wantTorpedoDrift = wantBallistics || CurrentSettings.NearbyDriftEnabled();
            if (wantTorpedoDrift && !torpedoInstalled)
                TryInstallTorpedoDrift(harmony);
            if (wantTorpedoDrift && !decoyInstalled)
                TryInstallSonarDecoyDrift(harmony);
            if (wantBallistics && !flareInstalled)
                TryInstallFlareWindDrift(harmony);
            if (!installed && wantBallistics)
                InstallOrdnanceAndGunHooks(harmony);
        }

        private static void InstallOrdnanceAndGunHooks(Harmony harmony)
        {
            MethodInfo updateShells = typeof(Shell).GetMethod("UpdateShells", AnyStatic);
            MethodInfo dcFixedUpdate = typeof(DepthCharge).GetMethod("FixedUpdate", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            MethodInfo hhFixedUpdate = typeof(HedgehogProjectile).GetMethod("FixedUpdate", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            MethodInfo getDirection = typeof(Gun).GetMethod("GetDirection", InstanceNonPublic, null, new[] { typeof(Entity), typeof(Vector3) }, null);
            MethodInfo gunFire = typeof(Gun).GetMethod("Fire", InstanceNonPublic);

            shellsField = typeof(Shell).GetField("shells", AnyStatic);
            splashedField = typeof(Shell).GetField("splashed", InstanceNonPublic);
            calibreField = typeof(Gun).GetField("calibre", InstanceNonPublic);
            shellSpeedField = typeof(Gun).GetField("shellSpeed", InstanceNonPublic);
            magazineSizeField = typeof(Gun).GetField("magazineSize", InstanceNonPublic);
            parentObservatorField = typeof(Gun).GetField("parentObservator", InstanceNonPublic);

            if (updateShells == null || dcFixedUpdate == null || hhFixedUpdate == null
                || getDirection == null || gunFire == null
                || shellsField == null || splashedField == null)
            {
                Debug.LogWarning(LogPrefix + "required hooks missing; ballistic wind & bracketing disabled");
                return;
            }

            try
            {
                var afterShells = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterUpdateShells), AnyStatic));
                var afterDc = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterDepthChargeFixedUpdate), AnyStatic));
                var afterHh = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterHedgehogFixedUpdate), AnyStatic));
                var afterDir = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterGetDirection), AnyStatic));
                var beforeFire = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(BeforeFire), AnyStatic));
                var afterFire = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterFire), AnyStatic));

                harmony.Patch(updateShells, postfix: afterShells);
                harmony.Patch(dcFixedUpdate, postfix: afterDc);
                harmony.Patch(hhFixedUpdate, postfix: afterHh);
                harmony.Patch(getDirection, postfix: afterDir);
                harmony.Patch(gunFire, prefix: beforeFire, postfix: afterFire);

                installed = true;
                Debug.Log(LogPrefix + "ballistic wind, subsurface ordnance drift, and symmetric gun bracketing installed");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "patch failed: " + ex.Message);
            }
        }

        private static void TryInstallTorpedoDrift(Harmony harmony)
        {
            MethodInfo torpedoFixed = typeof(Torpedo).GetMethod("FixedUpdate", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            torpedoBodyField = typeof(Torpedo).GetField("rigidBody", InstanceNonPublic);
            torpedoDudField = typeof(Torpedo).GetField("reportedAsDud", InstanceNonPublic);
            torpedoGyroField = typeof(Torpedo).GetField("gyroAngle", InstanceNonPublic);
            torpedoPassedDistField = typeof(Torpedo).GetField("passedDistance", InstanceNonPublic);
            torpedoLaunchTimeField = typeof(Torpedo).GetField("launchTime", InstanceNonPublic);
            torpedoOwnerField = typeof(Torpedo).GetField("owner", InstanceNonPublic);
            torpedoSpawnedField = typeof(Torpedo).GetField("wasSpawnedInsteadLaunched", InstanceNonPublic);

            if (torpedoFixed == null || torpedoBodyField == null || torpedoDudField == null
                || torpedoGyroField == null || torpedoPassedDistField == null
                || torpedoLaunchTimeField == null || torpedoOwnerField == null || torpedoSpawnedField == null)
            {
                Debug.LogWarning(LogPrefix + "torpedo hooks missing; torpedo current drift disabled");
                return;
            }

            try
            {
                var afterTorpedo = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterTorpedoFixedUpdate), AnyStatic));
                harmony.Patch(torpedoFixed, postfix: afterTorpedo);
                torpedoInstalled = true;
                Debug.Log(LogPrefix + "torpedo depth-current drift installed");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "torpedo patch failed: " + ex.Message);
            }
        }

        private static void TryInstallSonarDecoyDrift(Harmony harmony)
        {
            MethodInfo decoyFixed = typeof(SonarDecoy).GetMethod("FixedUpdate", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            decoyBodyField = typeof(SonarDecoy).GetField("decoyRigidBody", InstanceNonPublic);
            if (decoyFixed == null || decoyBodyField == null)
            {
                Debug.LogWarning(LogPrefix + "sonar decoy hooks missing; decoy current drift disabled");
                return;
            }

            try
            {
                var afterDecoy = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterSonarDecoyFixedUpdate), AnyStatic));
                harmony.Patch(decoyFixed, postfix: afterDecoy);
                decoyInstalled = true;
                Debug.Log(LogPrefix + "sonar decoy depth-current drift installed");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "sonar decoy patch failed: " + ex.Message);
            }
        }

        private static void TryInstallFlareWindDrift(Harmony harmony)
        {
            MethodInfo flareStart = typeof(SceneFlare).GetMethod("Start", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            MethodInfo flareDestroy = typeof(SceneFlare).GetMethod("OnDestroy", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            MethodInfo flareUpdate = typeof(SceneFlare).GetMethod("Update", DeclaredInstanceNonPublic, null, Type.EmptyTypes, null);
            if (flareStart == null || flareDestroy == null || flareUpdate == null)
            {
                Debug.LogWarning(LogPrefix + "flare hooks missing; parachute flare wind drift disabled");
                return;
            }

            try
            {
                var afterStart = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterFlareStart), AnyStatic));
                var afterDestroy = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterFlareOnDestroy), AnyStatic));
                var afterUpdate = new HarmonyMethod(typeof(CurrentBallisticsAdapter).GetMethod(nameof(AfterFlareUpdate), AnyStatic));
                harmony.Patch(flareStart, postfix: afterStart);
                harmony.Patch(flareDestroy, postfix: afterDestroy);
                harmony.Patch(flareUpdate, postfix: afterUpdate);
                flareInstalled = true;
                Debug.Log(LogPrefix + "parachute flare wind drift installed");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "flare patch failed: " + ex.Message);
            }
        }

        private static void AfterSonarDecoyFixedUpdate(SonarDecoy __instance)
        {
            if (!decoyInstalled || !__instance)
                return;
            if (!(decoyBodyField?.GetValue(__instance) is Rigidbody body) || body.isKinematic)
                return;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            float surfaceY = ResolveSeaSurfaceY();
            Vector3 pos = body.position;
            if (pos.y > surfaceY)
                return;

            float depthMeters = Mathf.Max(0f, surfaceY - pos.y);
            if (!CurrentRuntimeAdapter.TrySampleWaterAtDepth(pos, depthMeters, out Vector3 waterMps))
                return;

            Vector3 dv = CurrentBallisticsMath.ComputeSubmergedWaterDeltaV(body.velocity, waterMps, body.drag, dt);
            if (dv.x != 0f || dv.z != 0f)
                body.velocity += dv;
        }

        private static void AfterFlareStart(SceneFlare __instance)
        {
            if (!flareInstalled || !__instance)
                return;
            if (FlareBodies.Count > 16)
                PruneExpiredFlares();
            FlareBodies[__instance] = __instance.GetComponent<Rigidbody>();
        }

        private static void AfterFlareOnDestroy(SceneFlare __instance)
        {
            if (__instance != null)
                FlareBodies.Remove(__instance);
        }

        private static void AfterFlareUpdate(SceneFlare __instance)
        {
            if (!flareInstalled || !__instance)
                return;
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 windMps = CurrentRuntimeAdapter.CurrentWindMps;
            if (windMps.x == 0f && windMps.z == 0f)
                return;

            FlareBodies.TryGetValue(__instance, out Rigidbody body);
            if ((bool)body && !body.isKinematic)
            {
                Vector3 dv = CurrentBallisticsMath.ComputeParachuteWindDeltaV(body.velocity, windMps, body.drag, dt);
                if (dv.x != 0f || dv.z != 0f)
                    body.velocity += dv;
                return;
            }

            Vector3 shift = CurrentBallisticsMath.ComputeWindDriftDisplacement(windMps, dt);
            if (shift.x != 0f || shift.z != 0f)
                __instance.transform.position += shift;
        }

        private static void PruneExpiredFlares()
        {
            ExpiredFlares.Clear();
            foreach (SceneFlare flare in FlareBodies.Keys)
            {
                if (!flare)
                    ExpiredFlares.Add(flare);
            }
            for (int i = 0; i < ExpiredFlares.Count; i++)
                FlareBodies.Remove(ExpiredFlares[i]);
            ExpiredFlares.Clear();
        }

        private static void AfterTorpedoFixedUpdate(Torpedo __instance)
        {
            if (!torpedoInstalled || !__instance)
                return;
            if ((bool)torpedoDudField.GetValue(__instance))
                return;
            if (!(torpedoBodyField.GetValue(__instance) is Rigidbody body))
                return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            float surfaceY = ResolveSeaSurfaceY();
            Vector3 pos = body.position;
            if (pos.y > surfaceY)
                return;

            float depthMeters = Mathf.Max(0f, surfaceY - pos.y);
            if (!CurrentRuntimeAdapter.TrySampleWaterAtDepth(pos, depthMeters, out Vector3 waterMps))
                return;

            float gyro = (float)torpedoGyroField.GetValue(__instance);
            float passed = (float)torpedoPassedDistField.GetValue(__instance);
            long launchTicks = (long)torpedoLaunchTimeField.GetValue(__instance);
            bool spawned = (bool)torpedoSpawnedField.GetValue(__instance);
            Entity owner = torpedoOwnerField.GetValue(__instance) as Entity;
            bool hasOwnerVel = !spawned && owner is Ship ship && ship.RigidBody != null;

            float elapsedSec = ResolveElapsedLaunchSeconds(launchTicks);
            float blend = CurrentBallisticsMath.ComputeTorpedoLaunchWaterBlend(
                !float.IsNaN(gyro), hasOwnerVel, passed, elapsedSec);

            Vector3 delta = CurrentBallisticsMath.ComputeTorpedoWaterDisplacement(waterMps, blend, dt);
            if (delta.x != 0f || delta.z != 0f)
                body.position += delta;
        }

        private static float ResolveElapsedLaunchSeconds(long launchTicks)
        {
            if (!gameTime)
                gameTime = InjectionFramework.Instance?.GetInstance<GameTime>();
            if (!gameTime)
                return 10f;
            return Mathf.Max(0f, (float)((gameTime.Ticks - launchTicks) * 1e-7));
        }

        private static void AfterUpdateShells()
        {
            if (!installed || shellsField == null || splashedField == null)
                return;
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 windMps = CurrentRuntimeAdapter.CurrentWindMps;
            if (windMps.x == 0f && windMps.z == 0f)
                return;

            if (!(shellsField.GetValue(null) is HashSet<Shell> shells) || shells.Count == 0)
                return;

            foreach (Shell shell in shells)
                ApplyShellAirWind(shell, windMps, dt);
        }

        private static void ApplyShellAirWind(Shell shell, Vector3 windMps, float dt)
        {
            if (shell == null || shell is DepthCharge || shell is HedgehogProjectile)
                return;
            if ((bool)splashedField.GetValue(shell))
                return;
            Rigidbody body = shell.RigidBody;
            if (!body || body.isKinematic)
                return;

            float kAir = CurrentBallisticsMath.ComputeShellAirDragFactor(body.mass);
            Vector3 dv = CurrentBallisticsMath.ComputeAirWindDeltaV(body.velocity, windMps, kAir, body.drag, dt);
            if (dv.x != 0f || dv.z != 0f)
                body.velocity += dv;
        }

        private static void AfterDepthChargeFixedUpdate(DepthCharge __instance)
        {
            ApplyOrdnanceStep(__instance, 0.00018f);
        }

        private static void AfterHedgehogFixedUpdate(HedgehogProjectile __instance)
        {
            ApplyOrdnanceStep(__instance, CurrentBallisticsMath.HedgehogAirDrag);
        }

        private static void ApplyOrdnanceStep(Shell shell, float airDragFactor)
        {
            if (!installed || shell == null || splashedField == null)
                return;
            Rigidbody body = shell.RigidBody;
            if (!body || body.isKinematic)
                return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            float surfaceY = ResolveSeaSurfaceY();
            Vector3 pos = body.position;
            bool submerged = pos.y <= surfaceY || (bool)splashedField.GetValue(shell);
            if (!submerged)
            {
                Vector3 windMps = CurrentRuntimeAdapter.CurrentWindMps;
                Vector3 dvAir = CurrentBallisticsMath.ComputeAirWindDeltaV(body.velocity, windMps, airDragFactor, body.drag, dt);
                if (dvAir.x != 0f || dvAir.z != 0f)
                    body.velocity += dvAir;
                return;
            }

            float depthMeters = Mathf.Max(0f, surfaceY - pos.y);
            if (CurrentRuntimeAdapter.TrySampleWaterAtDepth(pos, depthMeters, out Vector3 waterMps))
            {
                Vector3 dvWater = CurrentBallisticsMath.ComputeSubmergedWaterDeltaV(body.velocity, waterMps, body.drag, dt);
                if (dvWater.x != 0f || dvWater.z != 0f)
                    body.velocity += dvWater;
            }
        }

        private static void AfterGetDirection(Gun __instance, Entity target, Vector3 spawnPosition, ref Vector3 __result)
        {
            if (!installed || __instance == null || target == null || __instance.ManualAim)
                return;

            Vector3 windMps = CurrentRuntimeAdapter.CurrentWindMps;
            float windSpeed = windMps.magnitude;
            if (windSpeed <= 0.1f)
                return;

            GunBracketingState state = GetOrCreateState(__instance, target);
            float shellSpeed = ReadShellSpeed(__instance);
            float calibreMm = ReadCalibreMm(__instance);
            float massKg = CurrentBallisticsMath.EstimateMassFromCalibreMm(calibreMm);
            float kAir = CurrentBallisticsMath.ComputeShellAirDragFactor(massKg);
            float windDifficulty = CurrentBallisticsMath.ComputeWindDifficulty(windSpeed);

            Vector3 targetPos = target.transform.position;
            float dist = (targetPos - spawnPosition).magnitude;
            float tof = dist / Mathf.Max(shellSpeed, 1f);
            Vector3 predictedDrift = CurrentBallisticsMath.EstimateShellWindDisplacementXZ(
                __result * shellSpeed, windMps, kAir, tof);

            float learnedFraction = state.ShotsAtTarget <= 0
                ? 0f
                : CurrentBallisticsMath.ComputeLearnedWindFraction(state.WindProgress, windDifficulty);

            __result = CurrentBallisticsMath.ApplyWindCompensation(
                __result, spawnPosition, targetPos, predictedDrift, learnedFraction,
                windDifficulty, SampleGaussian(), SampleGaussian());
        }

        private static void BeforeFire(Gun __instance, Entity target, out FireState __state)
        {
            __state = default(FireState);
            if (!installed || !__instance || __instance.ManualAim || !target)
                return;

            GunBracketingState state = GetOrCreateState(__instance, target);
            double nowSec = ResolveGameSeconds();
            Vector3 targetVel = ReadTargetVelocity(target);

            if (!ReferenceEquals(state.LastTarget, target))
            {
                bool isPlayer = __instance.ParentEntity is PlayerShip;
                float startFloor = isPlayer ? 0.25f : 0.10f;
                __instance.CurrentAccuracy = Mathf.Max(startFloor, __instance.CurrentAccuracy * 0.55f);
                state.LastTarget = target;
                state.LastTargetVelocity = targetVel;
                state.WindProgress = 0f;
                state.ShotsAtTarget = 0;
            }
            else
            {
                if (state.LastShotGameSeconds > 0.0 && nowSec > state.LastShotGameSeconds)
                    state.WindProgress = CurrentBallisticsMath.DecayProgressOnTimeGap(
                        state.WindProgress, (float)(nowSec - state.LastShotGameSeconds));
                float deltaV = (targetVel - state.LastTargetVelocity).magnitude;
                state.WindProgress = CurrentBallisticsMath.DecayProgressOnTargetManeuver(state.WindProgress, deltaV);
                state.LastTargetVelocity = targetVel;
            }

            state.LastShotGameSeconds = nowSec;
            __state.Tracked = true;
            __state.AccuracyBefore = __instance.CurrentAccuracy;
            __state.WindSpeedMps = CurrentRuntimeAdapter.CurrentWindMps.magnitude;
        }

        private static void AfterFire(Gun __instance, Shell __result, FireState __state)
        {
            if (!installed || !__state.Tracked || __instance == null)
                return;

            float windDifficulty = CurrentBallisticsMath.ComputeWindDifficulty(__state.WindSpeedMps);
            float maxAcc = CurrentBallisticsMath.ComputeMaxBracketingAccuracy(windDifficulty);
            float vanillaDelta = __instance.CurrentAccuracy - __state.AccuracyBefore;
            if (vanillaDelta > 0f)
            {
                float rateMult = CurrentBallisticsMath.ComputeBracketingRateMultiplier(windDifficulty);
                float scaled = __state.AccuracyBefore + vanillaDelta * rateMult;
                __instance.CurrentAccuracy = Mathf.Min(maxAcc, scaled);
            }
            else if (__instance.CurrentAccuracy > maxAcc)
            {
                __instance.CurrentAccuracy = maxAcc;
            }

            if (__result != null && Guns.TryGetValue(__instance, out GunBracketingState state))
            {
                float crewAcc = ReadCrewAccuracy(__instance);
                float magSize = ReadMagazineSize(__instance);
                state.WindProgress = CurrentBallisticsMath.AdvanceWindBracketing(
                    state.WindProgress, crewAcc, magSize, windDifficulty);
                state.ShotsAtTarget++;
            }
        }

        private static GunBracketingState GetOrCreateState(Gun gun, Entity target)
        {
            if (Guns.Count > 64)
                PruneExpiredGuns();
            if (!Guns.TryGetValue(gun, out GunBracketingState state))
            {
                state = new GunBracketingState
                {
                    LastTarget = target,
                    LastTargetVelocity = ReadTargetVelocity(target)
                };
                Guns[gun] = state;
            }
            return state;
        }

        private static void PruneExpiredGuns()
        {
            ExpiredGuns.Clear();
            foreach (Gun gun in Guns.Keys)
            {
                if (gun == null)
                    ExpiredGuns.Add(gun);
            }
            for (int i = 0; i < ExpiredGuns.Count; i++)
                Guns.Remove(ExpiredGuns[i]);
            ExpiredGuns.Clear();
        }

        private static float ResolveSeaSurfaceY()
        {
            if (weather == null)
                weather = InjectionFramework.Instance?.GetInstance<Weather>();
            return weather?.Sea != null ? weather.Sea.VerticalOffset : 0f;
        }

        private static double ResolveGameSeconds()
        {
            if (!gameTime)
                gameTime = InjectionFramework.Instance?.GetInstance<GameTime>();
            return gameTime ? gameTime.Ticks * 1e-7 : (double)Time.realtimeSinceStartup;
        }

        private static Vector3 ReadTargetVelocity(Entity target)
        {
            if (target == null)
                return Vector3.zero;
            Rigidbody body = target.GetComponent<Rigidbody>();
            return body != null ? body.velocity : Vector3.zero;
        }

        private static float ReadShellSpeed(Gun gun)
        {
            if (shellSpeedField == null)
                return 800f;
            return (float)shellSpeedField.GetValue(gun);
        }

        private static float ReadCalibreMm(Gun gun)
        {
            if (calibreField == null)
                return 88f;
            return calibreField.GetValue(gun) is Parameter param ? param.Value : 88f;
        }

        private static float ReadMagazineSize(Gun gun)
        {
            if (magazineSizeField == null)
                return 1f;
            return magazineSizeField.GetValue(gun) is Parameter param ? Mathf.Max(1f, param.Value) : 1f;
        }

        private static float ReadCrewAccuracy(Gun gun)
        {
            if (parentObservatorField == null)
                return 0.5f;
            return parentObservatorField.GetValue(gun) is Observator obs ? Mathf.Clamp01(obs.CrewAccuracy) : 0.5f;
        }

        private static float SampleGaussian()
        {
            float u1 = Mathf.Max(1e-4f, UnityEngine.Random.value);
            float u2 = UnityEngine.Random.value;
            return Mathf.Clamp(Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(6.2831855f * u2), -2.5f, 2.5f);
        }
    }
}
