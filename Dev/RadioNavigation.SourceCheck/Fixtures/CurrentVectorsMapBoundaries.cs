using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    public enum HideFlags { DontSave = 52 }
    public enum KeyCode { Y = 121, LeftShift = 304, RightShift = 303 }
    public enum TextureFormat { RGBA32 = 4 }
    public enum TextureWrapMode { Clamp = 1 }

    public struct Vector2
    {
        public float x, y;
        public static readonly Vector2 zero = new Vector2(0f, 0f);
        public static readonly Vector2 one = new Vector2(1f, 1f);
        public Vector2(float a, float b) { x = a; y = b; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public static readonly Vector3 zero = new Vector3(0f, 0f, 0f);
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float s) { return new Vector3(a.x * s, a.y * s, a.z * s); }
    }

    public struct Color
    {
        public float r, g, b, a;
        public static readonly Color white = new Color(1f, 1f, 1f, 1f);
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }

    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d; }
    }

    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public static float Abs(float v) { return v < 0f ? -v : v; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }

    public class Object
    {
        public string name;
        public bool Destroyed;
        public static T FindObjectOfType<T>(bool includeInactive) where T : Object
        {
            return MapWorld.Objects.OfType<T>().FirstOrDefault(o => !o.Destroyed);
        }
        public static void Destroy(Object obj)
        {
            if (obj == null || obj.Destroyed) return;
            obj.Destroyed = true;
            var go = obj as GameObject;
            if (go != null)
            {
                for (int i = 0; i < go.Children.Count; i++) Destroy(go.Children[i]);
                for (int i = 0; i < go.Components.Count; i++) go.Components[i].Destroyed = true;
            }
        }
        public static void DontDestroyOnLoad(Object obj) { }
    }

    public static class MapWorld
    {
        public static readonly List<Object> Objects = new List<Object>();
        public static readonly Dictionary<string, int> IntPrefs = new Dictionary<string, int>();
        public static readonly Dictionary<string, float> FloatPrefs = new Dictionary<string, float>();
        public static readonly HashSet<KeyCode> HeldKeys = new HashSet<KeyCode>();
        public static readonly List<string> Logs = new List<string>();
        public static GameObject SelectedInputObject;
        public static float UnscaledTime;

        public static void Reset()
        {
            Objects.Clear();
            IntPrefs.Clear();
            FloatPrefs.Clear();
            HeldKeys.Clear();
            Logs.Clear();
            SelectedInputObject = null;
            UnscaledTime = 10f;
            UI.Graphic.defaultGraphicMaterial = new Material { name = "DefaultUI" };
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : Component { return gameObject.GetComponentInParent<T>(); }
    }

    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;
        public Vector3 eulerAngles;
        private int siblingIndex;

        public void SetParent(Transform p, bool worldPositionStays)
        {
            if (parent != null) parent.gameObject.Children.Remove(gameObject);
            parent = p;
            if (p != null)
            {
                p.gameObject.Children.Add(gameObject);
                siblingIndex = p.gameObject.Children.Count - 1;
            }
        }
        public int GetSiblingIndex() { return siblingIndex; }
        public void SetSiblingIndex(int idx) { siblingIndex = idx; }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, sizeDelta, anchoredPosition;
    }

    public sealed class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public readonly Transform transform;
        public bool activeSelf = true;

        public GameObject(string objName, params Type[] components)
        {
            name = objName;
            MapWorld.Objects.Add(this);
            bool wantRect = components != null && components.Contains(typeof(RectTransform));
            var tr = wantRect ? (Transform)new RectTransform() : new Transform();
            tr.gameObject = this;
            tr.name = objName;
            transform = tr;
            Components.Add(tr);
        }

        public static GameObject Find(string objName)
        {
            return MapWorld.Objects.OfType<GameObject>().FirstOrDefault(g => !g.Destroyed && g.name == objName);
        }

        public void SetActive(bool value) { activeSelf = value; }

        public T AddComponent<T>() where T : Component
        {
            if (typeof(T) == typeof(RectTransform) && transform is RectTransform) return (T)(object)transform;
            var instance = (T)Activator.CreateInstance(typeof(T), true);
            instance.gameObject = this;
            instance.name = name;
            Components.Add(instance);
            MapWorld.Objects.Add(instance);
            return instance;
        }

        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => !c.Destroyed); }
        public T[] GetComponents<T>() where T : Component { return Components.OfType<T>().Where(c => !c.Destroyed).ToArray(); }
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform cur = transform; cur != null; cur = cur.parent)
            {
                T hit = cur.gameObject.GetComponent<T>();
                if (hit != null) return hit;
            }
            return null;
        }
    }

    public sealed class Camera : Component
    {
        public Ray NextCenterRay = new Ray(new Vector3(0f, 10000f, 0f), new Vector3(0f, -1f, 0f));
        public Ray ViewportPointToRay(Vector3 pt) { return NextCenterRay; }
    }

    public sealed class Texture2D : Object
    {
        public HideFlags hideFlags;
        public TextureWrapMode wrapMode;
        public Texture2D(int w, int h, TextureFormat fmt, bool mip) { MapWorld.Objects.Add(this); }
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixels(Color[] pixels) { }
        public void Apply() { }
    }

    public sealed class Sprite : Object
    {
        public HideFlags hideFlags;
        public static Sprite Create(Texture2D tex, Rect r, Vector2 pivot)
        {
            var s = new Sprite();
            MapWorld.Objects.Add(s);
            return s;
        }
    }

    public sealed class Material : Object
    {
        public HideFlags hideFlags;
        public readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public Material Source;
        public Material() { MapWorld.Objects.Add(this); }
        public Material(Material src) : this() { Source = src; }
        public void SetInt(string key, int value) { Ints[key] = value; }
    }

    public static class Time { public static float unscaledTime { get { return MapWorld.UnscaledTime; } } }
    public static class Input { public static bool GetKey(KeyCode k) { return MapWorld.HeldKeys.Contains(k); } }
    public static class Debug { public static void Log(string s) { MapWorld.Logs.Add(s); } public static void LogWarning(string s) { MapWorld.Logs.Add("WARN:" + s); } }

    public static class PlayerPrefs
    {
        public static int GetInt(string k, int def) { int v; return MapWorld.IntPrefs.TryGetValue(k, out v) ? v : def; }
        public static void SetInt(string k, int v) { MapWorld.IntPrefs[k] = v; }
        public static float GetFloat(string k, float def) { float v; return MapWorld.FloatPrefs.TryGetValue(k, out v) ? v : def; }
        public static void SetFloat(string k, float v) { MapWorld.FloatPrefs[k] = v; }
        public static void Save() { }
    }
}

