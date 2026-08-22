using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// Minimal Transform stub for tests: name + child list, enough for
    /// TransformHelpers.FindChildRecursive traversal tests.
    /// </summary>
    public class Transform
    {
        public string name;
        public readonly List<Transform> children = new List<Transform>();

        public Transform(string name)
        {
            this.name = name;
        }

        public Transform AddChild(string name)
        {
            Transform child = new Transform(name);
            children.Add(child);
            return child;
        }

        public int childCount => children.Count;

        public Transform GetChild(int index) => children[index];
    }
}
