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
    /// below; the base reconciles the points with it every 0.25 s.
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
        }

        /// <summary>New observation recorded (I): a fresh fix may publish again.</summary>
        public static void OnNewObservation()
        {
            Suppressed = false;
            suppressedIndices.Clear();
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
            suppressedIndices.Add(index);
            Debug.Log(LogPrefix + " fix marker erased by the map eraser (index=" + index + ")");
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
        /// IconSextan prefab only (SpriteRenderer + sprite, IconSonne
        /// pattern) — no fallback chain by author's decision. Blank icon +
        /// one warning when the bundle predates the prefab.
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
