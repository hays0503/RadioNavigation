// HEADLESS TEST MODEL ONLY. Never referenced by the mod or linked into Dev/Tests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<MonoBehaviour> Components = new List<MonoBehaviour>();
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object
        { return Components.Where(c => c != null && (includeInactive || c.isActiveAndEnabled)).OfType<T>().ToArray(); }
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            var component = value as MonoBehaviour;
            if (!ReferenceEquals(component, null))
            {
                component.enabled = false; component.Invoke("OnDestroy");
                component.Destroyed = true; Components.Remove(component); return;
            }
            var root = value as GameObject;
            if (!ReferenceEquals(root, null))
                foreach (MonoBehaviour child in Components.Where(c => ReferenceEquals(c.gameObject, root)).ToArray()) Destroy(child);
            value.Destroyed = true;
        }
    }
    public class MonoBehaviour : Object
    {
        private bool active = true;
        public GameObject gameObject;
        public bool enabled
        {
            get { return active; }
            set
            {
                if (active == value) return;
                bool before = isActiveAndEnabled; active = value;
                if (before != isActiveAndEnabled) Invoke(isActiveAndEnabled ? "OnEnable" : "OnDisable");
            }
        }
        public bool isActiveAndEnabled { get { return !Destroyed && active && gameObject != null && gameObject.activeInHierarchy; } }
        public void ForceEnabledWithoutCallback(bool value) { active = value; } // Negative-control fixture only.
        public void Invoke(string method)
        {
            MethodInfo info = GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (info != null) info.Invoke(this, null);
        }
    }
    public sealed class GameObject : Object
    {
        private bool active = true;
        public bool activeInHierarchy { get { return active && !Destroyed; } }
        public GameObject(string name) { }
        public void SetActive(bool value)
        {
            if (active == value) return;
            active = value;
            foreach (MonoBehaviour component in Components.Where(c => ReferenceEquals(c.gameObject, this)).ToArray())
                if (component.enabled) component.Invoke(value ? "OnEnable" : "OnDisable");
        }
        public T AddComponent<T>() where T : MonoBehaviour { return (T)AddComponent(typeof(T)); }
        public MonoBehaviour AddComponent(Type type)
        {
            var value = (MonoBehaviour)Activator.CreateInstance(type, true);
            value.gameObject = this;
            Components.Add(value);
            if (value.isActiveAndEnabled) value.Invoke("OnEnable");
            return value;
        }
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object message) { Warnings.Add(Convert.ToString(message)); }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene
    {
        public string name; public int handle; public bool isLoaded;
        public bool IsValid() { return handle > 0; }
    }
    public static class SceneManager
    {
        public static Scene Main;
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static event Action<Scene> sceneUnloaded;
        public static Scene GetSceneByName(string name) { return Main; }
        public static int Subscriptions { get { return (sceneLoaded == null ? 0 : sceneLoaded.GetInvocationList().Length)
            + (sceneUnloaded == null ? 0 : sceneUnloaded.GetInvocationList().Length); } }
        public static void Load(int handle)
        { Main = new Scene { name = "Main Scene", handle = handle, isLoaded = true }; sceneLoaded?.Invoke(Main, LoadSceneMode.Single); }
        public static void Unload()
        { Scene old = Main; Main = default(Scene); sceneUnloaded?.Invoke(old); }
    }
}

namespace DWS.Common.InjectionFramework
{
    public sealed class InjectionFramework
    {
        public static InjectionFramework Instance;
        public readonly Dictionary<Type, object> Values = new Dictionary<Type, object>();
        public T GetInstance<T>() where T : class { object value; return Values.TryGetValue(typeof(T), out value) ? (T)value : null; }
    }
}

namespace UBOAT.Game.Core.Serialization
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class NonSerializedInGameStateAttribute : Attribute { }
    public sealed class SavesManager
    {
        public bool IsLoading;
        public bool Destroyed;
        // Model the Unity fake-null condition separately from managed identity.
        public static bool operator ==(SavesManager a, SavesManager b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(SavesManager a, SavesManager b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public event Action LoadingInitialization;
        public event Action<Queue<Action>> Loaded;
        public int Subscribers { get { return (LoadingInitialization == null ? 0 : LoadingInitialization.GetInvocationList().Length)
            + (Loaded == null ? 0 : Loaded.GetInvocationList().Length); } }
        public void Begin() { IsLoading = true; LoadingInitialization?.Invoke(); }
        public Queue<Action> Finish()
        { IsLoading = false; var queue = new Queue<Action>(); Loaded?.Invoke(queue); return queue; }
    }
}
namespace UBOAT.Game.Core.Time
{
    public sealed class GameTime { public DateTime GmtDateTime; }
}
namespace UBOAT.Game.Scene.Environment
{
    public sealed class Weather { public global::TOD_Sky Sky; }
}
public sealed class TOD_WorldParameters { public float Latitude, Longitude, UTC; }
public sealed class TOD_Sky { public TOD_WorldParameters World = new TOD_WorldParameters(); }
