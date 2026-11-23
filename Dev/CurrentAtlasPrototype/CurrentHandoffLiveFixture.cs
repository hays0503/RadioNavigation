using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Staged only for the isolated P1 run. Never moves the watched group,
    // forces Spawn/Unspawn, sets a velocity, or changes the game clock.
    [DefaultExecutionOrder(32000)]
    public sealed class CurrentHandoffLiveFixture : MonoBehaviour
    {
        private const string HarmonyId = "radionav.dev.current-handoff-receipt";
        private static CurrentHandoffLiveFixture active;
        private static Harmony harmony;
        private SandboxMobileGroup group;
        private SandboxEntity entity;
        private Rigidbody2D farBody;
        private GameTime clock;
        private SandboxPlayerWolfpack playerGroup;
        private int fixedTick;
        private int farDepth;
        private Sample pending;
        private readonly Trace trace = new Trace();

        [Serializable]
        public sealed class Candidate
        {
            public int id;
            public string name, type, prefab, state;
            public float east, north, distanceKm, groupSpeedMps, playerEast, playerNorth, areaRadiusKm, effectiveAreaRadiusKm, simulationDistanceMultiplier;
        }

        [Serializable]
        public sealed class Candidates
        {
            public Candidate[] values;
        }

        [Serializable]
        public sealed class Sample
        {
            public string phase, state, utc, entityState;
            public int frame, fixedTick, routeCount;
            public long storyTicks, groupTicks;
            public double eastKm, northKm;
            public float tc, eastMps, northMps, eastWaterMps, northWaterMps,
                groupEastKm, groupNorthKm, farBodyEastKm, farBodyNorthKm,
                farBodyEastMps, farBodyNorthMps, stockEastMps, stockNorthMps,
                shipHeadingDeg, rudderEulerY, rudderTargetTurn, propellerPower,
                powerModifier, averageVelocityMps, targetVelocityMps;
            public int nnpWaterSteps, nnpAirSteps, totalFullSteps, totalSimpleSteps, totalWaterSteps;
            public float nearbyWaterE, nearbyWaterN;
            public bool covered, hasSceneBody, isKinematic, nearbyFound, nearbyActive;
        }

        [Serializable]
        public sealed class Trace
        {
            public string error;
            public Candidate target;
            public bool complete;
            public long armedStoryTicks;
            public bool enteredNativeArea, leftNativeArea;
            public List<Sample> rows = new List<Sample>();
        }

        private static IEnumerable<SandboxMobileGroup> Eligible() =>
            FindObjectsOfType<SandboxMobileGroup>().Where(g => g && !(g is SandboxPlayerWolfpack)
                && !g.IsAirborne && !g.IsBeingDestroyed && g.Entities.Count == 1 && g.Route.Count > 0
                && g.Entities[0].Enabled && g.Entities[0].Type.Name == "Liberty");

        private static bool Far(SandboxMobileGroup g) => g.SpawnState <= SpawnState.SpawnedImpostors;

        private static Candidate Describe(SandboxMobileGroup g, Vector2 player)
        {
            var ship = g.Entities[0];
            var settings = InjectionFramework.Instance?.GetInstance<UBOAT.Game.Core.Data.UserSettings>();
            float mult = settings != null ? settings.GameplaySettings.SimulationDistanceMultiplier : 1f;
            return new Candidate { id = g.GetInstanceID(), name = ship.Name, type = ship.Type.Name,
                prefab = ship.Type.PrefabPath, state = g.SpawnState.ToString(),
                east = ship.Position.x, north = ship.Position.y, distanceKm = (ship.Position - player).magnitude,
                groupSpeedMps = g.Velocity * 1000f, playerEast = player.x, playerNorth = player.y,
                areaRadiusKm = g.AreaRadius,
                simulationDistanceMultiplier = mult,
                effectiveAreaRadiusKm = g.AreaRadius * mult };
        }

        public static string Discover()
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!player || player.Docked)
                return "no player at sea";
            Vector2 point = player.SandboxEntity.Position;
            return Encode(new Candidates
            {
                values = Eligible().Where(Far).Select(g => Describe(g, point))
                    .OrderBy(candidate => candidate.distanceKm).Take(24).ToArray()
            });
        }

        // Controlled diagnostic only: move the player radially to provoke the
        // game's encounter detector. It is not a continuous sailing trial.
        public static string PlacePlayer(int id, double east, double north)
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            var wolfpack = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            var target = active && active.group.GetInstanceID() == id
                ? active.group : Eligible().SingleOrDefault(g => g.GetInstanceID() == id && Far(g));
            if (!player || player.Docked || !wolfpack || !target || !Finite((float)east) || !Finite((float)north))
                return "placement unavailable";
            var point = new Vector2((float)east, (float)north);
            if ((point - target.Position).magnitude < 1f)
                return "refused placement within 1 km of the tracked hull";
            wolfpack.SetPosition(point, true);
            return Encode(Describe(target, player.SandboxEntity.Position));
        }

        public static string Arm(int id)
        {
            if (active)
                return "trace already armed";
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            var wolfpack = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            var target = Eligible().SingleOrDefault(g => g.GetInstanceID() == id && Far(g));
            if (!player || player.Docked || !time || !wolfpack || !target || !Sandbox.Instance)
                return "target or campaign not ready";
            var body = target.GetComponent<Rigidbody2D>();
            if (!body)
                return "far body missing";
            var observer = new GameObject("RadioNav.Dev.HandoffObserver")
                .AddComponent<CurrentHandoffLiveFixture>();
            observer.group = target;
            observer.entity = target.Entities[0];
            observer.farBody = body;
            observer.clock = time;
            observer.playerGroup = wolfpack;
            observer.trace.target = Describe(target, player.SandboxEntity.Position);
            observer.trace.armedStoryTicks = time.StoryTicks;
            active = observer;
            try
            {
                Install();
                wolfpack.EnterGroupArea += observer.OnEnter;
                wolfpack.LeaveGroupArea += observer.OnLeave;
                return "armed";
            }
            catch (Exception ex)
            {
                Stop();
                return "hooks unavailable: " + ex.Message;
            }
        }

        public static string Read() => active ? Encode(active.trace) : "not armed";

        public static string Probe(int id)
        {
            var g = FindObjectsOfType<SandboxMobileGroup>().FirstOrDefault(x => x && x.GetInstanceID() == id);
            return EncodeProbe(g);
        }

        public static string ProbeNear(double eastKm, double northKm)
        {
            var target = new Vector2((float)eastKm, (float)northKm);
            SandboxMobileGroup best = null;
            float bestDist = 5f;
            foreach (var g in Eligible())
            {
                float d = (g.Position - target).magnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }
            return EncodeProbe(best);
        }

        private static string EncodeProbe(SandboxMobileGroup g)
        {
            var clock = InjectionFramework.Instance?.GetInstance<GameTime>();
            var sandbox = Sandbox.Instance;
            if (!g || g.Entities.Count == 0 || !clock || sandbox == null)
                return "missing";
            var ent = g.Entities[0];
            var body = g.GetComponent<Rigidbody2D>();
            var ship = ent.SpawnedEntity as Ship;
            var sceneBody = ship ? ship.RigidBody : null;
            bool inScene = g.SpawnState == SpawnState.Spawned && sceneBody != null;
            double eastKm = g.Position.x, northKm = g.Position.y;
            if (inScene)
                sandbox.SceneToEncounterSpace(sceneBody.position, out eastKm, out northKm);
            return Encode(new
            {
                id = g.GetInstanceID(),
                state = g.SpawnState.ToString(),
                storyTicks = clock.StoryTicks,
                groupTicks = g.LastUpdateTicks,
                eastKm,
                northKm,
                eastMps = inScene ? sceneBody.velocity.x : (body ? body.velocity.x * 1000f : 0f),
                northMps = inScene ? sceneBody.velocity.z : (body ? body.velocity.y * 1000f : 0f),
                farBodyEastMps = body ? body.velocity.x * 1000f : 0f,
                farBodyNorthMps = body ? body.velocity.y * 1000f : 0f,
                hasSceneBody = sceneBody != null,
                describe = CurrentStrategicDrift.Describe(),
            });
        }

        public static bool Stop()
        {
            if (harmony != null)
                harmony.UnpatchAll(HarmonyId);
            harmony = null;
            var previous = active;
            active = null;
            if (previous)
            {
                if (previous.playerGroup)
                {
                    previous.playerGroup.EnterGroupArea -= previous.OnEnter;
                    previous.playerGroup.LeaveGroupArea -= previous.OnLeave;
                }
                Destroy(previous.gameObject);
            }
            return true;
        }

        private void OnEnter(SandboxGroup candidate)
        {
            if (candidate == group && Has("far.before") && !Has("scene.before"))
                trace.enteredNativeArea = true;
        }

        private void OnLeave(SandboxGroup candidate)
        {
            if (candidate == group && Has("scene.before"))
                trace.leftNativeArea = true;
        }

        private static void Install()
        {
            harmony = new Harmony(HarmonyId);
            var groupType = typeof(SandboxGroup);
            Patch(groupType, nameof(SandboxGroup.Spawn), Type.EmptyTypes, nameof(BeforeSpawn), null);
            Patch(groupType, nameof(SandboxGroup.OnSpawned), new[] { typeof(SandboxEntity) },
                null, nameof(AfterSpawn));
            Patch(groupType, nameof(SandboxGroup.Unspawn), Type.EmptyTypes,
                nameof(BeforeUnspawn), nameof(AfterUnspawn));
            Patch(typeof(SandboxMobileGroup), nameof(SandboxMobileGroup.UpdateGroup), Type.EmptyTypes,
                nameof(BeforeFarUpdate), nameof(AfterFarUpdate));
        }

        private static void Patch(Type type, string name, Type[] args, string prefix, string postfix)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.DeclaredOnly, null, args, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, Handler(prefix, Priority.First), Handler(postfix, Priority.Last));
        }

        private static HarmonyMethod Handler(string name, int priority)
        {
            if (name == null)
                return null;
            return new HarmonyMethod(typeof(CurrentHandoffLiveFixture).GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };
        }

        private static bool Watching(SandboxGroup candidate) =>
            active && active.group == candidate && active.trace.error == null && !active.trace.complete;

        private static void BeforeSpawn(SandboxGroup __instance)
        {
            if (Watching(__instance) && Far(active.group))
                active.AddOnce("far.before", false);
        }

        private static void AfterSpawn(SandboxGroup __instance)
        {
            if (Watching(__instance) && __instance.SpawnState == SpawnState.Spawned
                && active.Has("far.before"))
                active.AddOnce("scene.native", true);
        }

        private static void BeforeUnspawn(SandboxGroup __instance)
        {
            if (Watching(__instance) && __instance.SpawnState == SpawnState.Spawned
                && active.Has("scene.native"))
                active.AddOnce("scene.before", true);
        }

        private static void AfterUnspawn(SandboxGroup __instance)
        {
            if (Watching(__instance) && active.Has("scene.before")
                && (__instance.SpawnState == SpawnState.Unspawning || Far(active.group)))
                active.AddOnce("far.release", false);
        }

        private static void BeforeFarUpdate(SandboxMobileGroup __instance, out bool __state)
        {
            __state = Watching(__instance) && active.Has("scene.before")
                && active.group.SpawnState == SpawnState.NotSpawned;
            if (!__state || active.farDepth++ != 0)
                return;
            active.AddOnce("far.initial", false);
            active.pending = active.Capture("far.step.before", false);
        }

        private static void AfterFarUpdate(SandboxMobileGroup __instance, bool __state)
        {
            if (!__state || !active || active.group != __instance || --active.farDepth != 0)
                return;
            if (!Watching(__instance) || active.pending == null
                || __instance.LastUpdateTicks <= active.pending.groupTicks)
                return;
            var after = active.Capture("far.step.after", false);
            if (after == null)
                return;
            active.trace.rows.Add(active.pending);
            active.trace.rows.Add(after);
            active.pending = null;
            active.trace.complete = true;
        }

        public void FixedUpdate()
        {
            if (!group || !farBody || !clock)
            {
                trace.error = "target or campaign lost";
                return;
            }
            if (!Watching(group))
                return;
            fixedTick++;
            if (group.SpawnState == SpawnState.Spawned && Has("scene.native") && !Has("scene.before"))
            {
                int count = trace.rows.Count(row => row.phase == "scene.fixed");
                // First callback is before Unity's first solver step; the
                // following three also observe the previous solver output.
                if (count < 4)
                    Add("scene.fixed", true);
            }
            if (group.SpawnState == SpawnState.NotSpawned && Has("scene.before"))
                AddOnce("far.initial", false);
        }

        private bool Has(string phase) => trace.rows.Any(row => row.phase == phase);

        private void AddOnce(string phase, bool scene)
        {
            if (!Has(phase))
                Add(phase, scene);
        }

        private void Add(string phase, bool scene)
        {
            var row = Capture(phase, scene);
            if (row != null)
                trace.rows.Add(row);
        }

        private Sample Capture(string phase, bool scene)
        {
            try
            {
                if (group.Entities.Count != 1 || !ReferenceEquals(group.Entities[0], entity))
                    throw new InvalidOperationException("tracked entity replaced");
                return CaptureNative(phase, scene);
            }
            catch (Exception ex)
            {
                trace.error = phase + ": " + ex.Message;
                return null;
            }
        }

        private Sample CaptureNative(string phase, bool scene)
        {
            Vector2 point = entity.Position;
            Vector2 velocity = farBody.velocity * 1000f;
            var body = (entity.SpawnedEntity as Ship)?.RigidBody;
            if (scene && !body)
                throw new InvalidOperationException("scene body missing");
            var row = new Sample
            {
                phase = phase, state = group.SpawnState.ToString(), entityState = entity.SpawnState.ToString(),
                frame = Time.frameCount, fixedTick = fixedTick, storyTicks = clock.StoryTicks,
                tc = clock.TimeScale.Value,
                groupTicks = group.LastUpdateTicks, routeCount = group.Route.Count,
                utc = CurrentRuntimeAdapter.GameUtc().ToString("O"),
                hasSceneBody = body != null, isKinematic = body != null && body.isKinematic,
                eastKm = point.x, northKm = point.y, eastMps = velocity.x, northMps = velocity.y,
                groupEastKm = group.Position.x, groupNorthKm = group.Position.y,
                farBodyEastKm = farBody.position.x, farBodyNorthKm = farBody.position.y,
                farBodyEastMps = velocity.x, farBodyNorthMps = velocity.y
            };
            var stock = group.Up * (group.Velocity * 1000f);
            row.stockEastMps = stock.x;
            row.stockNorthMps = stock.y;
            if (scene)
            {
                Sandbox.Instance.SceneToEncounterSpace(body.position, out row.eastKm, out row.northKm);
                row.eastMps = body.velocity.x;
                row.northMps = body.velocity.z;
                CaptureSceneDiagnostics(entity.SpawnedEntity as Ship, row);
            }
            row.covered = CurrentRuntimeAdapter.TrySampleEncounter(group.Position,
                CurrentRuntimeAdapter.GameUtc(), out Vector3 water);
            row.eastWaterMps = water.x;
            row.northWaterMps = water.z;
            return row;
        }

        private static readonly FieldInfo ShipEnginesField = typeof(Ship).GetField("activeEngines",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ShipRuddersField = typeof(Ship).GetField("rudders",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ShipTargetVelField = typeof(Ship).GetField("targetVelocity",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NearbyDictField = typeof(CurrentRuntimeAdapter).GetField("Nearby",
            BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly FieldInfo AppliedFullField = typeof(CurrentRuntimeAdapter).GetField("appliedFull",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo AppliedSimpleField = typeof(CurrentRuntimeAdapter).GetField("appliedSimple",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo AppliedWaterField = typeof(CurrentRuntimeAdapter).GetField("appliedWater",
            BindingFlags.Static | BindingFlags.NonPublic);

        private static void CaptureSceneDiagnostics(Ship ship, Sample row)
        {
            if (AppliedFullField?.GetValue(null) is int af) row.totalFullSteps = af;
            if (AppliedSimpleField?.GetValue(null) is int asmp) row.totalSimpleSteps = asmp;
            if (AppliedWaterField?.GetValue(null) is int aw) row.totalWaterSteps = aw;
            if (!ship)
                return;
            row.shipHeadingDeg = ship.transform.eulerAngles.y;
            row.averageVelocityMps = ship.AverageVelocity;
            if (ShipTargetVelField?.GetValue(ship) is float tv)
                row.targetVelocityMps = tv;
            if (ship.Propellers != null && ship.Propellers.Length > 0 && ship.Propellers[0] != null)
                row.propellerPower = ship.Propellers[0].Power * ship.Propellers[0].PowerMultiplier;
            var engines = ShipEnginesField?.GetValue(ship);
            var pmProp = engines?.GetType().GetProperty("PowerModifier", BindingFlags.Instance | BindingFlags.Public);
            if (engines != null && pmProp?.GetValue(engines) is float pm)
                row.powerModifier = pm;
            if (ShipRuddersField?.GetValue(ship) is Array rudders && rudders.Length > 0)
            {
                var r0 = rudders.GetValue(0);
                if (r0 != null)
                {
                    var rt = r0.GetType();
                    if (rt.GetField("eulerY", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(r0) is float ey)
                        row.rudderEulerY = ey;
                    if (rt.GetProperty("TargetTurn", BindingFlags.Instance | BindingFlags.Public)?.GetValue(r0) is float tt)
                        row.rudderTargetTurn = tt;
                }
            }
            if (NearbyDictField?.GetValue(null) is IDictionary nearby)
            {
                foreach (DictionaryEntry pair in nearby)
                {
                    var entry = pair.Value;
                    var et = entry?.GetType();
                    if (et == null || !ReferenceEquals(et.GetField("Body", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry), ship.RigidBody))
                        continue;
                    row.nearbyFound = true;
                    if (et.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry) is bool na)
                        row.nearbyActive = na;
                    if (et.GetProperty("Water", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry) is Vector3 nw)
                    {
                        row.nearbyWaterE = nw.x;
                        row.nearbyWaterN = nw.z;
                    }
                    if (et.GetField("waterSteps", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry) is int ws)
                        row.nnpWaterSteps = ws;
                    if (et.GetField("airSteps", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry) is int airs)
                        row.nnpAirSteps = airs;
                    break;
                }
            }
        }

        // Unity JsonUtility drops fields on runtime-compiled DTOs in the game
        // player. The receipt has only public fields, primitives and sequences.
        private static string Encode(object value)
        {
            var json = new StringBuilder();
            WriteValue(json, value);
            return json.ToString();
        }

        private static void WriteValue(StringBuilder json, object value)
        {
            if (value == null || value is float && !Finite((float)value)
                || value is double && (double.IsNaN((double)value) || double.IsInfinity((double)value)))
                json.Append("null");
            else if (value is string text)
                WriteString(json, text);
            else if (value is bool flag)
                json.Append(flag ? "true" : "false");
            else if (value is IFormattable number)
                json.Append(number.ToString(value is float || value is double ? "R" : null,
                    CultureInfo.InvariantCulture));
            else if (value is IEnumerable sequence)
                WriteSequence(json, sequence);
            else
                WriteObject(json, value);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void WriteSequence(StringBuilder json, IEnumerable sequence)
        {
            json.Append('[');
            bool first = true;
            foreach (var item in sequence)
            {
                if (!first)
                    json.Append(',');
                first = false;
                WriteValue(json, item);
            }
            json.Append(']');
        }

        private static void WriteObject(StringBuilder json, object value)
        {
            json.Append('{');
            bool first = true;
            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!first)
                    json.Append(',');
                first = false;
                WriteString(json, field.Name);
                json.Append(':');
                WriteValue(json, field.GetValue(value));
            }
            foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0)
                    continue;
                if (!first)
                    json.Append(',');
                first = false;
                WriteString(json, prop.Name);
                json.Append(':');
                WriteValue(json, prop.GetValue(value, null));
            }
            json.Append('}');
        }

        private static void WriteString(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\')
                    json.Append('\\').Append(c);
                else if (char.IsControl(c) || char.IsSurrogate(c))
                    json.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else
                    json.Append(c);
            }
            json.Append('"');
        }
    }
}
