using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, object> store = new Dictionary<string, object>();

        public static void SetFloat(string key, float value) => store[key] = value;
        public static float GetFloat(string key, float defaultValue)
            => store.TryGetValue(key, out object v) && v is float f ? f : defaultValue;

        public static void SetInt(string key, int value) => store[key] = value;
        public static int GetInt(string key, int defaultValue)
            => store.TryGetValue(key, out object v) && v is int i ? i : defaultValue;

        public static void DeleteKey(string key) => store.Remove(key);
        public static void DeleteAll() => store.Clear();
        public static void Save() { }
    }
}
