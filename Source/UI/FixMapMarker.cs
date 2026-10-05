using System.Collections.Generic;
using UBOAT.Game.Sandbox;
using DWS.Common.Resources;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Nav-map markers for the sextant running fix (SEXTANT_PLAN v1.12;
    /// native game points since 2026-11-05): one marker for the fix pin plus
    /// one per cocked-hat vertex, all real <c>MapPointUI</c> points through
    /// <see cref="NavMapMarkerBase"/> — so they can be dragged, erased and
    /// snapped by the charting tools (Lines/Circles/Angles/Bearing). The
    /// worksheet publishes the last computed fix through the static state
    /// below; the base reconciles the points with it every 1 s. The worksheet
    /// and marker model are restored from the current save, not session statics.
    ///
    /// Deletion (the map eraser / Delete) removes ONLY the marker: the fix
    /// data, the worksheet card and the probe are untouched — the markers are
    /// the visible result of the calculations, not their source. An erased
    /// marker stays hidden until the next observation (I) or the series reset
    /// (O), otherwise the 1 Hz card refresh would republish it instantly.
    ///
    /// The tooltip legend is Latin-only ("lat/lon"): the map marker tooltip
    /// uses the game's TMP font without Cyrillic glyphs (boxes in-game
    /// 2026-08-30), unlike the worksheet card which uses legacy OS Arial.
    /// Position recomputes every frame through EncounterToSceneSpace — the
    /// sandbox origin shifts between sessions.
    /// </summary>
    public sealed class FixMapMarker : NavMapMarkerBase
    {
        private const string IconPrefabName = "IconSextan";

        public static bool HasFix { get; private set; }
        public static double FixLat { get; private set; }
        public static double FixLon { get; private set; }
        public static string FixLabel { get; private set; }

        /// <summary>Cocked-hat vertices (P6): marker 0 is the fix, 1..3 are
        /// the pairwise LOP intersections.</summary>
        public static bool HasTriangle { get; private set; }
        private static readonly double[] VertexLat = new double[3];
        private static readonly double[] VertexLon = new double[3];

        /// <summary>Eraser dismissal of the fix pin: Set() is ignored until
        /// the next observation or series reset republishes the fix.</summary>
        public static bool Suppressed { get; private set; }

        // Per-marker dismissal: erasing one LOP vertex hides that vertex
        // only. Cleared with the pin's suppression on I/O.
        private static readonly HashSet<int> suppressedIndices = new HashSet<int>();

        // Explicit removal on the series reset (O) / scene start: the base
        // reconcile consumes it once (stale cleanup by state is off — the
        // double-load-safe behaviour, see NavMapMarkerBase.ApplyPlan).
        private static bool clearRequested;
        private static bool restoredFromSave;

        protected override bool PreserveInitialPointPosition => restoredFromSave;
        // Saved native rows can be created by MapChartingUI earlier in the
        // Loaded queue than the worksheet restore. Never stale-clean or move
        // them using the temporary empty model in that window.
        protected override bool ContentReady => !SextantSavePersistence.IsLoading;

        public static void Set(double latDeg, double lonDeg, string label)
        {
            if (Suppressed)
                return;
            HasFix = true;
            FixLat = latDeg;
            FixLon = lonDeg;
            FixLabel = label ?? "FIX";
        }

        /// <summary>Publishes the cocked-hat vertices (P6): A = B∩C, B = C∩A,
        /// C = A∩B, matching CockedHatResult.</summary>
        public static void SetTriangle(double aLat, double aLon,
            double bLat, double bLon, double cLat, double cLon)
        {
            VertexLat[0] = aLat;
            VertexLon[0] = aLon;
            VertexLat[1] = bLat;
            VertexLon[1] = bLon;
            VertexLat[2] = cLat;
            VertexLon[2] = cLon;
            HasTriangle = true;
        }

        /// <summary>Series reset (O): drop the pin and allow publishing.</summary>
        public static void Clear()
        {
            HasFix = false;
            HasTriangle = false;
            FixLabel = null;
            Suppressed = false;
            suppressedIndices.Clear();
            clearRequested = true;
            restoredFromSave = false;
        }

        /// <summary>New observation recorded (I): a fresh fix may publish again.</summary>
        public static void OnNewObservation()
        {
            Suppressed = false;
            suppressedIndices.Clear();
            restoredFromSave = false;
            // O can be pressed while the map is closed. Its deferred cleanup
            // must not delete a later result on the first map-open (nor turn
            // that destruction into a player erase). The desired set is now
            // authoritative; normal stale cleanup removes old LOP keys.
            clearRequested = false;
            HasTriangle = false;
        }

        internal static FixMarkerSnapshot CaptureState()
        {
            var state = new FixMarkerSnapshot
            {
                HasFix = HasFix,
                Lat = HasFix ? FixLat : 0,
                Lon = HasFix ? FixLon : 0,
                Label = FixLabel,
                HasTriangle = HasFix && HasTriangle,
                VertexLat = (double[])VertexLat.Clone(),
                VertexLon = (double[])VertexLon.Clone(),
                SuppressedMask = Suppressed ? 15 : 0
            };
            foreach (int index in suppressedIndices)
                state.SuppressedMask |= 1 << index;
            return state;
        }

        internal static void RestoreState(FixMarkerSnapshot state)
        {
            HasFix = state.HasFix;
            FixLat = state.Lat;
            FixLon = state.Lon;
            FixLabel = state.Label;
            HasTriangle = state.HasTriangle;
            System.Array.Copy(state.VertexLat, VertexLat, 3);
            System.Array.Copy(state.VertexLon, VertexLon, 3);
            Suppressed = false;
            suppressedIndices.Clear();
            for (int i = 0; i < 4; i++)
            {
                if ((state.SuppressedMask & (1 << i)) != 0)
                    suppressedIndices.Add(i);
            }
            // A bootstrap/session reset must not erase freshly restored rows.
            clearRequested = false;
            restoredFromSave = true;
        }

        /// <summary>Stale cleanup scope: only the fix marker's own keys
        /// (FIX / LOP) may be destroyed by this component's reconcile.</summary>
        protected override bool OwnsKey(string key)
        {
            return FixMarkerSnapshot.IndexOfKey(key) >= 0;
        }

        // Marker 0 = the fix pin, 1..3 = the cocked-hat vertices (P6).
        protected override int MarkerCount => 4;

        protected override string PointKey(int index)
        {
            return index == 0 ? MapMarkerPoints.FixKey : MapMarkerPoints.LopVertexKey(index - 1);
        }

        protected override MapMarkerKind PointKind(int index)
        {
            return index == 0 ? MapMarkerKind.Fix : MapMarkerKind.LopVertex;
        }

        protected override bool PointSuppressed(int index)
        {
            return Suppressed || suppressedIndices.Contains(index);
        }

        protected override void OnPointDestroyed(int index)
        {
            // MapChartingUI.Clear destroys ALL points during loading. It is
            // teardown, not a player erase to carry into the new save.
            if (SextantSavePersistence.IsLoading)
                return;
            suppressedIndices.Add(index);
            Debug.Log(LogPrefix + " fix marker erased by the map eraser (index=" + index + ")");
        }

        /// <summary>O / scene start: hand the fix + LOP keys to the reconcile
        /// once, so the markers actually disappear (state-driven stale cleanup
        /// is off by design — double-load safe).</summary>
        protected override void CollectExplicitRemovals(List<string> keys)
        {
            if (!clearRequested)
                return;
            clearRequested = false;
            for (int i = 0; i < MarkerCount; i++)
                keys.Add(PointKey(i));
        }

        protected override string HoverText(int index)
        {
            if (index == 0)
                return HasFix ? FixLabel : null;
            return HasFix && HasTriangle ? VertexLabel(index - 1) : null;
        }

        protected override string PinnedText(int index)
        {
            return HoverText(index);
        }

        protected override bool HasContent(int index)
        {
            return index == 0 ? HasFix : HasFix && HasTriangle;
        }

        protected override Vector2 GetEncounterPosition(int index)
        {
            if (index == 0)
                return Coordinates.ToWorldPosition((float)FixLat, (float)FixLon);
            int vertex = index - 1;
            return Coordinates.ToWorldPosition((float)VertexLat[vertex], (float)VertexLon[vertex]);
        }

        /// <summary>Vertex tooltip (Latin only — the map TMP font has no
        /// Cyrillic): the LOP pair and the coordinates.</summary>
        private static string VertexLabel(int vertex)
        {
            string pair = MapMarkerPoints.LopPair(vertex);
            return "LOP " + pair
                + "\nlat " + SextantDmFormat.FormatLatLonAscii(VertexLat[vertex], true)
                + " lon " + SextantDmFormat.FormatLatLonAscii(VertexLon[vertex], false);
        }

        /// <summary>
        /// IconSextan prefab (Image or SpriteRenderer), IconSonne pattern.
        /// ResourceManager is tried first, but on 2026.1-patch21 it cannot see
        /// the mod bundle (F229), so the icon is resolved straight from the
        /// already-loaded bundle as the fallback that actually works. Blank
        /// icon + one warning only when even the bundle lacks the prefab.
        /// </summary>
        protected override Sprite LoadIconSprite()
        {
            if (resourceManager == null)
                return null;

            Resource<GameObject> prefab =
                resourceManager.RetrieveAsset<GameObject>(IconPrefabName, this, false);
            if (prefab.Valid && prefab.Asset != null)
            {
                var image = prefab.Asset.GetComponent<UnityEngine.UI.Image>();
                if (image != null && image.sprite != null)
                    return image.sprite;
                var spriteRenderer = prefab.Asset.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null && spriteRenderer.sprite != null)
                    return spriteRenderer.sprite;
            }

            // F229: resolve from the bundle the mod itself loaded (prefab
            // Image/SpriteRenderer).
            Sprite fromBundle = LoadBundleIconSprite(IconPrefabName, null);
            if (fromBundle != null)
                return fromBundle;

            if (!iconWarned)
            {
                iconWarned = true;
                Debug.LogWarning(LogPrefix + " fix icon '" + IconPrefabName +
                    "' not found in bundle; fix marker icon will be blank");
            }
            return null;
        }
    }
}
