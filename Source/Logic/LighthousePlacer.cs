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
    /// Places the mod's "lighthouse" 3D prefab for every enabled
    /// LighthouseProfile as a decorative sandbox entity (no map marker of its
    /// own — the map shows a charting point with a label from
    /// StationMapOverlay instead) and attaches a LighthouseRotator to the
    /// spawned prefab's rotator child when the profile defines a rotation
    /// speed.
    ///
    /// Uses the runtime group/entity pattern proven by the "Expanded Ice"
    /// workshop mod: SandboxGroup.Create + SandboxEntity.Create with an
    /// EntityType/EntityBlueprint built at runtime from a constructed
    /// GameDataRow (prefab path resolved through ResourceManager, so the
    /// prefab from this mod's own AssetBundle is used). Parenting under the
    /// group's SceneSpawnContainer keeps the model glued to its map position
    /// when the game shifts the floating scene origin.
    ///
    /// The spawned instance appears asynchronously (terrain wait, then
    /// InstantiatePrefab), so each placement is a two-phase poll: resolve
    /// (or reuse) the sandbox group, then wait for the rotator child to
    /// appear under the group's SceneSpawnContainer and attach the rotation
    /// component. Reusing an existing group also covers save/load: the game
    /// respawns the prefab without the runtime-attached component, so the
    /// placer re-attaches the rotator on every Main Scene start.
    /// </summary>
    [NonSerializedInGameState]
    public sealed class LighthousePlacer : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Lighthouse]";
        private const int MaxAttemptsPerProfile = 3600;
        private const int RotatorGracePeriodFrames = 600;

        [Inject] private static ResourceManager resourceManager = null;
        [Inject] private static GameState gameState = null;

        private static LighthousePlacer instance;

        /// <summary>
        /// The most recently placed lighthouse sandbox entity. The mod's own
        /// map marker (LighthouseMapMarker) uses it for positioning.
        /// </summary>
        public static SandboxEntity LighthouseEntity;

        private sealed class Placement
        {
            public readonly LighthouseProfile Profile;
            public readonly string GroupName;
            public SandboxGroup Group;
            public string ResolvedPrefab;
            public bool Done;
            public bool Fatal;
            public int Attempts;
            public int RotatorWaitAttempts;

            public Placement(LighthouseProfile profile)
            {
                Profile = profile;
                GroupName = "Lighthouse " + profile.Name;
            }
        }

        private readonly List<Placement> placements = new List<Placement>();
        private int gameplayWaitAttempts;
        private bool dumpedStatics;

        public static void EnsureRunning()
        {
            if (instance.IsNotNull())
            {
                return;
            }

            var holder = new GameObject("Lighthouse Placer");
            instance = holder.AddComponent<LighthousePlacer>();
            foreach (LighthouseProfile profile in LighthouseProfiles.Active())
            {
                instance.placements.Add(new Placement(profile));
            }
        }

        public static void Reset()
        {
            // Hot-reload: destroy the holder GO too, not just null the
            // instance. Otherwise the old Update() loop keeps polling
            // placements while EnsureRunning spawns a second one.
            if (instance.IsNotNull())
                UnityEngine.Object.Destroy(instance.gameObject);
            instance = null;
            // The entity belongs to the destroyed scene: drop the static so
            // LighthouseMapMarker re-resolves instead of reading a dead ref
            // (IsNull now reports destroyed objects as null).
            LighthouseEntity = null;
        }

        private void Update()
        {
            try
            {
                if (!IsGameplayReady())
                {
                    return;
                }

                foreach (Placement placement in placements)
                {
                    if (placement.Done || placement.Fatal)
                    {
                        continue;
                    }

                    if (++placement.Attempts > MaxAttemptsPerProfile)
                    {
                        Debug.LogError(LogPrefix + " '" + placement.GroupName + "' giving up after " +
                            MaxAttemptsPerProfile + " attempts; check prefab/bundle availability");
                        placement.Fatal = true;
                        continue;
                    }

                    Advance(placement);
                }

                if (AllFinished())
                {
                    Debug.Log(LogPrefix + " placement finished");
                    Destroy(gameObject);
                    instance = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " placement error: " + e.Message);
                Debug.LogException(e);
            }
        }

        private bool AllFinished()
        {
            foreach (Placement placement in placements)
            {
                if (!placement.Done && !placement.Fatal)
                {
                    return false;
                }
            }
            return true;
        }

        private void Advance(Placement placement)
        {
            if (placement.Group.IsNull() && !TryResolveGroup(placement))
            {
                return;
            }

            TryAttachRotator(placement);
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
                placement.RotatorWaitAttempts = 0;
                LighthouseEntity = existing.Entities[0];
                // Old saves may carry the entity with DisplayInUI/IsPublic
                // true — reset so the game stops creating its own marker.
                for (int i = 0; i < existing.Entities.Count; i++)
                {
                    existing.Entities[i].DisplayInUI = false;
                    existing.Entities[i].IsPublic = false;
                }
                Debug.Log(LogPrefix + " '" + placement.GroupName +
                    "': group already exists; reusing (entities=" + existing.Entities.Count + ")");
                return true;
            }

            if (placement.ResolvedPrefab.IsNull())
            {
                placement.ResolvedPrefab = ResolvePrefabPath(placement.Profile.PrefabPath);
                if (placement.ResolvedPrefab.IsNull())
                {
                    Debug.LogError(LogPrefix + " '" + placement.GroupName + "': prefab not found: '" +
                        placement.Profile.PrefabPath + "'");
                    placement.Fatal = true;
                    return false;
                }
            }

            Vector2 position = Coordinates.ToWorldPosition(placement.Profile.Latitude, placement.Profile.Longitude);

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
                LighthouseEntity = entity;

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
        /// under the group's SceneSpawnContainer and attach a
        /// LighthouseRotator when the profile defines a rotation speed.
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
            LighthouseBeamGlow glow = EnsureGlowController("RadioNavLighthouseGlow_" + placement.GroupName);
            glow.Initialize(
                container.transform,
                container.transform.InverseTransformPoint(rotator.position),
                rotator,
                placement.GroupName);
            Debug.Log(LogPrefix + " '" + placement.GroupName + "': glow controller ready");

            placement.Done = true;
        }

        /// <summary>
        /// Removes a broken group (empty or never-spawned) from the sandbox so
        /// the next TryResolveGroup pass creates a fresh one under the same
        /// name. DestroyGroup internally calls sandbox.RemoveGroup.
        /// </summary>
        private bool HealGroup(Placement placement)
        {
            SandboxGroup group = placement.Group;
            if (group.IsNull())
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

        private static LighthouseBeamGlow EnsureGlowController(string controllerName)
        {
            GameObject existing = GameObject.Find(controllerName);
            if (existing.IsNotNull())
            {
                LighthouseBeamGlow existingComponent = existing.GetComponent<LighthouseBeamGlow>();
                if (existingComponent.IsNotNull())
                {
                    return existingComponent;
                }
                return existing.AddComponent<LighthouseBeamGlow>();
            }

            var holder = new GameObject(controllerName);
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
                Debug.Log(LogPrefix + " prefab resolved: '" + prefabName + "'");
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
