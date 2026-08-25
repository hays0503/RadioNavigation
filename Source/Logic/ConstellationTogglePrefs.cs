using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Persisted on/off state for the constellation lines layer
    /// (STAR_SKY_PLAN §13.6). Default is ON — the lines rendered before the
    /// toggle existed, so the default preserves the old behavior.
    /// </summary>
    public static class ConstellationTogglePrefs
    {
        public const string Key = "RadioNav.ConstellationsEnabled";
        public const bool Default = true;

        public static bool Load()
        {
            return PlayerPrefs.GetInt(Key, Default ? 1 : 0) != 0;
        }

        public static void Save(bool enabled)
        {
            PlayerPrefs.SetInt(Key, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
