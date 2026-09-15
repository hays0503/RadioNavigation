using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Cached assembly-qualified game Type resolution. Unifies two duplicates:
    /// RadioStoryIntegration.ResolveType (uncached, two-assembly fallback) and
    /// SkyVisibilityResolver.typeCache (cached, single assembly, on the
    /// per-frame IsPlayerOutside path — P5.1). Pure BCL, unit-tested offline
    /// in GameTypeCacheTests. Type objects are assembly-level and survive
    /// scene loads; Clear exists for domain-reload hygiene. Misses are NOT
    /// cached: game assemblies load lazily, so a miss now may hit later and
    /// a cached null would stick forever.
    public static class GameTypeCache
    {
        private static readonly string[] GameAssemblies = { "com.uboat.game", "Assembly-CSharp" };

        private static readonly Dictionary<string, Type> cache = new Dictionary<string, Type>();
        // Default-probe hot path (Resolve(typeName)) keyed by typeName alone so
        // no per-call "name\0assembly" string is built on the frame path.
        private static readonly Dictionary<string, Type> defaultCache = new Dictionary<string, Type>();

        public static Type Resolve(string typeName)
        {
            Type type;
            if (!defaultCache.TryGetValue(typeName, out type))
            {
                type = ResolveUncached(typeName, GameAssemblies);
                if (type != null)
                    defaultCache[typeName] = type;
            }
            return type;
        }

        public static Type Resolve(string typeName, params string[] probeAssemblies)
        {
            string key = typeName + "\0" + string.Join("\0", probeAssemblies);
            if (!cache.TryGetValue(key, out Type type))
            {
                type = ResolveUncached(typeName, probeAssemblies);
                if (type != null)
                    cache[key] = type;
            }
            return type;
        }

        private static Type ResolveUncached(string typeName, string[] probeAssemblies)
        {
            foreach (string assembly in probeAssemblies)
            {
                Type type = Type.GetType(typeName + ", " + assembly);
                if (type != null)
                    return type;
            }
            return null;
        }

        public static void Clear()
        {
            cache.Clear();
            defaultCache.Clear();
        }
    }
}
