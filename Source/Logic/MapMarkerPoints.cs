using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Kind of a mod map marker (drives the static/dynamic policy).</summary>
    public enum MapMarkerKind
    {
        Fix = 0,
        LopVertex = 1,
        Lighthouse = 2,
        Station = 3
    }

    /// <summary>Per-frame policy of one marker (pure output of Evaluate).</summary>
    public struct MapMarkerPolicy
    {
        /// <summary>The point must exist (create/adopt). False for a fix/LOP
        /// marker the player erased — suppressed until the next result.</summary>
        public bool Exists;
        /// <summary>The point catches the pointer (hover/tools/drag/eraser).</summary>
        public bool Interactive;
        /// <summary>The true position is restored every frame (static markers:
        /// the game's drag must not move them).</summary>
        public bool PositionLocked;
        /// <summary>Shown by the declutter.</summary>
        public bool Visible;
    }

    /// <summary>
    /// Pure helpers behind the mod's native map points (author 2026-11-05):
    /// adoption keys, the static/dynamic policy and the reconcile plan. No
    /// Unity — unit-tested offline (Tests/MapMarkerPointsTests.cs).
    ///
    /// Every mod point is named with <see cref="Prefix"/>; the name is the
    /// adoption key after a save/load (the game recreates the saved points,
    /// so the mod must adopt them instead of creating duplicates). The label
    /// of a point is disabled by the bridge, so the key is invisible in game.
    /// </summary>
    public static class MapMarkerPoints
    {
        public const string Prefix = "RadioNav ";
        public const string FixKey = Prefix + "FIX";

        /// <summary>LOP vertex key; index 0..2 = pairs B+C, C+A, A+B
        /// (matches FixMapMarker / CockedHatResult order).</summary>
        public static string LopVertexKey(int index)
        {
            return Prefix + "LOP " + LopPair(index);
        }

        public static string LighthouseKey(string code)
        {
            return Prefix + "LH " + (code ?? string.Empty);
        }

        public static string StationKey(string callsign)
        {
            return Prefix + "STN " + (callsign ?? string.Empty);
        }

        public static string LopPair(int index)
        {
            switch (index)
            {
                case 0: return "B+C";
                case 1: return "C+A";
                default: return "A+B";
            }
        }

        /// <summary>True when the point name belongs to the mod.</summary>
        public static bool IsOwned(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>Static markers cannot be moved or erased; they only serve
        /// as anchors for the measuring tools.</summary>
        public static bool IsStatic(MapMarkerKind kind)
        {
            return kind == MapMarkerKind.Lighthouse || kind == MapMarkerKind.Station;
        }

        /// <summary>Frame policy of one marker.</summary>
        public static MapMarkerPolicy Evaluate(MapMarkerKind kind, bool suppressed,
            bool visible, bool pointsToolActive, bool eraserActive)
        {
            bool isStatic = IsStatic(kind);
            MapMarkerPolicy policy;
            policy.Exists = isStatic || !suppressed;
            policy.Visible = visible;
            policy.PositionLocked = isStatic;
            // The Points tool opens the game's rename popup on click and the
            // name is our adoption key — the mod's points stay inert then.
            // The eraser must not see the static markers at all.
            policy.Interactive = visible
                && !pointsToolActive
                && !(isStatic && eraserActive);
            return policy;
        }

        /// <summary>
        /// Reconcile plan: every desired key is adopted when an owned point
        /// with that name already exists, otherwise created; every owned point
        /// not in the desired set is stale (duplicate, orphan after a rename,
        /// marker from an older session) and must be destroyed.
        /// </summary>
        public static void Plan(IList<string> desiredKeys, IList<string> existingOwnedNames,
            List<string> toCreate, List<string> toAdopt, List<string> toDestroy)
        {
            if (toCreate == null || toAdopt == null || toDestroy == null)
                return;

            toCreate.Clear();
            toAdopt.Clear();
            toDestroy.Clear();

            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (existingOwnedNames != null)
            {
                for (int i = 0; i < existingOwnedNames.Count; i++)
                {
                    if (IsOwned(existingOwnedNames[i]))
                        existing.Add(existingOwnedNames[i]);
                }
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            if (desiredKeys != null)
            {
                for (int i = 0; i < desiredKeys.Count; i++)
                {
                    string key = desiredKeys[i];
                    if (string.IsNullOrEmpty(key) || !desired.Add(key))
                        continue;
                    if (existing.Contains(key))
                        toAdopt.Add(key);
                    else
                        toCreate.Add(key);
                }
            }

            foreach (string name in existing)
            {
                if (!desired.Contains(name))
                    toDestroy.Add(name);
            }
        }
    }
}
