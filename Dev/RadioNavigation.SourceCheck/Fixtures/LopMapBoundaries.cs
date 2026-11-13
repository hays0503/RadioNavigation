// Embedded boundaries only. Actual LOP cleanup, material owner and ownership
// predicates are compiled from Source, never reimplemented here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed, Queued;
        public Object() { World.Objects.Add(this); }
        public static T FindObjectOfType<T>(bool inactive) where T : Object { return FindObjectsOfType<T>(inactive).FirstOrDefault(); }
        public static T[] FindObjectsOfType<T>(bool inactive) where T : Object { return World.Objects.OfType<T>().Where(o => !o.Destroyed).ToArray(); }
        public static void Destroy(Object obj)
        {
            if (obj == null || obj.Destroyed || obj.Queued) return;
            obj.Queued = true;
            World.Events.Add("destroy:" + obj.name);
            World.Pending.Enqueue(obj);
        }
    }
    public static class World
    {
        public static readonly List<Object> Objects = new List<Object>();
        public static readonly Queue<Object> Pending = new Queue<Object>();
        public static readonly List<string> Events = new List<string>();
        public static void Reset() { Objects.Clear(); Pending.Clear(); Events.Clear(); }
        public static void FlushDestroy()
        {
            while (Pending.Count != 0)
            {
                Object obj = Pending.Dequeue();
                var go = obj as GameObject;
                if (go != null)
                {
                    // Model native Destroyed subscriptions: deleting an endpoint
                    // destroys shapes referencing that same Data object.
                    var point = go.GetComponent<UBOAT.Game.UI.Map.MapPointUI>();
                    if (point != null)
                    {
                        foreach (var shape in Objects.OfType<UBOAT.Game.UI.Map.MapLineUI>().Where(s => !s.Destroyed).ToArray())
                            if (shape.Data.PointA == point.Data || shape.Data.PointB == point.Data) Object.Destroy(shape.gameObject);
                    }
                    var line = go.GetComponent<UBOAT.Game.UI.Map.MapLineUI>();
                    if (line != null) UBOAT.Mods.RadioNavigation.MapSaveSanitizer.Database.RemoveLine(line.Data);
                    foreach (Component component in go.Components)
                    {
                        var method = component.GetType().GetMethod("OnDestroy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (method != null) method.Invoke(component, null);
                        component.Destroyed = true;
                    }
                    foreach (GameObject child in go.Children) Object.Destroy(child);
                }
                obj.Destroyed = true;
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentInChildren<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public bool Active = true;
        public GameObject(string objectName) { name = objectName; }
        public T AddComponent<T>() where T : Component
        {
            var value = (T)Activator.CreateInstance(typeof(T), true);
            value.gameObject = this;
            value.name = name;
            Components.Add(value);
            return value;
        }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(); }
        public T GetComponentInChildren<T>() where T : Component
        {
            return GetComponent<T>() ?? Children.Select(child => child.GetComponentInChildren<T>()).FirstOrDefault(value => value != null);
        }
        public void SetActive(bool active) { Active = active; }
    }
    public struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } }
    public struct Color { public float r, g, b, a; public Color(float red, float green, float blue, float alpha) { r = red; g = green; b = blue; a = alpha; } }
    public enum HideFlags { DontSave }
    public class Material : Object
    {
        public HideFlags hideFlags;
        public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
        public Material Source;
        public Material() { }
        public Material(Material original) { Source = original; foreach (var pair in original.Values) Values.Add(pair.Key, pair.Value); }
        public void SetInt(string key, int value) { Values[key] = value; }
    }
    public static class Debug { public static void LogWarning(string text) { } }
}
namespace UnityEngine.Rendering { public enum CompareFunction { Always = 8 } }
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.MonoBehaviour
    {
        public static UnityEngine.Material defaultGraphicMaterial;
        private UnityEngine.Material assigned;
        public UnityEngine.Material material { get { return assigned ?? defaultGraphicMaterial; } set { assigned = value; } }
        public UnityEngine.Color color;
    }
    public class Image : Graphic { }
    public class RawImage : Graphic { }
}
namespace TMPro
{
    public class TMP_FontAsset { public UnityEngine.Material material = new UnityEngine.Material(); }
    public class TextMeshProUGUI : UnityEngine.MonoBehaviour
    {
        public string text;
        public UnityEngine.Color color;
        public TMP_FontAsset font = new TMP_FontAsset();
        public UnityEngine.Material fontSharedMaterial;
    }
}
namespace UBOAT.Game.Sandbox
{
    public static class Coordinates { public static UnityEngine.Vector2 ToWorldPosition(float lat, float lon) { return new UnityEngine.Vector2(lon, lat); } }
}
namespace UBOAT.Game.Scene.Tasks
{
    public class MapPointData { public string Name; public UnityEngine.Vector2 Position; }
    public class MapLineData { public MapPointData PointA, PointB; }
    public class MapCircleData { public MapPointData PointA, PointB; }
    public class MapAngleData { public MapPointData PointA, PointB, PointC; }
    public class MapChartingDatabase
    {
        public readonly List<MapPointData> Points = new List<MapPointData>();
        public readonly List<MapLineData> Lines = new List<MapLineData>();
        public readonly List<MapCircleData> Circles = new List<MapCircleData>();
        public readonly List<MapAngleData> Angles = new List<MapAngleData>();
        public void RemoveLine(MapLineData data) { UnityEngine.World.Events.Add("remove-line"); Lines.Remove(data); }
        public void RemovePoint(MapPointData data) { UnityEngine.World.Events.Add("remove-point:" + data.Name); Points.Remove(data); }
    }
}
namespace UBOAT.Game.UI.Map
{
    public class MapPointUI : UnityEngine.MonoBehaviour
    {
        public UBOAT.Game.Scene.Tasks.MapPointData Data;
        public bool Interactive;
        public string Name { get { return Data.Name; } set { Data.Name = value; GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text = value; } }
    }
    public class MapLineUI : UnityEngine.MonoBehaviour { public UBOAT.Game.Scene.Tasks.MapLineData Data; }
    public class MapBearingUI : UnityEngine.MonoBehaviour { public UBOAT.Game.Scene.Tasks.MapLineData Data; }
}
namespace UBOAT.Game.UI
{
    public class MapChartingUI : UnityEngine.MonoBehaviour
    {
        public enum Tool { Points, Route }
        public Tool CurrentTool;
        public int FailPointAt = -1, PointCalls;
        public bool FailAfterLineRegistration;
        public UBOAT.Game.UI.Map.MapPointUI CreatePoint(UnityEngine.Vector2 position, string name)
        {
            if (++PointCalls == FailPointAt) throw new InvalidOperationException("Injected point failure");
            var go = new UnityEngine.GameObject(name);
            var point = go.AddComponent<UBOAT.Game.UI.Map.MapPointUI>();
            go.AddComponent<UnityEngine.UI.Image>();
            var label = new UnityEngine.GameObject(name + " label");
            label.AddComponent<TMPro.TextMeshProUGUI>();
            go.Children.Add(label);
            point.Data = new UBOAT.Game.Scene.Tasks.MapPointData { Name = name, Position = position };
            UBOAT.Mods.RadioNavigation.MapSaveSanitizer.Database.Points.Add(point.Data);
            return point;
        }
        public UBOAT.Game.UI.Map.MapLineUI CreateLine(UBOAT.Game.UI.Map.MapPointUI a, UBOAT.Game.UI.Map.MapPointUI b)
        {
            var go = new UnityEngine.GameObject("native-line");
            var line = go.AddComponent<UBOAT.Game.UI.Map.MapLineUI>();
            go.AddComponent<UnityEngine.UI.RawImage>();
            line.Data = new UBOAT.Game.Scene.Tasks.MapLineData { PointA = a.Data, PointB = b.Data };
            UBOAT.Mods.RadioNavigation.MapSaveSanitizer.Database.Lines.Add(line.Data);
            if (FailAfterLineRegistration) throw new InvalidOperationException("Injected line failure");
            return line;
        }
    }
}
namespace UBOAT.Mods.RadioNavigation
{
    internal static class MapSaveSanitizer
    {
        internal static UBOAT.Game.Scene.Tasks.MapChartingDatabase Database;
        internal static UBOAT.Game.Scene.Tasks.MapChartingDatabase ResolveDatabase() { return Database; }
    }
    internal static class WarnOnce { internal static bool ShouldLog(string key) { return false; } }
}
