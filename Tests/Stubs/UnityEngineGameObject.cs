using System;

namespace UnityEngine
{
    /// <summary>
    /// Minimal Component/GameObject stubs for tests: enough to compile
    /// Source files that wire components by Type (SceneContentBuilder).
    /// Behavioural tests use fakes, never these stubs.
    /// </summary>
    public class Component : Object
    {
    }

    public class GameObject : Object
    {
        public string name;

        public GameObject(string name)
        {
            this.name = name;
        }

        public static GameObject Find(string name) { return null; }

        public Component GetComponent(Type type) { return null; }

        public Component AddComponent(Type type) { return null; }
    }
}
