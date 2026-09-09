using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Decides which sandbox groups the lighthouse fleet should remove
    /// (pure C#, offline-tested). Group names are mod-owned: every group
    /// the placer creates is named "Lighthouse ..." (curated profiles keep
    /// the historical "Lighthouse <name>", registry entries use
    /// "Lighthouse <B-code> <name>"). When a beacon is excluded from the
    /// fleet by data curation, its group survives in old saves (groups are
    /// flagged DontDestroyIfEmpty) and would keep the 3D model alive even
    /// though neither the map nor the fleet knows the beacon anymore —
    /// this policy identifies exactly those stale leftovers.
    ///
    /// Anything without the mod's "Lighthouse " prefix is never touched,
    /// so game-spawned and player-created groups are safe.
    /// </summary>
    public static class LighthouseStaleGroupPolicy
    {
        public const string GroupPrefix = "Lighthouse ";

        /// <summary>
        /// True when a sandbox group named <paramref name="groupName"/>
        /// carries the mod's prefix but is not part of the current fleet
        /// (<paramref name="fleetGroupNames"/> holds every group name the
        /// placer may own, already uniquified).
        /// </summary>
        public static bool ShouldDestroy(string groupName, ICollection<string> fleetGroupNames)
        {
            if (string.IsNullOrEmpty(groupName) || fleetGroupNames == null)
            {
                return false;
            }

            if (!groupName.StartsWith(GroupPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return !fleetGroupNames.Contains(groupName);
        }
    }
}
