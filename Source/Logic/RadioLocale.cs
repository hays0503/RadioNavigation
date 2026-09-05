using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Locale labels + game-language resolution behind RadioStoryIntegration
    /// (SRP: label tables are not patch wiring). The integration keeps the
    /// Harmony entry points, init orchestration and panel lifecycle; key
    /// constants, the label table and the cached language reflection live
    /// here. Label selection runs offline in RadioLocaleTests. The
    /// ResolveType name is a SourceCheck contract (GameTypeCache gate):
    /// RadioStoryIntegration.ResolveType must keep delegating, so this
    /// class calls GameTypeCache.Resolve directly.
    /// </summary>
    internal static class RadioLocale
    {
        internal const string Key = "Radio/OpenT3PLL38";
        internal const string LeaveStationKey = "Radio/LeaveStation";
        internal const string Prefix = "Stories/";

        private const string DefaultLabel = "T3PLL38 on-board radio receiver for very long and long waves";

        private static readonly Dictionary<string, string> Labels =
            new Dictionary<string, string>
        {
            { "russian", "T3PLL38 \u0431\u043E\u0440\u0442\u043E\u0432\u043E\u0439 \u0440\u0430\u0434\u0438\u043E\u043F\u0440\u0438\u0435\u043C\u043D\u0438\u043A \u0441\u0432\u0435\u0440\u0445\u0434\u043B\u0438\u043D\u043D\u044B\u0445 \u0438 \u0434\u043B\u0438\u043D\u043D\u044B\u0445 \u0432\u043E\u043B\u043D" },
        };

        private static PropertyInfo languageProp;
        private static MethodInfo loadSingletonMethod;
        private static Type localeType;

        internal static void Reset()
        {
            languageProp = null;
            loadSingletonMethod = null;
            localeType = null;
        }

        internal static void EnsureInitialized()
        {
            var singletonType = GameTypeCache.Resolve("DWS.Common.InjectionFramework.ScriptableObjectSingleton");
            if (singletonType.IsNotNull())
                loadSingletonMethod = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
            localeType = GameTypeCache.Resolve("UBOAT.Game.Core.Data.Locale");
            if (!loadSingletonMethod.IsNull() && !localeType.IsNull())
                languageProp = localeType.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance);
        }

        internal static bool MatchesLocaleKey(string key)
        {
            return key == Prefix + Key || key == Key;
        }

        internal static string GetLocalizedLabel()
        {
            try
            {
                return GetLabel(GetCurrentLanguage());
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RadioNav] GetLocalizedLabel error: " + ex);
            }
            return DefaultLabel;
        }

        internal static string GetLabel(string language)
        {
            if (language.IsNotNull() && Labels.TryGetValue(language, out string label))
                return label;
            return DefaultLabel;
        }

        internal static string GetCurrentLanguage()
        {
            try
            {
                if (loadSingletonMethod.IsNull() || localeType.IsNull() || languageProp.IsNull())
                    return "english";
                object locale = loadSingletonMethod.Invoke(null, new object[] { localeType });
                if (locale.IsNull()) return "english";
                string lang = languageProp.GetValue(locale, null) as string;
                return lang != null ? lang.ToLowerInvariant() : "english";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RadioNav] GetCurrentLanguage failed: " + ex);
                return "english";
            }
        }
    }
}
