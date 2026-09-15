using System;
using System.Collections.Generic;
using System.Reflection;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Caches reflection <see cref="MemberInfo"/> lookups per (Type, member name)
    /// so per-frame callers (SkyVisibilityResolver, SextantPipPose) stop paying
    /// Type.GetProperty/GetField — and, more importantly, stop building a
    /// string cache key on every call (which allocated each frame).
    ///
    /// Keys are the member name literals themselves; no concatenation. Misses
    /// are cached too: game type shapes are fixed, so re-probing a missing
    /// member every frame buys nothing. Pure BCL, unit-tested offline in
    /// TypeMemberCacheTests. Clear() exists for domain-reload hygiene.
    /// </summary>
    public static class TypeMemberCache
    {
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> instanceCache =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();

        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> staticCache =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();

        public static MemberInfo GetInstanceMember(Type type, string memberName)
        {
            return Get(type, memberName, BindingFlags.Instance, instanceCache);
        }

        public static MemberInfo GetStaticMember(Type type, string memberName)
        {
            return Get(type, memberName, BindingFlags.Static, staticCache);
        }

        private static MemberInfo Get(
            Type type,
            string memberName,
            BindingFlags scope,
            Dictionary<Type, Dictionary<string, MemberInfo>> cache)
        {
            if (type == null || memberName == null)
                return null;

            Dictionary<string, MemberInfo> byName;
            if (!cache.TryGetValue(type, out byName))
            {
                byName = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
                cache[type] = byName;
            }

            MemberInfo cached;
            if (byName.TryGetValue(memberName, out cached))
                return cached;

            BindingFlags flags = scope | BindingFlags.Public | BindingFlags.NonPublic;
            MemberInfo resolved = (MemberInfo)type.GetProperty(memberName, flags)
                ?? type.GetField(memberName, flags);
            byName[memberName] = resolved;
            return resolved;
        }

        public static void Clear()
        {
            instanceCache.Clear();
            staticCache.Clear();
        }
    }
}
