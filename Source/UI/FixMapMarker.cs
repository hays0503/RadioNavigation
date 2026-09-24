using UBOAT.Game.Sandbox;
using UBOAT.Game.UI;
using DWS.Common.Resources;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Nav-map marker for the sextant running fix (SEXTANT_PLAN v1.12): a
    /// thin subclass of the shared <see cref="NavMapMarkerBase"/> skeleton
    /// (same pattern as StationMapMarker). The sextant worksheet publishes
    /// the last computed fix through the static state below; the marker
    /// shows it on the map with a hover/pinned tooltip.
    ///
    /// The game's eraser tool (MapChartingUI.Tool.Eraser) can delete the
    /// marker: while hovered with the eraser active and LMB held, the fix
    /// pin is suppressed until the next observation (I) or series reset (O)
    /// — otherwise the 1 Hz card refresh would republish it instantly.
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

        /// <summary>Eraser dismissal: Set() is ignored until the next
        /// observation or series reset republishes the fix.</summary>
        public static bool Suppressed { get; private set; }

        private bool hovered;
        private MapChartingUI charting;

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
        }

        /// <summary>New observation recorded (I): a fresh fix may publish again.</summary>
        public static void OnNewObservation()
        {
            Suppressed = false;
        }

        private void ClearByEraser()
        {
            HasFix = false;
            HasTriangle = false;
            FixLabel = null;
            Suppressed = true;
            Debug.Log(LogPrefix + " fix marker erased by map eraser tool");
        }

        // Marker 0 = the fix pin, 1..3 = the cocked-hat vertices (P6).
        protected override int MarkerCount => 4;

        protected override string MarkerRootName(int index) =>
            index == 0 ? "RadioNavFixIcon" : "RadioNavFixVertex" + index;

        protected override string HoverText(int index) =>
            index == 0
                ? (HasFix ? FixLabel : null)
                : (HasFix && HasTriangle ? VertexLabel(index - 1) : null);

        protected override string PinnedText(int index) => HoverText(index);

        protected override bool HasContent(int index) =>
            index == 0 ? HasFix : HasFix && HasTriangle;

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
            string pair = vertex == 0 ? "B+C" : (vertex == 1 ? "C+A" : "A+B");
            return "LOP " + pair
                + "\nlat " + SextantDmFormat.FormatLatLonAscii(VertexLat[vertex], true)
                + " lon " + SextantDmFormat.FormatLatLonAscii(VertexLon[vertex], false);
        }

        protected override void OnHoverEnter(int index)
        {
            hovered = index == 0;
        }

        protected override void OnHoverExit(int index)
        {
            if (index == 0)
                hovered = false;
        }

        protected override void OnMarkerPositioned(int index, Vector3 worldPosition,
            float scale, float iconPx)
        {
            if (index != 0)
                return; // the eraser acts on the fix pin only
            // Eraser parity with MapChartingUI.UpdateEraserTool: LMB held
            // over the marker while the eraser tool is active removes it.
            if (!hovered || Suppressed || !Input.GetMouseButton(0))
                return;
            if (charting == null)
                charting = FindObjectOfType<MapChartingUI>();
            if (charting != null && charting.CurrentTool == MapChartingUI.Tool.Eraser)
                ClearByEraser();
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
