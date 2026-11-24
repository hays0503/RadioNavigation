using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using PlayWay.Water.Extensions;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Environment.Terrain;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Runtime current transport for the player hull (plan §13.2, B1.1).
    /// Opt-in water velocity, either a constant field or a depth profile,
    /// plus direct scene wind. The profile makes surface flow stop at depth
    /// instead of being copied down the water column. With no flow and no
    /// wind, including explicit zero water, no detour is installed.
    /// Otherwise apply one difference against the still-media model on the
    /// same input state and keep native waves and drag.
    /// </summary>
    public static class CurrentRuntimeAdapter
    {
        private const string LogPrefix = "[RadioNav][CURRENT] ";
        private const string BinderName = "RadioNavCurrentBinder";
        private const int NearbyLimit = 16;

        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels");
        private static readonly Type HullType = typeof(NeuralNetworkPhysics);

        private static NeuralNetworkPhysics target;
        private static PlayerShip boundShip;
        private static ICurrentField currentField;
        private static string fieldKind = "none";
        private static string fieldSummary;
        private static GameTime gameTime;
        private static double lastLatitude;
        private static double lastLongitude;
        private static bool lastPositionValid;
        private static Vector3 water;
        private static Vector3 wind;
        private static float exposure;
        private static bool docked;
        private static bool grounded;
        private static int seabedContactTicks;
        private static PlayerShip collisionHookShip;
        private static bool useWind;
        private static Weather weather;
        private static Transform windDirection;
        private static float nextWeatherSample;
        private static float nextFieldSample;
        private static CurrentSurfaceDragStep step;
        private static FieldInfo playerShipField;
        private static int appliedFull;
        private static int appliedSimple;
        private static int appliedAir;
        private static int appliedWater;
        private static Vector3 lastAir;
        private static bool useNearby;
        private static bool useRelativeRudder;
        private static bool useBallistic;
        private static float nextNearbyScan;
        private static readonly Dictionary<NeuralNetworkPhysics, NearbyDriftHull> Nearby =
            new Dictionary<NeuralNetworkPhysics, NearbyDriftHull>();
        private static readonly List<NeuralNetworkPhysics> Expired = new List<NeuralNetworkPhysics>();

        public static void EnsureRunning(Harmony harmony)
        {
            // A binder from a previous assembly copy survives scene loads
            // (DontDestroyOnLoad) but its patches are already unpatched;
            // remove it on every mod load, enabled or not.
            GameObject stale = GameObject.Find(BinderName);
            if (stale != null)
                Object.Destroy(stale);

            BindCollisionHooks(null);
            CurrentRudderAdapter.ClearNearby();
            CurrentDiagnostics.EnsureRegistered();
            if (!CurrentSettings.IsMasterEnabled())
            {
                currentField = null;
                fieldKind = "none";
                fieldSummary = null;
                useWind = false;
                useNearby = false;
                useRelativeRudder = false;
                useBallistic = false;
                Debug.Log(LogPrefix + "disabled (RADIONAV_CURRENT_FIELD unset); currents off; stock physics untouched");
                return;
            }
            useWind = CurrentSettings.DirectWindEnabled();
            useNearby = CurrentSettings.NearbyDriftEnabled();
            useBallistic = CurrentSettings.BallisticWindEnabled();
            if (!TryReadField(out bool anyFlow))
            {
                useWind = false;
                useNearby = false;
                useBallistic = false;
                return;
            }
            if (!useWind && !anyFlow && !useBallistic)
            {
                Debug.Log(LogPrefix + "no ambient flow requested; no water drag transpilers installed");
                return;
            }
            if ((useWind || anyFlow) && !TryInstall(harmony))
                return;
            useRelativeRudder = CurrentSettings.RelativeRudderEnabled(anyFlow)
                && CurrentRudderAdapter.TryInstall(harmony, typeof(PlayerShip).Assembly.GetType("UBOAT.Game.Scene.Items.Rudder"));
            if (useNearby && anyFlow)
                CurrentShipSpeedAdapter.TryInstall(harmony);

            GameObject carrier = new GameObject(BinderName);
            // OnLoaded fires before the campaign replaces the boot scene.
            Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<CurrentRuntimeBinder>();
        }

        internal static string Describe()
        {
            return "field=" + Horizontal(water) + " m/s kind=" + fieldKind
                + (fieldSummary == null ? "" : (fieldKind == "profile" ? " profile=" : " source=") + fieldSummary)
                + (lastPositionValid ? " position=" + lastLatitude.ToString("F4", CultureInfo.InvariantCulture)
                    + "," + lastLongitude.ToString("F4", CultureInfo.InvariantCulture) : " position=unavailable")
                + " wind=" + Horizontal(wind) + " m/s exposure="
                + exposure.ToString("F3", CultureInfo.InvariantCulture) + " docked=" + docked + " grounded=" + grounded
                + " relativeRudder=" + useRelativeRudder + " nearbyFlag=" + useNearby
                + " boundPlayer=" + (target != null) + " full=" + appliedFull + " simple=" + appliedSimple
                + " waterSteps=" + appliedWater + " airSteps=" + appliedAir
                + " trackedNearby=" + Nearby.Count;
        }

        internal static IList<string> DescribeNearby()
        {
            var lines = new List<string>(Nearby.Count);
            foreach (var entry in Nearby.Values)
                lines.Add(entry.Describe());
            return lines;
        }

        internal static bool WaterConfigured => currentField?.HasFlow == true;
        internal static PlayerShip BoundShip => boundShip;

        internal static bool HasField => currentField != null;
        internal static bool NearbyDriftEnabled => useNearby && boundShip && target != null;

        // The handoff is only for a hull owned by the enabled near-side
        // adapter. Use the same cap and binding path as the regular scan.
        internal static bool BindHandoffHull(Ship ship, Rigidbody body)
        {
            if (!NearbyDriftEnabled)
                return false;
            var hull = ship.GetComponentInChildren<NeuralNetworkPhysics>();
            if (!hull)
                return false;
            if (Nearby.TryGetValue(hull, out NearbyDriftHull existing) && !existing.Current())
                RemoveNearbyEntry(hull, existing);
            if (!Nearby.ContainsKey(hull) && Nearby.Count < NearbyLimit)
                BindNearby(ship);
            return Nearby.TryGetValue(hull, out NearbyDriftHull entry) && entry.Body == body && entry.Current();
        }

        // Distant sandbox groups have no scene transform to convert, so the
        // query is built straight from the group's encounter position (km).
        internal static bool TrySampleEncounter(Vector2 positionKm, DateTime utc, out Vector3 velocity)
        {
            return TrySampleEncounterAtDepth(positionKm, 0f, utc, out velocity);
        }

        internal static bool TrySampleEncounterAtDepth(Vector2 positionKm, float depthMeters, DateTime utc, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (currentField == null)
                return false;
            Vector2 latLong = Coordinates.ToLatLong(positionKm);
            if (float.IsNaN(latLong.x) || float.IsInfinity(latLong.x)
                || float.IsNaN(latLong.y) || float.IsInfinity(latLong.y))
                return false;
            var query = new CurrentQuery
            {
                DepthMeters = depthMeters > 0f ? depthMeters : 0f,
                Utc = utc,
                HasPosition = true,
                Latitude = latLong.x,
                Longitude = latLong.y
            };
            return currentField.TrySample(query, out velocity);
        }

        internal static bool TryGetRouteWater(PlayerShip ship, out Vector3 flow)
        {
            flow = water;
            return boundShip == ship && target != null && !ship.Docked && !grounded && HasFlow(water);
        }

        // Reads the requested source once. Precedence: atlas tile, then depth
        // profile, then constant field. All are explicit experimental inputs,
        // not a silent year/depth extrapolation.
        private static void WrapAtlasWithTidalLayer(CurrentAtlasField atlas, out bool anyFlow)
        {
            if (CurrentSettings.TidalCurrentsEnabled())
            {
                var tidal = CurrentTidalField.CreateWithAtlas(atlas);
                currentField = new CompositeCurrentField(atlas, tidal, IsLandAt);
                anyFlow = atlas.HasFlow || tidal.HasFlow;
                fieldKind = "atlas+tidal";
                return;
            }
            currentField = atlas;
            anyFlow = atlas.HasFlow;
            fieldKind = "atlas";
        }

        internal static bool IsCoastReady => LighthouseCoastProbe.Ready;

        private static bool IsLandAt(double latitude, double longitude)
        {
            longitude = Math.IEEERemainder(longitude, 360.0);
            if (LighthouseCoastProbe.TryGetDistanceKm(latitude, longitude, out double coastKm)
                && coastKm <= 0.0)
                return true;
            WorldMaps.Map elevMap = WorldMaps.ElevationMap;
            if (elevMap == null)
                return false;
            Vector2 encounterKm = Coordinates.ToWorldPosition(latitude, longitude);
            float elevationM = elevMap.GetAtWorldSpace(encounterKm.x, encounterKm.y);
            return !float.IsNaN(elevationM) && elevationM > 0.5f;
        }

        private static bool TryReadField(out bool anyFlow)
        {
            anyFlow = false;
            currentField = null;
            fieldKind = "none";
            fieldSummary = null;
            string atlasPath = ResolveAtlasPath(CurrentSettings.ResolveRequestedAtlas());
            if (!string.IsNullOrEmpty(atlasPath))
            {
                if (!CurrentAtlasField.TryLoad(atlasPath, out CurrentAtlasField atlas, out string error))
                {
                    Debug.LogWarning(LogPrefix + "invalid " + CurrentSettings.AtlasVariable + " \"" + atlasPath
                        + "\": " + error + "; adapter disabled");
                    return false;
                }
                WrapAtlasWithTidalLayer(atlas, out anyFlow);
                fieldSummary = atlas.ToString();
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CurrentSettings.ProfileVariable))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CurrentSettings.FieldVariable)))
                    Debug.LogWarning(LogPrefix + CurrentSettings.AtlasVariable + " overrides profile/constant sources");
                Debug.Log(LogPrefix + "requested atlas tile " + fieldSummary
                    + "; tidal=" + CurrentSettings.TidalCurrentsEnabled()
                    + "; sample uses campaign UTC and hull lat/lon");
                return true;
            }
            string profile = Environment.GetEnvironmentVariable(CurrentSettings.ProfileVariable);
            if (!string.IsNullOrEmpty(profile))
            {
                if (!CurrentProfileField.TryParse(profile, out CurrentProfileField parsed, out string error))
                {
                    Debug.LogWarning(LogPrefix + "invalid " + CurrentSettings.ProfileVariable + " \"" + profile
                        + "\": " + error + "; adapter disabled");
                    return false;
                }
                currentField = parsed;
                anyFlow = parsed.HasFlow;
                fieldKind = "profile";
                fieldSummary = parsed.ToString();
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CurrentSettings.FieldVariable)))
                    Debug.LogWarning(LogPrefix + CurrentSettings.ProfileVariable + " overrides " + CurrentSettings.FieldVariable);
                Debug.Log(LogPrefix + "requested depth profile " + fieldSummary
                    + " (depth positive down, m/s); flow=" + anyFlow);
                return true;
            }
            string raw = Environment.GetEnvironmentVariable(CurrentSettings.FieldVariable);
            if (string.IsNullOrEmpty(raw))
            {
                if (!useWind)
                    Debug.Log(LogPrefix + "disabled (" + CurrentSettings.FieldVariable + " unset); stock physics untouched");
                return true;
            }
            string[] parts = raw.Split(',');
            float east;
            float north;
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out east)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out north)
                || !IsFinite(east) || !IsFinite(north))
            {
                Debug.LogWarning(LogPrefix + "invalid " + CurrentSettings.FieldVariable + " \"" + raw
                    + "\"; expected \"east,north\" in m/s; adapter disabled");
                return false;
            }
            var constant = new ConstantCurrentField(east, north);
            currentField = constant;
            anyFlow = constant.HasFlow;
            fieldKind = "constant";
            fieldSummary = null;
            Debug.Log(LogPrefix + "requested field east=" + east.ToString("R", CultureInfo.InvariantCulture)
                + " north=" + north.ToString("R", CultureInfo.InvariantCulture) + " m/s");
            return true;
        }

        // "bundled" is the shipped regional slice; a player or a run script
        // does not have to spell the long mod path. Anything else is taken
        // literally, so an explicit folder still wins.
        private static string ResolveAtlasPath(string requested)
        {
            if (requested != CurrentSettings.BundledAtlasValue)
                return requested;
            return System.IO.Path.Combine(Application.persistentDataPath,
                "Mods", "RadioNavigation", "Source", "Data", "CurrentAtlas");
        }

        private static bool TryInstall(Harmony harmony)
        {
            var simple = HullType.GetMethod("SimpleUpdate", InstanceFields);
            var full = HullType.GetMethod("Execute", AnyStatic);
            if (Labels == null || simple == null || full == null)
            {
                Debug.LogError(LogPrefix + "water drag hook unavailable; stock physics retained");
                return false;
            }

            // The IL anchors below were derived from the verified installed
            // water DLL (SHA-256 in plan §11.1). A game update changes them;
            // the transpiler then refuses and the mod stays on stock drag.
            try
            {
                harmony.Patch(simple,
                    transpiler: new HarmonyMethod(Method(nameof(PatchSurfaceSimple))));
                harmony.Patch(full,
                    transpiler: new HarmonyMethod(Method(nameof(PatchSurfaceFull))));
                Debug.Log(LogPrefix + "water drag transpilers installed; direct scene wind=" + useWind);
                return true;
            }
            catch (Exception ex)
            {
                harmony.Unpatch(simple, HarmonyPatchType.Transpiler, harmony.Id);
                harmony.Unpatch(full, HarmonyPatchType.Transpiler, harmony.Id);
                Debug.LogError(LogPrefix + "water drag hooks rejected: " + ex.Message);
                return false;
            }
        }

        // The four members below are called from the transpiled water DLL:
        // they must stay public in a public type, or the runtime may reject
        // the cross-assembly call.

        public static bool SurfaceFull(NeuralNetworkPhysics hull, Rigidbody body, Vector3 velocity,
            float wet, float dry, float stepPerMass, Quaternion inverseHull, float blend,
            out Vector3 waterDelta, out Vector3 airDelta)
        {
            waterDelta = airDelta = default(Vector3);
            if (!TryGetStep(hull, body, out CurrentSurfaceDragStep drag, out float exposed, out Vector3 hullWater,
                    out NearbyDriftHull entry))
                return false;
            appliedFull++;
            SurfaceDifference(drag, exposed, hullWater, velocity, wet, dry, stepPerMass, inverseHull, blend,
                out waterDelta, out airDelta);
            entry?.Record(waterDelta, airDelta);
            return true;
        }

        public static bool SurfaceSimple(NeuralNetworkPhysics hull, Rigidbody body, Vector3 velocity,
            float dragPart, float wetFraction, float airDensity, float stepPerMass, Quaternion rotation,
            float blend, out Vector3 waterDelta, out Vector3 airDelta)
        {
            waterDelta = airDelta = default(Vector3);
            if (!TryGetStep(hull, body, out CurrentSurfaceDragStep drag, out float exposed, out Vector3 hullWater,
                    out NearbyDriftHull entry))
                return false;
            appliedSimple++;
            SurfaceDifference(drag, exposed, hullWater, velocity, dragPart * wetFraction,
                dragPart * (1f - wetFraction) * airDensity / 998.6f,
                stepPerMass, Quaternion.Inverse(rotation), blend, out waterDelta, out airDelta);
            entry?.Record(waterDelta, airDelta);
            return true;
        }

        public static void AddFullDifference(ref Vector3 velocity, ref Vector3 stockWater,
            ref Vector3 stockAir, Vector3 waterDelta, Vector3 airDelta, float stepPerMass)
        {
            velocity += (waterDelta + airDelta) * stepPerMass;
            stockWater += waterDelta;
            stockAir += airDelta;
        }

        private static void SurfaceDifference(CurrentSurfaceDragStep drag, float exposed, Vector3 hullWater,
            Vector3 velocity, float wet, float dry, float stepPerMass, Quaternion inverseHull, float blend,
            out Vector3 waterDelta, out Vector3 airDelta)
        {
            drag.Difference(velocity, hullWater, wind, wet, dry, exposed, stepPerMass, inverseHull, blend,
                out waterDelta, out airDelta);
            if (HasFlow(waterDelta))
                appliedWater++;
            if (HasFlow(airDelta) && exposed > 0f && HasFlow(wind))
                appliedAir++;
            lastAir = airDelta;
        }

        private static bool TryGetStep(NeuralNetworkPhysics hull, Rigidbody body,
            out CurrentSurfaceDragStep drag, out float exposed, out Vector3 hullWater, out NearbyDriftHull entry)
        {
            drag = null;
            exposed = 0f;
            hullWater = Vector3.zero;
            entry = null;
            if (ReferenceEquals(hull, target))
            {
                if (docked || grounded)
                    return false;
                drag = step;
                exposed = exposure;
                hullWater = water;
            }
            else if (useNearby && Nearby.TryGetValue(hull, out entry))
            {
                if (!entry.Active || !ReferenceEquals(body, entry.Body))
                    return false;
                drag = entry.Drag;
                exposed = entry.Exposure;
                hullWater = entry.Water;
            }
            return drag != null && !body.isKinematic && body.constraints != RigidbodyConstraints.FreezeAll
                && (HasFlow(hullWater) || (exposed > 0f && HasFlow(wind)));
        }

        internal static void BinderUpdate()
        {
            CurrentRouteRuntime.FlushNotices();
            if ((Time.frameCount & 31) == 0)
            {
                TryBind();
                if (CurrentRouteRuntime.Installed)
                    CurrentFairway.RememberDepartureBerth(boundShip);
            }
            if (Time.realtimeSinceStartup >= nextFieldSample)
                RefreshSampledWater();
            if (useNearby && Time.realtimeSinceStartup >= nextNearbyScan)
                ScanNearby();
            if ((useWind || useBallistic) && Time.realtimeSinceStartup >= nextWeatherSample)
                RefreshWind();
            if (++heartbeat < 1800)
                return;
            heartbeat = 0;
            Debug.Log(LogPrefix + "active: full=" + appliedFull + " simple=" + appliedSimple
                + " kind=" + fieldKind
                + " field=" + water.x.ToString("R", CultureInfo.InvariantCulture) + ","
                + water.z.ToString("R", CultureInfo.InvariantCulture)
                + " waterSteps=" + appliedWater + " airSteps=" + appliedAir
                + " air=" + Horizontal(wind) + " exposure=" + exposure.ToString("F3", CultureInfo.InvariantCulture)
                + " nearby=" + Nearby.Count + " lastAirDelta=" + Horizontal(lastAir));
            if (useNearby)
                foreach (var entry in Nearby.Values)
                    entry.Report();
        }

        private static int heartbeat;

        // Sampling stays out of the drag call. Physics steps use the last
        // sample; a 10 Hz refresh resolves a profile while the boat changes
        // depth, without per-voxel atlas work inside NeuralNetworkPhysics.
        private static void RefreshSampledWater()
        {
            nextFieldSample = Time.realtimeSinceStartup + 0.1f;
            var ship = boundShip;
            Vector3 position = ship ? ship.transform.position : Vector3.zero;
            SampleWater(position, ship ? Mathf.Max(0f, ship.Depth) : 0f, out Vector3 sampled, true);
            water = sampled;
            CurrentRudderAdapter.SetWater(water);
        }

        private static void SampleWater(Vector3 scenePosition, float depthMeters, out Vector3 velocity,
            bool recordPosition = false)
        {
            if (currentField == null)
            {
                // No field: the stand sets a fixed water vector directly.
                velocity = water;
                return;
            }
            var query = new CurrentQuery { DepthMeters = depthMeters > 0f ? depthMeters : 0f, Utc = GameUtc() };
            query.HasPosition = TryLatLong(scenePosition, out query.Latitude, out query.Longitude);
            if (recordPosition)
            {
                lastPositionValid = query.HasPosition;
                lastLatitude = query.Latitude;
                lastLongitude = query.Longitude;
            }
            if (!currentField.TrySample(query, out velocity))
                velocity = Vector3.zero;
        }

        internal static Vector3 CurrentWindMps => wind;

        internal static bool TrySampleWaterAtDepth(Vector3 scenePosition, float depthMeters, out Vector3 velocity)
        {
            if (currentField == null)
            {
                velocity = water;
                return HasFlow(velocity);
            }
            SampleWater(scenePosition, depthMeters, out velocity, false);
            return HasFlow(velocity);
        }

        internal static DateTime GameUtc()
        {
            if (!gameTime)
                gameTime = InjectionFramework.Instance?.GetInstance<GameTime>();
            return gameTime
                ? DateTime.SpecifyKind(gameTime.GmtDateTime, DateTimeKind.Utc)
                : default(DateTime);
        }

        private static bool TryLatLong(Vector3 scenePosition, out double latitude, out double longitude)
        {
            latitude = 0;
            longitude = 0;
            var sandbox = Sandbox.Instance;
            if (sandbox == null)
                return false;
            Vector2 latLong = Coordinates.ToLatLong(sandbox.SceneToEncounterSpace(scenePosition));
            if (float.IsNaN(latLong.x) || float.IsInfinity(latLong.x)
                || float.IsNaN(latLong.y) || float.IsInfinity(latLong.y))
                return false;
            latitude = latLong.x;
            longitude = latLong.y;
            return true;
        }

        internal static void ReadHullState()
        {
            var ship = boundShip;
            docked = true;
            bool sleeping = false;
            if (ship)
            {
                docked = ship.Docked;
                sleeping = (bool)ship.RigidBody && ship.RigidBody.IsSleeping();
            }
            seabedContactTicks = CurrentBallisticsMath.AdvanceSeabedContactTicks(seabedContactTicks, sleeping, false);
            grounded = (bool)ship && seabedContactTicks > 0;
            if (useRelativeRudder)
                CurrentRudderAdapter.SetHeld(docked || grounded);
            // Depth is measured to the top of the hull/conning tower, not to
            // the deck. Fade over the last exposed metre; omit mast drag.
            exposure = ship == null ? 0f : Mathf.Clamp01(-ship.Depth);
            if (useNearby)
            {
                foreach (var entry in Nearby.Values)
                {
                    entry.Refresh();
                    if (useRelativeRudder)
                        CurrentRudderAdapter.SetNearbyWater(entry.Body, entry.Water);
                    CurrentShipSpeedAdapter.SetWater(entry.Ship, entry.Water);
                }
            }
        }

        private static void BindCollisionHooks(PlayerShip ship)
        {
            if (ReferenceEquals(collisionHookShip, ship))
                return;
            if (collisionHookShip != null)
            {
                collisionHookShip.CollisionEnter -= OnPlayerCollision;
                collisionHookShip.CollisionStay -= OnPlayerCollision;
            }
            collisionHookShip = ship;
            seabedContactTicks = 0;
            grounded = false;
            if (ship != null)
            {
                ship.CollisionEnter += OnPlayerCollision;
                ship.CollisionStay += OnPlayerCollision;
            }
        }

        private static void OnPlayerCollision(Collision collision)
        {
            if (collision?.collider != null
                && string.Equals(collision.collider.GetType().Name, "TerrainCollider", StringComparison.Ordinal))
            {
                seabedContactTicks = CurrentBallisticsMath.SeabedContactGraceTicks;
                grounded = boundShip != null;
            }
        }

        private static void RefreshWind()
        {
            nextWeatherSample = Time.realtimeSinceStartup + 0.25f;
            wind = Vector3.zero;
            if (boundShip == null)
                return;
            var current = InjectionFramework.Instance?.GetInstance<Weather>();
            if (current != weather)
            {
                weather = current;
                windDirection = null;
            }
            if (current == null || current.Sea == null)
                return;
            if (windDirection == null)
                windDirection = WindDirectionField?.GetValue(current) as Transform;
            if (windDirection == null)
                return;
            wind = CurrentSurfaceDragStep.SceneWind(windDirection.forward, current.TotalWindSpeed);
        }

        private static readonly FieldInfo WindDirectionField =
            typeof(Weather).GetField("windDirectionTransform", InstanceFields);

        /// <summary>
        /// Scene wind on demand, independent of the drift environment flags:
        /// the map indicator must work even when no current or wind patch is
        /// installed. The vector is where the air moves TO.
        /// </summary>
        internal static bool TrySampleWind(out Vector3 metersPerSecond, out float knots)
        {
            metersPerSecond = Vector3.zero;
            knots = 0f;
            var current = InjectionFramework.Instance?.GetInstance<Weather>();
            if (current == null || current.Sea == null)
                return false;
            if (WindDirectionField == null
                || !(WindDirectionField.GetValue(current) is Transform direction))
                return false;
            knots = current.TotalWindSpeed;
            metersPerSecond = CurrentSurfaceDragStep.SceneWind(direction.forward, knots);
            return knots > 0f;
        }

        private static string Horizontal(Vector3 value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture) + ","
                + value.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool HasFlow(Vector3 value)
        {
            return value.x != 0f || value.z != 0f;
        }

        private static void TryBind()
        {
            PlayerShip ship = ResolvePlayerShip();
            if (ship != null && ReferenceEquals(ship, boundShip) && target != null)
                return;
            boundShip = null;
            target = null;
            BindCollisionHooks(null);
            if (useRelativeRudder)
                CurrentRudderAdapter.Bind(null, null, Vector3.zero);
            if (ship == null)
                return;
            Rigidbody body = ship.RigidBody;
            NeuralNetworkPhysics hull = body == null
                ? null : body.GetComponentInChildren<NeuralNetworkPhysics>();
            if (hull == null)
                return;
            if (!TryBuildStep(hull, out step))
                return;
            boundShip = ship;
            target = hull;
            BindCollisionHooks(ship);
            RefreshSampledWater();
            if (useRelativeRudder)
                CurrentRudderAdapter.Bind(body, ship.Rudders, water);
            ReadHullState();
            Debug.Log(LogPrefix + "bound player hull " + hull.name + " ("
                + ship.GetType().Name + ")");
        }

        private static bool TryBuildStep(NeuralNetworkPhysics hull, out CurrentSurfaceDragStep drag)
        {
            drag = null;
            // Game assemblies are obfuscated: field lookups can silently
            // return null after an update, so cache and guard each one.
            if (linearField == null)
                linearField = HullType.GetField("linearDragCoefficient", InstanceFields);
            if (quadraticField == null)
                quadraticField = HullType.GetField("quadraticDragCoefficient", InstanceFields);
            if (uniformField == null)
                uniformField = HullType.GetField("uniformDragCoefficient", InstanceFields);
            if (quadraticArrayField == null)
                quadraticArrayField = HullType.GetField("quadraticDragCoefficients", InstanceFields);
            if (linearField == null || quadraticField == null
                || uniformField == null || quadraticArrayField == null)
            {
                if (WarnOnce.ShouldLog("CurrentRuntimeAdapter.coefficients"))
                    Debug.LogWarning(LogPrefix
                        + "hull drag fields not found (game update?); current transport inert");
                return false;
            }
            drag = new CurrentSurfaceDragStep(
                (float)linearField.GetValue(hull),
                (float)quadraticField.GetValue(hull),
                (bool)uniformField.GetValue(hull)
                    ? null : (float[])quadraticArrayField.GetValue(hull));
            return true;
        }

        private static void RemoveNearbyEntry(NeuralNetworkPhysics hull, NearbyDriftHull entry)
        {
            if (useRelativeRudder)
                CurrentRudderAdapter.UnbindNearby(entry.Body);
            if (useNearby)
                CurrentShipSpeedAdapter.Unbind(entry.Ship);
            Nearby.Remove(hull);
        }

        private static void ScanNearby()
        {
            nextNearbyScan = Time.realtimeSinceStartup + 2f;
            Expired.Clear();
            foreach (var pair in Nearby)
            {
                if (!pair.Value.Current())
                {
                    Expired.Add(pair.Key);
                    continue;
                }
                pair.Value.Refresh();
                if (useRelativeRudder)
                    CurrentRudderAdapter.SetNearbyWater(pair.Value.Body, pair.Value.Water);
                if (useNearby)
                    CurrentShipSpeedAdapter.SetWater(pair.Value.Ship, pair.Value.Water);
            }
            foreach (var hull in Expired)
            {
                if (Nearby.TryGetValue(hull, out NearbyDriftHull stale))
                    RemoveNearbyEntry(hull, stale);
            }

            if (boundShip == null)
                return;
            foreach (var entity in Entity.List)
            {
                if (Nearby.Count >= NearbyLimit)
                    break;
                BindNearby(entity);
            }
        }

        private static void BindNearby(Entity entity)
        {
            if (!IsEligibleNearbyShip(entity, out Ship ship, out Rigidbody body, out NeuralNetworkPhysics hull))
                return;
            if (!TryBuildStep(hull, out CurrentSurfaceDragStep drag))
                return;
            string kind = entity.Blueprint?.Type?.Name ?? entity.GetType().Name;
            var entry = new NearbyDriftHull(kind, entity, ship, body, drag);
            entry.Refresh();
            Nearby.Add(hull, entry);
            if (useRelativeRudder)
                CurrentRudderAdapter.BindNearby(body, ship.Rudders, entry.Water);
            if (useNearby)
                CurrentShipSpeedAdapter.Bind(ship, entry.Water);
            Debug.Log(LogPrefix + "bound nearby " + kind + " (" + entity.name + ")");
        }

        private static bool IsEligibleNearbyShip(Entity entity, out Ship ship, out Rigidbody body,
            out NeuralNetworkPhysics hull)
        {
            ship = entity as Ship;
            body = null;
            hull = null;
            if (ship == null || ship is PlayerShip || ship is Aircraft || ship.DockPoint != null)
                return false;
            var sandboxEntity = entity.SandboxEntity;
            if (sandboxEntity == null)
                return false;
            var group = sandboxEntity.Group;
            if (group == null)
                return false;
            if (group.IsAirborne)
                return false;
            var blueprintType = entity.Blueprint != null ? entity.Blueprint.Type : null;
            if (blueprintType != null)
            {
                if (blueprintType.Category == EntityTypeCategory.Pontoon
                    || (blueprintType.Category & EntityTypeCategory.AircraftMask) != 0)
                    return false;
            }
            hull = entity.GetComponentInChildren<NeuralNetworkPhysics>();
            if (hull == null || Nearby.ContainsKey(hull))
                return false;
            body = hull.GetComponentInParent<Rigidbody>();
            if (body == null)
                return false;
            return !body.isKinematic;
        }

        private sealed class NearbyDriftHull
        {
            private readonly Entity entity;
            private readonly object identity;
            private readonly string kind;
            private int waterSteps;
            private int airSteps;
            internal readonly Rigidbody Body;
            internal readonly CurrentSurfaceDragStep Drag;
            internal readonly Ship Ship;
            internal bool Active { get; private set; }
            internal float Exposure { get; private set; }
            internal Vector3 Water { get; private set; }

            internal NearbyDriftHull(string kind, Entity entity, Ship ship, Rigidbody body, CurrentSurfaceDragStep drag)
            {
                this.kind = kind;
                this.entity = entity;
                identity = entity.SandboxEntity;
                Ship = ship;
                Body = body;
                Drag = drag;
            }

            internal bool Current()
            {
                return entity && entity.isActiveAndEnabled && Body && !Body.isKinematic
                    && Ship.DockPoint == null && ReferenceEquals(identity, entity.SandboxEntity);
            }

            internal void Refresh()
            {
                // Native DockPoint clamps NPC bodies with FreezeAll; the
                // shared callback rejects that constraint on the same tick.
                Active = Current();
                Exposure = Active ? Mathf.Clamp01(-entity.Depth) : 0f;
                Vector3 sampled = Vector3.zero;
                if (Active)
                    SampleWater(entity.transform.position, Mathf.Max(0f, entity.Depth), out sampled);
                Water = sampled;
            }

            internal void Record(Vector3 waterDelta, Vector3 airDelta)
            {
                if (HasFlow(waterDelta))
                    waterSteps++;
                if (HasFlow(airDelta) && Exposure > 0f && HasFlow(wind))
                    airSteps++;
            }

            internal void Report()
            {
                if (Active)
                    Debug.Log(LogPrefix + Describe());
            }

            internal string Describe()
            {
                return "nearby " + kind + " water=" + waterSteps + " air=" + airSteps + " velocity="
                    + Horizontal(Body == null ? Vector3.zero : Body.velocity)
                    + " current=" + Horizontal(Water) + " exposure="
                    + Exposure.ToString("F3", CultureInfo.InvariantCulture) + " active=" + Active;
            }
        }

        private static FieldInfo linearField;
        private static FieldInfo quadraticField;
        private static FieldInfo uniformField;
        private static FieldInfo quadraticArrayField;

        private static PlayerShip ResolvePlayerShip()
        {
            if (playerShipField.IsNull())
                playerShipField = typeof(Entity).GetField("playerShip",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (playerShipField.IsNull())
                return null;
            object ship = playerShipField.GetValue(null);
            // Normalize Unity fake-null so the caller can use a plain null check.
            return ship.IsNull() ? null : ship as PlayerShip;
        }

        private static IEnumerable<CodeInstruction> PatchSurfaceFull(IEnumerable<CodeInstruction> original,
            ILGenerator generator)
        {
            var code = original.ToList();
            Verify(code, 2288, 1693, OpCodes.Ldloca_S);
            Verify(code, 2288, 1724, OpCodes.Ldloc_S);
            Verify(code, 2288, 1775, OpCodes.Ldloc_S);
            var waterDelta = generator.DeclareLocal(typeof(Vector3));
            var airDelta = generator.DeclareLocal(typeof(Vector3));
            var active = generator.DeclareLocal(typeof(bool));
            var accumulate = code[1775];
            var skipStockCorrection = code[1724];
            var finishEarly = LabelAt(skipStockCorrection, generator);
            var afterEarly = LabelAt(accumulate, generator);

            // Native short-circuit suppresses its small forces. Nonzero
            // ambient flow must still act, even on a motionless hull.
            Insert(code, 1775, new CodeInstruction(OpCodes.Ldloc, active),
                new CodeInstruction(OpCodes.Brfalse, afterEarly),
                Address(56), Address(98), Address(99), new CodeInstruction(OpCodes.Ldloc, waterDelta),
                new CodeInstruction(OpCodes.Ldloc, airDelta), Load(86), Call(nameof(AddFullDifference)));
            MoveLabelToOriginal(code[1775], accumulate, afterEarly);
            Insert(code, 1724, new CodeInstruction(OpCodes.Ldloc, active),
                new CodeInstruction(OpCodes.Brfalse, finishEarly),
                Address(98), new CodeInstruction(OpCodes.Initobj, typeof(Vector3)),
                Address(99), new CodeInstruction(OpCodes.Initobj, typeof(Vector3)),
                new CodeInstruction(OpCodes.Br, LabelAt(code[1775], generator)));
            MoveLabelToOriginal(code[1724], skipStockCorrection, finishEarly);
            Insert(code, 1693, Load(37), Load(45), Load(56), Load(90), Load(91), Load(86),
                Load(62), Load(49), new CodeInstruction(OpCodes.Ldloca, waterDelta),
                new CodeInstruction(OpCodes.Ldloca, airDelta), Call(nameof(SurfaceFull)),
                new CodeInstruction(OpCodes.Stloc, active));
            return code;
        }

        private static IEnumerable<CodeInstruction> PatchSurfaceSimple(IEnumerable<CodeInstruction> original,
            ILGenerator generator)
        {
            var code = original.ToList();
            Verify(code, 468, 154, OpCodes.Ldloca_S);
            Verify(code, 468, 327, OpCodes.Ldarg_0);
            var waterDelta = generator.DeclareLocal(typeof(Vector3));
            var airDelta = generator.DeclareLocal(typeof(Vector3));
            var active = generator.DeclareLocal(typeof(bool));
            var verticalCorrection = code[327];
            var skip = LabelAt(verticalCorrection, generator);
            Insert(code, 327, new CodeInstruction(OpCodes.Ldloc, active),
                new CodeInstruction(OpCodes.Brfalse, skip),
                Address(3), Address(9), Address(10), new CodeInstruction(OpCodes.Ldloc, waterDelta),
                new CodeInstruction(OpCodes.Ldloc, airDelta), Load(6), Call(nameof(AddFullDifference)));
            MoveLabelToOriginal(code[327], verticalCorrection, skip);
            Insert(code, 154, Arg0(), Arg0(), ReadField("rigidBody"), Load(3),
                Arg0(), ReadField("dragPart"), Arg0(), ReadField("submergedAreaFraction"),
                Arg0(), ReadField("airDensity"), Load(6), Load(2), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca, waterDelta), new CodeInstruction(OpCodes.Ldloca, airDelta),
                Call(nameof(SurfaceSimple)), new CodeInstruction(OpCodes.Stloc, active));
            return code;
        }

        private static object LabelAt(CodeInstruction instruction, ILGenerator generator)
        {
            object label = generator.DefineLabel();
            ((System.Collections.IList)Labels.GetValue(instruction)).Add(label);
            return label;
        }

        private static void MoveLabelToOriginal(CodeInstruction inserted, CodeInstruction original, object label)
        {
            ((System.Collections.IList)Labels.GetValue(inserted)).Remove(label);
            ((System.Collections.IList)Labels.GetValue(original)).Add(label);
        }

        private static void Verify(List<CodeInstruction> code, int length, int anchor, OpCode expected)
        {
            if (code.Count != length || code[anchor].opcode != expected)
                throw new InvalidOperationException("Water DLL IL mismatch at instruction " + anchor
                    + " (expected " + expected + "); game update suspected");
        }

        private static void Insert(List<CodeInstruction> code, int index, params CodeInstruction[] extra)
        {
            // The label list type differs between Unity's facade and the
            // game's Harmony; move it without binding its type.
            Labels.SetValue(extra[0], Labels.GetValue(code[index]));
            Labels.SetValue(code[index], Labels.GetValue(new CodeInstruction(OpCodes.Nop)));
            code.InsertRange(index, extra);
        }

        private static MethodInfo Method(string name)
        {
            return typeof(CurrentRuntimeAdapter).GetMethod(name, AnyStatic);
        }

        private static CodeInstruction Call(string name)
        {
            return new CodeInstruction(OpCodes.Call, Method(name));
        }

        private static CodeInstruction Load(short local)
        {
            return new CodeInstruction(OpCodes.Ldloc, local);
        }

        private static CodeInstruction Address(short local)
        {
            return new CodeInstruction(OpCodes.Ldloca, local);
        }

        private static CodeInstruction Arg0()
        {
            return new CodeInstruction(OpCodes.Ldarg_0);
        }

        private static CodeInstruction ReadField(string name)
        {
            return new CodeInstruction(OpCodes.Ldfld, HullType.GetField(name, InstanceFields));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Thin Unity shell; all logic lives in CurrentRuntimeAdapter.
    /// Top-level because nested MonoBehaviours are not a supported Unity
    /// pattern, even when only added at runtime.</summary>
    public sealed class CurrentRuntimeBinder : MonoBehaviour
    {
        private void Update()
        {
            CurrentRuntimeAdapter.BinderUpdate();
            if (!CurrentRouteRuntime.Installed)
                return;
            if (!etaHud)
                etaHud = gameObject.AddComponent<CurrentRouteEtaHud>();
            etaHud.Refresh();
        }

        private CurrentRouteEtaHud etaHud;

        private void FixedUpdate()
        {
            CurrentRuntimeAdapter.ReadHullState();
        }
    }
}
