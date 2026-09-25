using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Seam for scene-content creation (P5.5 slice 1, F21). The production
    /// implementation works on real GameObjects; tests supply a fake over
    /// string names so the guard logic is verifiable without Unity.
    /// </summary>
    public interface ISceneContentHost
    {
        bool Exists(string rootName);
        void CreateRoot(string rootName, Type componentType);
    }

    /// <summary>
    /// Owns per-scene content construction (extracted from ModBootstrapper):
    /// map components on the radio panel, DontDestroyOnLoad sky roots and
    /// scene runner startups. Tables are the single source of truth for both
    /// the production path and the unit tests.
    /// </summary>
    public static class SceneContentBuilder
    {
        public struct SkyRoot
        {
            public readonly string Name;
            public readonly Type ComponentType;

            public SkyRoot(string name, Type componentType)
            {
                Name = name;
                ComponentType = componentType;
            }
        }

        public static readonly SkyRoot[] SkyRoots =
        {
            new SkyRoot("StarSkyRoot", typeof(StarsMesh)),
            new SkyRoot("ConstellationRoot", typeof(ConstellationMesh)),
            new SkyRoot("PlanetRoot", typeof(PlanetRenderer)),
            new SkyRoot("MoonDiscRoot", typeof(MoonDiscRenderer)),
            new SkyRoot("SunDiscRoot", typeof(SunDiscRenderer))
        };

        public static readonly Type[] MapComponents =
        {
            typeof(StationMapOverlay),
            typeof(LighthouseMapMarker),
            typeof(StationMapMarker),
            typeof(FixMapMarker), // SEXTANT_PLAN v1.10
            typeof(SextantController), // SEXTANT_PLAN §7.9
            typeof(MapShapeEraser) // eraser/Delete removes shapes, not our markers
        };

        public static void EnsureSkyRoots(ISceneContentHost host)
        {
            if (host.IsNull())
                return;
            foreach (SkyRoot root in SkyRoots)
            {
                if (!host.Exists(root.Name))
                    host.CreateRoot(root.Name, root.ComponentType);
            }
        }

        public static void EnsureMapComponents(Func<Type, bool> has, Action<Type> add)
        {
            if (has.IsNull() || add.IsNull())
                return;
            foreach (Type component in MapComponents)
            {
                if (!has(component))
                    add(component);
            }
        }

        public static void EnsureRunners(Action lighthouse, Action phase0Probe)
        {
            if (lighthouse.IsNotNull())
                lighthouse();
            if (phase0Probe.IsNotNull())
                phase0Probe();
        }

        public static void Build(GameObject radio)
        {
            if (radio.IsNull())
                return;
            EnsureMapComponents(
                t => HasComponentNamed(radio, t),
                t => radio.AddComponent(t));
            EnsureSkyRoots(new UnitySceneContentHost());
            EnsureRunners(LighthousePlacer.EnsureRunning, StarSkyPhase0Probe.EnsureRunning);
        }

        /// <summary>
        /// Copy-agnostic component check: the mod assembly can be loaded twice
        /// (ProbeTest shim + mod loader), so a component of the SAME type name
        /// coming from another copy must satisfy the check. Checking by the
        /// local Type made every copy add its own marker set — the two
        /// reconciles then fought (stale-set destruction / visibility), which
        /// the author saw as chaotically blinking markers (2026-11-05).
        /// </summary>
        internal static bool HasComponentNamed(GameObject go, Type componentType)
        {
            if (go == null || componentType == null)
                return false;
            if (go.GetComponent(componentType) != null)
                return true;
            return HasComponentNamed(go, componentType.Name);
        }

        /// <summary>Name-based overload for callers that must not reference the
        /// component Type directly (runtime components of a second assembly
        /// copy).</summary>
        internal static bool HasComponentNamed(GameObject go, string typeName)
        {
            if (go == null || string.IsNullOrEmpty(typeName))
                return false;
            MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null
                    && behaviours[i].GetType().Name == typeName)
                    return true;
            }
            return false;
        }

        private sealed class UnitySceneContentHost : ISceneContentHost
        {
            public bool Exists(string rootName)
            {
                return GameObject.Find(rootName).IsNotNull();
            }

            public void CreateRoot(string rootName, Type componentType)
            {
                GameObject root = new GameObject(rootName);
                root.AddComponent(componentType);
                Object.DontDestroyOnLoad(root);
                Debug.Log("[RadioNav][Main] " + rootName + " created with " + componentType.Name + " component");
            }
        }
    }
}
