using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UBOAT.Game.Core;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Places the mod's "lighthouse" 3D prefab for the enabled lighthouse
    /// fleet (curated profiles + LeuchtfeuerRegistry theater subset) as
    /// decorative sandbox entities, with a distance gate: the map marker
    /// layer always draws every profile, but the expensive 3D model +
    /// rotator + glow only exist while the lighthouse is within
    /// LighthouseDistanceGate.SpawnRadiusKm of the submarine (despawn past
    /// the hysteresis radius, capped at MaxConcurrent3D nearest instances).
    ///
    /// Uses the runtime group/entity pattern proven by the "Expanded Ice"
    /// workshop mod: SandboxGroup.Create + SandboxEntity.Create with an
    /// EntityType/EntityBlueprint built at runtime from a constructed
    /// GameDataRow (prefab path resolved through ResourceManager, so the
    /// prefab from this mod's own AssetBundle is used). Parenting under the
    /// group's SceneSpawnContainer keeps the model glued to its map position
    /// when the game shifts the floating scene origin.
    ///
    /// Each instance is uniformly scaled so its lamp pivot lands at the
    /// profile's real fire height (LighthouseScale, H0 measured by
    /// rn_lighthouse_height). The scale is re-applied on every (re)spawn
    /// because save/load respawns the prefab unscaled.
    ///
    /// Spawning is asynchronous (terrain wait, then InstantiatePrefab), so
    /// each placement is a two-phase poll: resolve (or reuse) the sandbox
    /// group, then wait for the rotator child to appear under the group's
    /// SceneSpawnContainer and attach the rotation component. Reusing an
    /// existing group also covers save/load.
    /// </summary>
    [NonSerializedInGameState]
    public sealed class LighthousePlacer : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Lighthouse]";
        private const int MaxAttemptsPerProfile = 3600;
        private const int RotatorGracePeriodFrames = 600;

        /// <summary>
        /// Alignment corrections beyond this are treated as a pathological
        /// frame state and skipped (the next tick re-measures). Time-compressed
        /// travel legitimately drifts static groups by tens of kilometers
        /// (observed in-game), so the clamp only guards against nonsense.
        /// </summary>
        private const float MaxAlignmentShiftM = 100000f;

        /// <summary>Global log throttle for repeated alignment shifts.</summary>
        private const float MinAlignLogIntervalSeconds = 5f;

        private static float lastAlignLogTime = -60f;

        /// <summary>
        /// Safety valve for harbor clusters: never more than this many 3D
        /// lighthouses alive at once (the nearest win). The German Bight DB
        /// density can exceed this within the 30 km radius.
        /// </summary>
        public const int MaxConcurrent3D = 64;

        /// <summary>Distance-gate maintenance cadence (seconds).</summary>
        private const float GateIntervalSeconds = 0.5f;

        [Inject] private static ResourceManager resourceManager = null;
        [Inject] private static GameState gameState = null;

        private static LighthousePlacer instance;

        private sealed class Placement
        {
            public readonly LighthouseProfile Profile;
            public readonly string GroupName;
            public readonly Vector2 EncounterKm;
            public SandboxGroup Group;
            public GameObject GlowHolder;
            public Transform Rotator;
            public string ResolvedPrefab;
            public bool WantsSpawn;
            public bool Done;
            public bool Fatal;
            public int Attempts;
            public int RotatorWaitAttempts;

            public Placement(LighthouseProfile profile, string groupName)
            {
                Profile = profile;
                GroupName = groupName;
                EncounterKm = Coordinates.ToWorldPosition(profile.Latitude, profile.Longitude);
            }

            public bool Spawned
            {
                get { return Group != null; }
            }
        }

        private readonly List<Placement> placements = new List<Placement>();
        private readonly List<Placement> gateScratch = new List<Placement>();
        private readonly Dictionary<string, Placement> byGroupName =
            new Dictionary<string, Placement>(StringComparer.Ordinal);
        private readonly HashSet<string> fleetGroupNames = new HashSet<string>(StringComparer.Ordinal);
        private float gateTimer;
        private float staleCleanupTimer;
        private int staleCleanupPassesLeft;
        private bool staleCleanupArmed;
        private int gameplayWaitAttempts;
        private bool dumpedStatics;

        /// <summary>
        /// Stale-group cleanup schedule: first pass on the first gate tick
        /// after Gameplay, then a few more (save restore and the game's own
        /// LoadStaticGroups can lag the transition), then stop.
        /// </summary>
        private const float StaleCleanupIntervalSeconds = 10f;
        private const int StaleCleanupPasses = 6;

        public static void EnsureRunning()
        {
            if (instance.IsNotNull())
            {
                return;
            }

            var holder = new GameObject("Lighthouse Placer");
            instance = holder.AddComponent<LighthousePlacer>();
            instance.BuildFleet();
        }

        /// <summary>Sandbox entity of a placed lighthouse by group name.</summary>
        public static SandboxEntity GetEntity(string groupName)
        {
            Placement placement;
            if (instance == null || !instance.byGroupName.TryGetValue(groupName, out placement))
            {
                return null;
            }

            return placement.Group != null && placement.Group.Entities.Count > 0
                ? placement.Group.Entities[0]
                : null;
        }

        public static void Reset()
        {
            // Hot-reload: destroy the holder GO too, not just null the
            // instance. Otherwise the old Update() loop keeps polling
            // placements while EnsureRunning spawns a second one.
            if (instance.IsNotNull())
                UnityEngine.Object.Destroy(instance.gameObject);
            instance = null;
        }

        private void BuildFleet()
        {
            placements.Clear();
            byGroupName.Clear();
            fleetGroupNames.Clear();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (LighthouseProfile profile in LighthouseProfiles.EnabledFleet())
            {
                string groupName = GroupNameFor(profile);
                int suffix = 2;
                string unique = groupName;
                while (!usedNames.Add(unique))
                {
                    unique = groupName + "_" + suffix++;
                }

                var placement = new Placement(profile, unique);
                placements.Add(placement);
                byGroupName[unique] = placement;
                fleetGroupNames.Add(unique);
            }

            Debug.Log(LogPrefix + " fleet built: " + placements.Count + " profiles (3D gated at " +
                LighthouseDistanceGate.SpawnRadiusKm.ToString("F0", CultureInfo.InvariantCulture) +
                " km, cap " + MaxConcurrent3D + ")");
        }

        private static string GroupNameFor(LighthouseProfile profile)
        {
            // Curated MOD-* codes keep the historical name-only group (old
            // saves hold "Lighthouse Wangerooge"); registry entries use the
            // unique B-number because names repeat across harbors.
            if (string.IsNullOrEmpty(profile.Code) || profile.Code.StartsWith("MOD-"))
            {
                return "Lighthouse " + profile.Name;
            }

            return "Lighthouse " + profile.Code + " " + profile.Name;
        }

        private void Update()
        {
            try
            {
                if (!IsGameplayReady())
                {
                    return;
                }

                RunGate();

                for (int i = 0; i < placements.Count; i++)
                {
                    Placement placement = placements[i];
                    if (!placement.WantsSpawn || placement.Fatal || placement.Done)
                    {
                        continue;
                    }

                    if (placement.Group == null)
                    {
                        TryResolveGroup(placement);
                    }
                    else
                    {
                        TryAttachRotator(placement);
                    }
                }

                if (!staleCleanupArmed)
                {
                    // Arm on the first tick after Gameplay: save restore and
                    // the game's own LoadStaticGroups have run by then.
                    staleCleanupArmed = true;
                    staleCleanupPassesLeft = StaleCleanupPasses;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " placement error: " + e.Message);
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Distance-gate maintenance: spawn near profiles (nearest first,
        /// capped), despawn far ones. Runs on the GateIntervalSeconds timer,
        /// not per frame.
        /// </summary>
        private void RunGate()
        {
            gateTimer += Time.unscaledDeltaTime;
            if (gateTimer < GateIntervalSeconds)
            {
                return;
            }

            gateTimer = 0f;

            float lat;
            float lon;
            if (!TryGetPlayerLatLon(out lat, out lon))
            {
                return;
            }

            Vector2 playerKm = Coordinates.ToWorldPosition(lat, lon);

            gateScratch.Clear();
            int spawnedCount = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                Placement placement = placements[i];
                double distanceKm = LighthouseDistanceGate.DistanceKm(
                    placement.EncounterKm.x, placement.EncounterKm.y, playerKm.x, playerKm.y);
                LighthouseGateAction action = LighthouseDistanceGate.Decide(
                    placement.Spawned || placement.WantsSpawn, distanceKm);

                if (action == LighthouseGateAction.Despawn)
                {
                    Teardown(placement, distanceKm);
                }
                else if (action == LighthouseGateAction.Spawn)
                {
                    gateScratch.Add(placement);
                }
                else if (placement.Spawned)
                {
                    spawnedCount++;
                }
            }

            // Nearest-first grant under the concurrency cap.
            if (gateScratch.Count > 0)
            {
                Vector2 playerRef = playerKm;
                gateScratch.Sort(delegate(Placement a, Placement b)
                {
                    double da = LighthouseDistanceGate.DistanceKm(
                        a.EncounterKm.x, a.EncounterKm.y, playerRef.x, playerRef.y);
                    double db = LighthouseDistanceGate.DistanceKm(
                        b.EncounterKm.x, b.EncounterKm.y, playerRef.x, playerRef.y);
                    int byDistance = da.CompareTo(db);
                    // Deterministic tie-break for co-distant lights.
                    return byDistance != 0 ? byDistance : a.GroupName.CompareTo(b.GroupName);
                });

                for (int i = 0; i < gateScratch.Count; i++)
                {
                    Placement placement = gateScratch[i];
                    if (placement.WantsSpawn)
                    {
                        continue; // already granted
                    }

                    if (spawnedCount >= MaxConcurrent3D)
                    {
                        break;
                    }

                    placement.WantsSpawn = true;
                    spawnedCount++;
                    Debug.Log(LogPrefix + " '" + placement.GroupName + "' entering 3D range (" +
                        LighthouseDistanceGate.DistanceKm(
                            placement.EncounterKm.x, placement.EncounterKm.y, playerRef.x, playerRef.y)
                            .ToString("F1", CultureInfo.InvariantCulture) + " km)");
                }
            }

            RealignSpawned();
            RunStaleGroupCleanup();
        }

        /// <summary>
        /// Removes sandbox groups left behind by old saves for beacons that
        /// are no longer in the fleet (excluded from Leuchtfeuer.json by
        /// land curation). Groups are DontDestroyIfEmpty, so without this
        /// the 3D model of an excluded beacon would survive every load.
        /// Runs on a short schedule after Gameplay: save restore and the
        /// game's own LoadStaticGroups can lag the state transition.
        /// </summary>
        private void RunStaleGroupCleanup()
        {
            if (staleCleanupPassesLeft <= 0)
            {
                return;
            }

            staleCleanupTimer += Time.unscaledDeltaTime;
            if (staleCleanupTimer < StaleCleanupIntervalSeconds)
            {
                return;
            }

            staleCleanupTimer = 0f;
            staleCleanupPassesLeft--;
            if (CleanupStaleGroups())
            {
                // A pass that found and removed something is followed by one
                // more pass to confirm the sandbox is clean.
                staleCleanupPassesLeft = Math.Max(staleCleanupPassesLeft, 1);
            }
        }

        /// <summary>True when at least one stale group was destroyed.</summary>
        private bool CleanupStaleGroups()
        {
            Sandbox sandbox = Sandbox.Instance;
            if (sandbox == null)
            {
                return false;
            }

            List<SandboxGroup> groups = sandbox.GetGroupsDirect();
            if (groups == null || groups.Count == 0)
            {
                return false;
            }

            // Snapshot first: DestroyGroup mutates the sandbox collection.
            List<SandboxGroup> doomed = null;
            for (int i = 0; i < groups.Count; i++)
            {
                SandboxGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                if (LighthouseStaleGroupPolicy.ShouldDestroy(group.name, fleetGroupNames))
                {
                    if (doomed == null)
                    {
                        doomed = new List<SandboxGroup>();
                    }

                    doomed.Add(group);
                }
            }

            if (doomed == null)
            {
                return false;
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                string groupName = doomed[i].name;
                try
                {
                    doomed[i].DestroyGroup();
                    Debug.Log(LogPrefix + " removed stale group '" + groupName +
                        "' (beacon excluded from the fleet by data curation)");
                }
                catch (Exception e)
                {
                    Debug.LogError(LogPrefix + " stale group '" + groupName + "' removal failed: " + e.Message);
                }
            }

            return true;
        }

        /// <summary>
        /// Re-anchors every spawned light to the current floating-origin frame
        /// on each gate tick. Teleports and time skips rebase the world without
        /// moving our mid-session-spawned static groups (observed in-game: the
        /// lamp stays kilometers behind the ship while the distance gate keeps
        /// reporting chart proximity). AlignToWorldFrame is idempotent with a
        /// ~1 m dead zone, so the steady state costs two transform reads per
        /// light per tick.
        /// </summary>
        private void RealignSpawned()
        {
            for (int i = 0; i < placements.Count; i++)
            {
                Placement placement = placements[i];
                if (!placement.Spawned || !placement.Done || placement.Rotator.IsNull())
                {
                    continue;
                }

                SceneSpawnContainer container = placement.Group.SceneSpawnContainer;
                if (container.IsNull())
                {
                    continue;
                }

                AlignToWorldFrame(placement, container.transform, placement.Rotator);
            }
        }

        private bool TryGetPlayerLatLon(out float lat, out float lon)
        {
            lat = 0f;
            lon = 0f;
            var provider = new GamePlayerPositionProvider();
            return provider.TryGetPosition(out lat, out lon);
        }

        /// <summary>Removes the 3D presence of a placement (group + glow).</summary>
        private void Teardown(Placement placement, double distanceKm)
        {
            bool had = placement.Spawned || placement.GlowHolder != null;
            placement.WantsSpawn = false;
            placement.Done = false;
            placement.Attempts = 0;
            placement.RotatorWaitAttempts = 0;
            if (placement.GlowHolder != null)
            {
                UnityEngine.Object.Destroy(placement.GlowHolder);
                placement.GlowHolder = null;
            }

            if (placement.Group != null)
            {
                try
                {
                    placement.Group.DestroyGroup();
                }
                catch (Exception e)
                {
                    Debug.LogError(LogPrefix + " '" + placement.GroupName + "' despawn failed: " + e.Message);
                }

                placement.Group = null;
            }

            if (had)
            {
                Debug.Log(LogPrefix + " '" + placement.GroupName + "' left 3D range (" +
                    distanceKm.ToString("F1", CultureInfo.InvariantCulture) + " km) — despawned");
            }
        }

        /// <summary>
        /// Phase 1: find the existing sandbox group (covers save/load) or
        /// create and spawn a new one from the profile.
        /// </summary>
        private bool TryResolveGroup(Placement placement)
        {
            Sandbox sandbox = Sandbox.Instance;
            if (sandbox.IsNull() || resourceManager.IsNull())
            {
                return false;
            }

            if (++placement.Attempts > MaxAttemptsPerProfile)
            {
                Debug.LogError(LogPrefix + " '" + placement.GroupName + "' giving up after " +
                    MaxAttemptsPerProfile + " attempts; check prefab/bundle availability");
                placement.Fatal = true;
                return false;
            }

            SandboxGroup existing = sandbox.FindGroup(placement.GroupName);
            if (existing.IsNotNull())
            {
                // A group restored from a save can lose its runtime-created
                // entity (the synthetic EntityType is not resolvable from the
                // game's data sheets), leaving an empty shell whose spawn
                // container never fills. Such a group can never produce the
                // rotator child — heal it instead of reusing it.
                if (existing.Entities.Count == 0)
                {
                    Debug.LogWarning(LogPrefix + " '" + placement.GroupName +
                        "': group exists but has no entities (broken save restore); removing and re-creating");
                    placement.Group = existing;
                    HealGroup(placement);
                    return false;
                }

                placement.Group = existing;
                placement.ResolvedPrefab = null;
                placement.RotatorWaitAttempts = 0;
                // Old saves may carry the entity with DisplayInUI/IsPublic
                // true — reset so the game stops creating its own marker.
                for (int i = 0; i < existing.Entities.Count; i++)
                {
                    existing.Entities[i].DisplayInUI = false;
                    existing.Entities[i].IsPublic = false;
                }

                return true;
            }

            if (placement.ResolvedPrefab == null)
            {
                placement.ResolvedPrefab = ResolvePrefabPath(placement.Profile.PrefabPath);
                if (placement.ResolvedPrefab == null)
                {
                    Debug.LogError(LogPrefix + " '" + placement.GroupName + "': prefab not found: '" +
                        placement.Profile.PrefabPath + "'");
                    placement.Fatal = true;
                    return false;
                }
            }

            Vector2 position = placement.EncounterKm;

            try
            {
                EntityType entityType = CreateEntityType(placement.GroupName, placement.ResolvedPrefab);
                Country country = sandbox.GetCountryByCode("Neutral", false);
                if (country.IsNull())
                {
                    // Countries load a few frames after a save is restored;
                    // wait for them instead of treating this as fatal.
                    LogThrottled(placement, "waiting for countries");
                    return false;
                }

                EntityBlueprint blueprint = entityType.GetCountryBlueprint(country);

                SandboxGroup group = SandboxGroup.Create<SandboxStaticGroup>(placement.GroupName, "Location", position, country);
                group.Flags |= GroupFlags.DontDestroyIfEmpty;

                SandboxEntity entity = SandboxEntity.Create(placement.GroupName, blueprint);
                entity.Enabled = true;
                // The map marker is the mod's own (LighthouseMapMarker) — keep
                // the entity out of the game's EntityMarkersManager so no
                // duplicate Unknown-icon marker is created.
                entity.DisplayInUI = false;
                entity.IsPublic = false;
                entity.RandomizeSpawnPosition = false;

                group.AddEntity(entity);
                entity.Position = position;

                sandbox.AddGroup(group);
                group.IsGroupPublic = true;
                sandbox.AddPublicGroup(group);

                group.Spawn();
                placement.Group = group;
            }
            catch (Exception e)
            {
                if (!dumpedStatics)
                {
                    Debug.LogError(LogPrefix + " placement failed on first attempt; dumping statics:");
                    DumpStatics();
                    Debug.LogException(e);
                    dumpedStatics = true;
                }
                else
                {
                    LogThrottled(placement, "placement not ready yet: " + e.Message);
                }

                return false;
            }

            Vector2 latlong = Coordinates.ToLatLong(position);
            Debug.Log(LogPrefix + " '" + placement.GroupName + "' placed at Lat " +
                latlong.x.ToString("F6", CultureInfo.InvariantCulture) +
                " Lon " + latlong.y.ToString("F6", CultureInfo.InvariantCulture) +
                " prefab='" + placement.ResolvedPrefab + "' fireHeight=" +
                placement.Profile.FireHeightM.ToString("F0", CultureInfo.InvariantCulture) + "m" +
                " speed=" + placement.Profile.RotationDegreesPerSecond.ToString("F1", CultureInfo.InvariantCulture) + "°/s");
            return true;
        }

        /// <summary>
        /// Phase 2: wait for the spawned prefab's rotator child to appear
        /// under the group's SceneSpawnContainer, apply the per-lighthouse
        /// height scale, then attach the rotation component and the glow
        /// controller.
        /// </summary>
        private void TryAttachRotator(Placement placement)
        {
            SceneSpawnContainer container = placement.Group.SceneSpawnContainer;
            if (container.IsNull())
            {
                placement.RotatorWaitAttempts++;
                LogThrottled(placement, "waiting for spawn container");
                if (placement.RotatorWaitAttempts >= RotatorGracePeriodFrames)
                {
                    Debug.LogWarning(LogPrefix + " '" + placement.GroupName +
                        "': spawn container missing after " + placement.RotatorWaitAttempts +
                        " frames — recreating group");
                    HealGroup(placement);
                }

                return;
            }

            Transform rotator = TransformHelpers.FindChildRecursive(container.transform, placement.Profile.RotatorObjectName);
            if (rotator.IsNull())
            {
                placement.RotatorWaitAttempts++;
                if (placement.RotatorWaitAttempts % 120 == 0)
                {
                    Debug.LogWarning(LogPrefix + " '" + placement.GroupName + "': waiting for rotator object '" +
                        placement.Profile.RotatorObjectName + "' entities=" + placement.Group.Entities.Count +
                        " container=" + DescribeContainer(container) +
                        " (attempt " + placement.RotatorWaitAttempts + ")");
                }

                // The entity can legitimately take a while to spawn (terrain
                // wait, async prefab load), but a save-restored group whose
                // entity never respawns would block forever. After the grace
                // period, drop the broken group and re-create it from scratch.
                if (placement.RotatorWaitAttempts >= RotatorGracePeriodFrames)
                {
                    Debug.LogWarning(LogPrefix + " '" + placement.GroupName +
                        "': rotator not found after " + placement.RotatorWaitAttempts +
                        " frames — entity did not respawn; recreating group");
                    HealGroup(placement);
                }

                return;
            }

            ApplyHeightScale(placement, container.transform);

            if (placement.Profile.RotationDegreesPerSecond <= 0f)
            {
                Debug.Log(LogPrefix + " '" + placement.GroupName + "': fixed light, rotator skipped");
            }
            else if (rotator.GetComponent<LighthouseRotator>().IsNotNull())
            {
                Debug.Log(LogPrefix + " '" + placement.GroupName + "': rotator already attached, skipping");
            }
            else
            {
                LighthouseRotator rotatorComponent = rotator.gameObject.AddComponent<LighthouseRotator>();
                rotatorComponent.SpeedDegreesPerSecond = placement.Profile.RotationDegreesPerSecond;
                float periodSeconds = 360f / placement.Profile.RotationDegreesPerSecond;
                Debug.Log(LogPrefix + " '" + placement.GroupName + "': rotator attached to '" +
                    placement.Profile.RotatorObjectName + "' at " +
                    placement.Profile.RotationDegreesPerSecond.ToString("F1", CultureInfo.InvariantCulture) +
                    "°/s (" + periodSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s per turn)");
            }

            // The glow controller lives on its own scene-root GameObject so
            // it keeps running when the game LOD-deactivates the prefab and
            // survives save/load respawns. Re-point it to the current
            // container/rotator on every (re)attach.
            LighthouseBeamGlow glow = EnsureGlowController(placement);
            glow.Initialize(
                container.transform,
                container.transform.InverseTransformPoint(rotator.position),
                rotator,
                placement.GroupName);

            AlignToWorldFrame(placement, container.transform, rotator);

            Debug.Log(LogPrefix + " '" + placement.GroupName + "': glow controller ready");

            placement.Done = true;
        }

        /// <summary>
        /// Re-anchors the spawned hierarchy so the lamp pivot sits at the
        /// entity's charted encounter position in the CURRENT floating-origin
        /// frame, using the game's own authoritative conversion
        /// (Sandbox.EncounterToSceneSpace). Time-compressed travel advances
        /// the scene origin via AddOffsetAlt, which does NOT translate static
        /// groups' scene content (no OffsetAdded subscribers), so spawned
        /// lighthouses go stale by up to tens of kilometers; this restores
        /// them. Runs on every attach and on every gate tick — idempotent.
        /// See LighthouseAlignment for the equivalent ship-anchored math.
        /// </summary>
        private void AlignToWorldFrame(Placement placement, Transform container, Transform rotator)
        {
            placement.Rotator = rotator;

            Sandbox sandbox = Sandbox.Instance;
            SandboxEntity entity = placement.Group != null && placement.Group.Entities.Count > 0
                ? placement.Group.Entities[0]
                : null;
            if (sandbox.IsNull() || entity == null)
            {
                Debug.LogWarning(LogPrefix + " '" + placement.GroupName +
                    "': alignment skipped (no sandbox/entity)");
                return;
            }

            Vector3 target = sandbox.EncounterToSceneSpace(entity.Position);
            float shiftX = target.x - rotator.position.x;
            float shiftZ = target.z - rotator.position.z;

            float shiftSq = shiftX * shiftX + shiftZ * shiftZ;
            if (shiftSq < 1f)
            {
                return; // already within ~1 m of the charted position
            }

            if (shiftSq > MaxAlignmentShiftM * MaxAlignmentShiftM)
            {
                // A correction this large means the frame state is pathological
                // (mid-transition teleport); applying it would fling the model.
                // Skip and let the next tick re-measure.
                Debug.LogWarning(LogPrefix + " '" + placement.GroupName + "' alignment shift (" +
                    shiftX.ToString("F0", CultureInfo.InvariantCulture) + ", " +
                    shiftZ.ToString("F0", CultureInfo.InvariantCulture) +
                    ") m exceeds sanity clamp — skipped");
                return;
            }

            container.position += new Vector3(shiftX, 0f, shiftZ);
            if (Time.unscaledTime - lastAlignLogTime < MinAlignLogIntervalSeconds)
            {
                return;
            }

            lastAlignLogTime = Time.unscaledTime;
            Debug.Log(LogPrefix + " '" + placement.GroupName + "' aligned: shift=(" +
                shiftX.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                shiftZ.ToString("F1", CultureInfo.InvariantCulture) +
                ") m, lamp scene=(" +
                rotator.position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                rotator.position.z.ToString("F1", CultureInfo.InvariantCulture) + ") m");
        }

        /// <summary>
        /// Uniformly scales the spawned prefab so the lamp pivot sits at the
        /// profile's real fire height. Save/load respawns the prefab at
        /// scale 1, so this re-applies on every attach (idempotent).
        /// </summary>
        private static void ApplyHeightScale(Placement placement, Transform container)
        {
            float lampHeight = PlayerPrefs.GetFloat(
                LighthouseHeightProbe.PlayerPrefsKey, LighthouseScale.DefaultLampHeightM);
            float scale = LighthouseScale.Compute(placement.Profile.FireHeightM, lampHeight);
            Vector3 current = container.localScale;
            if (Math.Abs(current.x - scale) < 0.001f
                && Math.Abs(current.y - scale) < 0.001f
                && Math.Abs(current.z - scale) < 0.001f)
            {
                return;
            }

            container.localScale = new Vector3(scale, scale, scale);
            Debug.Log(LogPrefix + " '" + placement.GroupName + "' scaled x" +
                scale.ToString("F3", CultureInfo.InvariantCulture) +
                " (fireHeight " + placement.Profile.FireHeightM.ToString("F0", CultureInfo.InvariantCulture) +
                " m / lamp " + lampHeight.ToString("F2", CultureInfo.InvariantCulture) + " m)");
        }

        /// <summary>
        /// Removes a broken group (empty or never-spawned) from the sandbox so
        /// the next TryResolveGroup pass creates a fresh one under the same
        /// name. DestroyGroup internally calls sandbox.RemoveGroup.
        /// </summary>
        private bool HealGroup(Placement placement)
        {
            SandboxGroup group = placement.Group;
            if (group == null)
            {
                return false;
            }

            try
            {
                group.DestroyGroup();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " '" + placement.GroupName + "' heal failed: " + e.Message);
                Debug.LogException(e);
                return false;
            }

            placement.Group = null;
            placement.RotatorWaitAttempts = 0;
            return true;
        }

        private static string DescribeContainer(SceneSpawnContainer container)
        {
            if (container.IsNull())
            {
                return "null";
            }

            string result = container.transform.childCount + " children";
            if (container.transform.childCount == 0)
            {
                return result;
            }

            result += ": ";
            for (int i = 0; i < container.transform.childCount && i < 8; i++)
            {
                if (i > 0)
                {
                    result += ", ";
                }

                result += "'" + container.transform.GetChild(i).name + "'";
            }

            if (container.transform.childCount > 8)
            {
                result += ", ...";
            }

            return result;
        }

        private LighthouseBeamGlow EnsureGlowController(Placement placement)
        {
            if (placement.GlowHolder != null)
            {
                LighthouseBeamGlow existingComponent = placement.GlowHolder.GetComponent<LighthouseBeamGlow>();
                if (existingComponent != null)
                {
                    return existingComponent;
                }

                return placement.GlowHolder.AddComponent<LighthouseBeamGlow>();
            }

            // A glow holder may survive from a previous session of this
            // group name (scene reload) — reuse instead of stacking.
            GameObject existing = GameObject.Find("RadioNavLighthouseGlow_" + placement.GroupName);
            if (existing.IsNotNull())
            {
                placement.GlowHolder = existing;
                LighthouseBeamGlow component = existing.GetComponent<LighthouseBeamGlow>();
                return component != null ? component : existing.AddComponent<LighthouseBeamGlow>();
            }

            var holder = new GameObject("RadioNavLighthouseGlow_" + placement.GroupName);
            placement.GlowHolder = holder;
            return holder.AddComponent<LighthouseBeamGlow>();
        }

        private bool IsGameplayReady()
        {
            // The game's SandboxGroup statics are only injected once gameplay
            // begins (LoadStaticGroups runs on the Gameplay state transition);
            // attempt before that and SandboxGroup.Create throws NREs.
            if (gameState.IsNull() || gameState.CurrentState != GameState.State.Gameplay)
            {
                if (++gameplayWaitAttempts % 120 == 1)
                {
                    Debug.Log(LogPrefix + " waiting for gameplay state (attempt " + gameplayWaitAttempts + ")");
                }

                return false;
            }

            return true;
        }

        private void LogThrottled(Placement placement, string message)
        {
            if (placement.Attempts % 120 == 0)
            {
                Debug.Log(LogPrefix + " '" + placement.GroupName + "': " + message + " (attempt " + placement.Attempts + ")");
            }
        }

        /// <summary>
        /// Probe whether the profile's prefab name exists in the AssetBundle;
        /// the bundle asset name must match exactly (case-sensitive).
        /// </summary>
        private static string ResolvePrefabPath(string prefabName)
        {
            Resource<GameObject> resource = resourceManager.RetrieveAsset<GameObject>(prefabName, null, false);
            bool valid = resource.Valid;
            resource.Free();
            if (valid)
            {
                return prefabName;
            }

            return null;
        }

        // P5.1: the failing-attempt retry path calls this every frame; without
        // reused buffers that is a List(18)+string[18]+int[18]+row churn per
        // frame. Rows are transient (discarded on the throwing attempt), so
        // buffer reuse is safe.
        private static readonly List<string> entityTypeValues = new List<string>(18);
        private static readonly string[] entityTypeKeys = new string[18];
        private static readonly int[] entityTypeKeyHashes = new int[18];
        private static bool entityTypeBuffersPrimed;

        /// <summary>
        /// Builds an EntityType (category "Structure", so the game keeps the
        /// prefab's own Y and treats it as a terrain object) whose prefab path
        /// points at the mod's bundle asset. Column layout mirrors the game's
        /// "Entities/Types" sheet; the 18th value guards EntityBlueprint's
        /// index-based parameter reads against nulls.
        /// </summary>
        private static EntityType CreateEntityType(string typeName, string prefabPath)
        {
            if (!entityTypeBuffersPrimed)
            {
                entityTypeKeys[9] = "Prefab Path";
                entityTypeKeyHashes[9] = "Prefab Path".GetHashCode();
                entityTypeBuffersPrimed = true;
            }

            List<string> values = entityTypeValues;
            EntityTypeRowValues.BuildValues(typeName, prefabPath, values);

            var row = new GameDataRow("Entities/Types", entityTypeKeys, entityTypeKeyHashes, values);
            return new EntityType(row);
        }

        /// <summary>
        /// Dumps the game's injected statics used by the placement pipeline so
        /// a failed first attempt pinpoints exactly what is still null.
        /// </summary>
        private static void DumpStatics()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            DumpStatic("Sandbox.Instance", Sandbox.Instance);
            DumpStatic("SandboxGroup.sandbox", typeof(SandboxGroup).GetField("sandbox", flags)?.GetValue(null));
            DumpStatic("SandboxGroup.savesManager", typeof(SandboxGroup).GetField("savesManager", flags)?.GetValue(null));
            DumpStatic("SandboxGroup.wolfpack", typeof(SandboxGroup).GetField("wolfpack", flags)?.GetValue(null));
            DumpStatic("SandboxGroup.entitiesDatabase", typeof(SandboxGroup).GetField("entitiesDatabase", flags)?.GetValue(null));
            DumpStatic("SandboxEntity.gameTime", typeof(SandboxEntity).GetField("gameTime", flags)?.GetValue(null));
            DumpStatic("SandboxEntity.sandbox", typeof(SandboxEntity).GetField("sandbox", flags)?.GetValue(null));
            DumpStatic("SandboxEntity.resourceManager", typeof(SandboxEntity).GetField("resourceManager", flags)?.GetValue(null));
            DumpStatic("SandboxEntity.entityTypesDatabase", typeof(SandboxEntity).GetField("entityTypesDatabase", flags)?.GetValue(null));
            DumpStatic("EntityType.sandbox", typeof(EntityType).GetField("sandbox", flags)?.GetValue(null));
        }

        private static void DumpStatic(string name, object value)
        {
            bool isUnityObject = value is UnityEngine.Object;
            bool destroyedOrNull = value == null || (isUnityObject && !(UnityEngine.Object)value);
            Debug.Log(LogPrefix + " static " + name + " = " + (destroyedOrNull ? "null/destroyed" : "ok (" + value.GetType().Name + ")"));
        }
    }
}
