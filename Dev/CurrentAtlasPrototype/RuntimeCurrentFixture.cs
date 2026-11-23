using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using PlayWay.Water.Extensions;
using UnityEngine;
using UBOAT.Mods.RadioNavigation;

// Only the scene/lifecycle facade is stubbed: the actual adapter, Harmony
// transpilers, drag law, and installed water DLL run unmodified.
namespace DWS.Common.InjectionFramework
{
    internal sealed class InjectionFramework
    {
        public static InjectionFramework Instance => null;
        public T GetInstance<T>() where T : class => null;
    }
}

namespace UBOAT.Game.Scene.Entities
{
    public class Entity : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<Entity> List =
            new System.Collections.Generic.List<Entity>();
        public object SandboxEntity { get; set; }
        public FixtureBlueprint Blueprint { get; set; }
        public Transform DockPoint => null;
        public float Depth { get; set; }
    }
    public class FixtureBlueprint
    {
        public FixtureBlueprint Type => this;
        public string Name { get; set; }
    }
    public class PlayerShip : Entity
    {
        public Rigidbody RigidBody => null;
        public bool Docked => false;
        public Component[] Rudders => null;
    }
}

namespace UBOAT.Game.Scene.Environment
{
    internal sealed class Weather : MonoBehaviour
    {
        public Transform Sea => null;
        public float TotalWindSpeed => 0f;
    }
}

namespace UBOAT.Game.UI.DevConsole
{
    internal static class DevConsole
    {
        public static void AddListener(string name, System.Action<string[]> handler) { }
    }
}

namespace UBOAT.Game.Sandbox
{
    internal sealed class Sandbox
    {
        public static Sandbox Instance => null;
        public Vector2 SceneToEncounterSpace(Vector3 scenePosition) => default;
    }

    internal static class Coordinates
    {
        public static Vector2 ToLatLong(Vector2 worldPos) => default;
    }
}

namespace UBOAT.Game.Core.Time
{
    internal sealed class GameTime : MonoBehaviour
    {
        public System.DateTime GmtDateTime => default;
    }
}

internal static class RuntimeCurrentFixture
{
    private static readonly Type Adapter = typeof(CurrentRuntimeAdapter);
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static UBOAT.Game.Scene.Entities.Entity nearby;

    public static void Install(string mode)
    {
        if (mode == "" || mode == "stock")
            return;
        var harmony = new Harmony("radionav.production-current-fixture");
        if (mode == "configured-zero")
        {
            Environment.SetEnvironmentVariable("RADIONAV_DIRECT_WIND", null);
            Environment.SetEnvironmentVariable("RADIONAV_CURRENT_FIELD", "0,0");
            CurrentRuntimeAdapter.EnsureRunning(harmony);
            if (GameObject.Find("RadioNavCurrentBinder") != null)
                throw new InvalidOperationException("Zero configuration installed a binder");
        }
        else if (!(bool)Adapter.GetMethod("TryInstall", Hidden).Invoke(null, new object[] { harmony }))
        {
            throw new InvalidOperationException("Production patch installation refused");
        }
        foreach (string name in new[] { "SimpleUpdate", "Execute" })
        {
            var method = typeof(NeuralNetworkPhysics).GetMethod(name,
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var info = Harmony.GetPatchInfo(method);
            bool installed = info != null && System.Linq.Enumerable.Contains(info.Owners, harmony.Id);
            if (installed != (mode != "configured-zero"))
                throw new InvalidOperationException("Wrong patch ownership on " + name);
        }
    }

    public static void Bind(NeuralNetworkPhysics hull, float east, float air, string kind)
    {
        Set("water", new Vector3(east, 0f, 0f));
        Set("wind", new Vector3(air, 0f, 0f));
        if (kind != "")
        {
            Set("useNearby", true);
            nearby = hull.GetComponentInParent<Rigidbody>().gameObject.AddComponent<UBOAT.Game.Scene.Entities.Entity>();
            nearby.SandboxEntity = new object();
            nearby.Blueprint = new UBOAT.Game.Scene.Entities.FixtureBlueprint { Name = kind };
            Adapter.GetMethod("BindNearby", Hidden).Invoke(null, new object[] { nearby });
            return;
        }
        object[] arguments = { hull, null };
        if (!(bool)Adapter.GetMethod("TryBuildStep", Hidden).Invoke(null, arguments))
            throw new InvalidOperationException("Production drag coefficient resolution refused");
        Set("step", arguments[1]);
        Set("target", hull);
        Set("docked", false);
    }

    public static void Tick(float exposure)
    {
        if (nearby == null)
            Set("exposure", Mathf.Clamp01(exposure));
        else
        {
            nearby.Depth = -exposure;
            CurrentRuntimeAdapter.ReadHullState();
        }
    }

    public static void Write(string output)
    {
        File.WriteAllText(Path.Combine(output, "runtime-current.csv"),
            "full,simple,water,air,airDeltaSquared\n" + string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4:R}\n", Get("appliedFull"), Get("appliedSimple"), Get("appliedWater"),
                Get("appliedAir"), ((Vector3)Get("lastAir")).sqrMagnitude));
    }

    private static void Set(string name, object value) => Adapter.GetField(name, Hidden).SetValue(null, value);
    private static object Get(string name) => Adapter.GetField(name, Hidden).GetValue(null);
}