namespace UnityEngine.Rendering { public enum CompareFunction { Always = 8 } }

namespace UnityEngine.EventSystems
{
    public sealed class EventSystem
    {
        public static readonly EventSystem current = new EventSystem();
        public UnityEngine.GameObject currentSelectedGameObject { get { return UnityEngine.MapWorld.SelectedInputObject; } }
    }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.MonoBehaviour
    {
        public static UnityEngine.Material defaultGraphicMaterial;
        public UnityEngine.Material material;
        public UnityEngine.Color color;
        public bool raycastTarget = true;
    }
    public class Image : Graphic
    {
        public UnityEngine.Sprite sprite;
        public UnityEngine.RectTransform rectTransform { get { return (UnityEngine.RectTransform)transform; } }
    }
    public class InputField : UnityEngine.MonoBehaviour { }
}

namespace TMPro { public class TMP_InputField : UnityEngine.MonoBehaviour { } }
namespace DWS.Common.InjectionFramework
{
    public sealed class InjectionFramework
    {
        public static readonly InjectionFramework Instance = new InjectionFramework();
        public T GetInstance<T>() where T : class { return null; }
    }
}
namespace UBOAT.Game.Sandbox
{
    public sealed class Sandbox : UnityEngine.MonoBehaviour
    {
        public static Sandbox Instance;
        public UnityEngine.Vector3 EncounterToSceneSpace(UnityEngine.Vector2 km) { return new UnityEngine.Vector3(km.x * 1000f, 0f, km.y * 1000f); }
        public UnityEngine.Vector2 SceneToEncounterSpace(UnityEngine.Vector3 m) { return new UnityEngine.Vector2(m.x * 0.001f, m.z * 0.001f); }
    }
}
namespace UBOAT.Game.Scene.Entities { public sealed class PlayerShip : UnityEngine.MonoBehaviour { public float Depth = 18f; } }
namespace UBOAT.Game.Scene.Camera
{
    public enum CameraMode { FPP, SectionView, Map }
    public sealed class MapCameraController
    {
        public enum MapDisplayMode { Normal = 1, Headquarters = 2 }
        public MapDisplayMode Mode = MapDisplayMode.Normal;
        public float CameraElevation = 50000f;
    }
    public sealed class MainCamera : UnityEngine.MonoBehaviour
    {
        public static MainCamera Instance;
        public bool isActiveAndEnabled = true;
        public CameraMode CurrentMode = CameraMode.Map;
        public MapCameraController MapController = new MapCameraController();
        public UnityEngine.Camera Camera;
    }
}
namespace UBOAT.Game.UI
{
    public sealed class MapChartingUI : UnityEngine.MonoBehaviour
    {
        private UnityEngine.Transform linesContainer;
        public void SetLinesContainer(UnityEngine.Transform t) { linesContainer = t; }
    }
}
namespace UBOAT.Mods.RadioNavigation
{
    internal static class UnityObjectExtensions { public static bool IsNotNull(this object obj) { return obj != null; } }
    public static class CurrentRuntimeAdapter
    {
        public static bool HasField = true;
        public static bool IsCoastReady = true;
        public static UBOAT.Game.Scene.Entities.PlayerShip BoundShip;
        public static DateTime Utc = new DateTime(1940, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        public static Func<UnityEngine.Vector2, float, UnityEngine.Vector3> Sampler;
        public static int SampleCalls;
        public static DateTime GameUtc() { return Utc; }
        public static bool TrySampleEncounterAtDepth(UnityEngine.Vector2 km, float depthMeters, DateTime utc, out UnityEngine.Vector3 vel)
        {
            SampleCalls++;
            vel = Sampler != null ? Sampler(km, depthMeters) : new UnityEngine.Vector3(1.5f, 0f, 0f);
            return vel.x != 0f || vel.z != 0f;
        }
    }
}
