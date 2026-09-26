using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Save/load hygiene for the mod's native map points (leak forensic
    /// 2026-09-25): the duplicate-point runaway (27,512 MapPointUI rows) must
    /// not survive into autosaves and must not be re-materialized on load.
    ///
    /// The base reconcile cleans duplicates only while the nav map is open; a
    /// dock/30-min/exit autosave before that re-serializes every junk row, and
    /// every load of a polluted save instantiates all of them before any mod
    /// code runs. Both hooks below close that window:
    ///   * Loaded (subscribed from mod init, before MapChartingUI subscribes)
    ///     prunes the database BEFORE the game's restore action runs, so the
    ///     junk GameObjects are never created;
    ///   * SavingStarted (fires one frame before serialization) prunes the
    ///     database and destroys the duplicate scene points as a backstop.
    ///
    /// Only our own names are touched ("RadioNav …" including the MCP test
    /// points, plus the legacy literal "MCP Point"), a point referenced by a
    /// line/circle/angle is always the survivor of its group, and duplicate
    /// scene GameObjects are destroyed only when rows actually were removed.
    /// </summary>
    internal static class MapSaveSanitizer
    {
        private static readonly FieldInfo DatabaseField = typeof(MapChartingUI).GetField(
            "mapChartingDatabase", BindingFlags.NonPublic | BindingFlags.Static);

        private static SavesManager savesManager;
        private static bool installed;
        private static bool loggedSaveRun;
        private static int lastRemoved = -1;

        /// <summary>Installs the database sanitizer on the game's save manager.
        /// Idempotent; safe to call every scene start until the singleton
        /// resolves (LoadSingleton returns null before the asset is loaded).</summary>
        internal static void TryInstall()
        {
            if (installed)
                return;

            SavesManager manager = ScriptableObjectSingleton.LoadSingleton<SavesManager>();
            if (manager == null)
                return;

            manager.Loaded -= OnLoaded;
            manager.Loaded += OnLoaded;
            manager.SavingStarted -= OnSavingStarted;
            manager.SavingStarted += OnSavingStarted;
            savesManager = manager;
            installed = true;
            Debug.Log("[RadioNav][Map] save sanitizer installed (Loaded + SavingStarted hooks)");
        }

        private static void OnLoaded(Queue<Action> queue)
        {
            Debug.Log("[RadioNav][Map] save sanitizer: Loaded hook fired (queue=" +
                (queue != null ? "yes" : "null") + ")");
            // The game destroys every point on load (SavesManagerOnLoadingStarted
            // -> Clear), and that destruction was recorded as a player erase by
            // the fix marker. Re-arm it: the fix data is untouched by a reload.
            FixMapMarker.ResetSuppressions();
            // The charting database is re-created/restored by the game's own
            // Loaded handlers (BackgroundTasksManager) and the map restore is
            // enqueued by MapChartingUI — so at handler time the database may
            // still be empty. Enqueue our prune: the game drains this queue
            // one action per frame AFTER all handlers ran, i.e. after the
            // database is populated but before the scene points are restored.
            if (queue != null)
                queue.Enqueue(delegate() { Sanitize("load"); });
            else
                Sanitize("load");
        }

        private static void OnSavingStarted()
        {
            Sanitize("save");
        }

        private static void Sanitize(string phase)
        {
            MapChartingDatabase database = ResolveDatabase();
            if (database == null)
                return;

            if (string.Equals(phase, "save", StringComparison.Ordinal))
            {
                if (!loggedSaveRun)
                {
                    loggedSaveRun = true;
                    Debug.Log("[RadioNav][Map] save sanitizer ran (save): points=" +
                        database.Points.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else
            {
                // One line per load: proves the load-phase prune runs on the
                // populated database (loading is rare, unlike autosaves).
                Debug.Log("[RadioNav][Map] save sanitizer ran (load): points=" +
                    database.Points.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var referenced = CollectReferencedPoints(database);
            var keepers = new Dictionary<string, MapPointData>(StringComparer.Ordinal);
            List<MapPointData> removals = null;

            var points = database.Points;
            for (int i = 0; i < points.Count; i++)
            {
                MapPointData data = points[i];
                if (data == null || !IsSanitizable(data.Name))
                    continue;

                string name = data.Name;
                MapPointData keeper;
                if (!keepers.TryGetValue(name, out keeper))
                {
                    keepers.Add(name, data);
                    continue;
                }

                if (!referenced.Contains(keeper) && referenced.Contains(data))
                {
                    // A line/circle/angle points at this instance: keep it and
                    // remove the previously kept twin instead.
                    keepers[name] = data;
                    AddRemoval(ref removals, keeper);
                    continue;
                }
                AddRemoval(ref removals, data);
            }

            if (removals == null)
            {
                lastRemoved = 0;
                return;
            }

            for (int i = 0; i < removals.Count; i++)
                database.RemovePoint(removals[i]);

            DestroyDuplicateScenePoints(keepers);
            if (lastRemoved != removals.Count || removals.Count > 0)
            {
                lastRemoved = removals.Count;
                Debug.LogWarning("[RadioNav][Map] save sanitizer (" + phase + "): removed " +
                    removals.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " duplicate owned point rows");
            }
        }

        private static void AddRemoval(ref List<MapPointData> removals, MapPointData data)
        {
            if (removals == null)
                removals = new List<MapPointData>();
            removals.Add(data);
        }

        private static HashSet<MapPointData> CollectReferencedPoints(MapChartingDatabase database)
        {
            var referenced = new HashSet<MapPointData>();

            var lines = database.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                MapLineData line = lines[i];
                if (line == null)
                    continue;
                if (line.PointA != null)
                    referenced.Add(line.PointA);
                if (line.PointB != null)
                    referenced.Add(line.PointB);
            }

            var circles = database.Circles;
            for (int i = 0; i < circles.Count; i++)
            {
                MapCircleData circle = circles[i];
                if (circle == null)
                    continue;
                if (circle.PointA != null)
                    referenced.Add(circle.PointA);
                if (circle.PointB != null)
                    referenced.Add(circle.PointB);
            }

            var angles = database.Angles;
            for (int i = 0; i < angles.Count; i++)
            {
                MapAngleData angle = angles[i];
                if (angle == null)
                    continue;
                if (angle.PointA != null)
                    referenced.Add(angle.PointA);
                if (angle.PointB != null)
                    referenced.Add(angle.PointB);
                if (angle.PointC != null)
                    referenced.Add(angle.PointC);
            }

            return referenced;
        }

        /// <summary>Destroys scene points that are duplicates of a kept row.
        /// Runs only on the save path with actual removals — a clean session
        /// pays nothing.</summary>
        private static void DestroyDuplicateScenePoints(Dictionary<string, MapPointData> keepers)
        {
            MapPointUI[] all = UnityEngine.Object.FindObjectsOfType<MapPointUI>(true);
            if (all.Length == 0)
                return;

            int destroyed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                MapPointUI point = all[i];
                if (point == null || point.Data == null)
                    continue;

                string name = point.Data.Name;
                if (!IsSanitizable(name))
                    continue;

                MapPointData keeper;
                if (keepers.TryGetValue(name, out keeper) && ReferenceEquals(point.Data, keeper))
                    continue;

                UnityEngine.Object.Destroy(point.gameObject);
                destroyed++;
            }

            if (destroyed > 0)
            {
                Debug.LogWarning("[RadioNav][Map] save sanitizer: destroyed " +
                    destroyed.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " duplicate scene points");
            }
        }

        /// <summary>Removes the row before a deferred Destroy, so a save that
        /// serializes in the same frame cannot resurrect the point (the game's
        /// own tools call RemovePoint before Destroy for the same reason).</summary>
        internal static void RemovePointDataBeforeDestroy(MapPointData data)
        {
            if (data == null)
                return;
            MapChartingDatabase database = ResolveDatabase();
            if (database == null)
                return;
            database.RemovePoint(data);
        }

        internal static bool IsSanitizable(string name)
        {
            return MapMarkerPoints.IsSanitizableName(name);
        }

        /// <remarks>F152 audit: <c>MapChartingDatabase</c> derives from
        /// <c>BackgroundTaskBase</c>, not <c>ScriptableObjectSingleton</c>, so the
        /// injection framework does not register it and the DI branch below
        /// returns null in practice — the working path is the private static
        /// field of the game, which is the very instance the game itself uses
        /// (verified live: prune reported the restored 215 rows). The DI probe
        /// is kept only as a cheap fast path for a future game change; do not
        /// read it as the primary route.</remarks>
        private static MapChartingDatabase ResolveDatabase()
        {
            InjectionFramework framework = InjectionFramework.Instance;
            if (framework != null)
            {
                MapChartingDatabase viaDi = framework.GetInstance<MapChartingDatabase>();
                if (viaDi != null)
                    return viaDi;
            }
            if (DatabaseField == null)
                return null;
            object value = DatabaseField.GetValue(null);
            return value as MapChartingDatabase;
        }
    }
}
