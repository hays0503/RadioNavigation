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

        /// <summary>Series reset (O): drop the pin and allow publishing.</summary>
        public static void Clear()
        {
            HasFix = false;
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
            FixLabel = null;
            Suppressed = true;
            Debug.Log(LogPrefix + " fix marker erased by map eraser tool");
        }

        protected override int MarkerCount => 1;

        protected override string MarkerRootName(int index) => "RadioNavFixIcon";

        protected override string HoverText(int index) => HasFix ? FixLabel : null;

        protected override string PinnedText(int index) => HasFix ? FixLabel : null;

        protected override bool HasContent(int index) => HasFix;

        protected override Vector2 GetEncounterPosition(int index) =>
            Coordinates.ToWorldPosition((float)FixLat, (float)FixLon);

        protected override void OnHoverEnter(int index)
        {
            hovered = true;
        }

        protected override void OnHoverExit(int index)
        {
            hovered = false;
        }

        protected override void OnMarkerPositioned(int index, Vector3 worldPosition,
            float scale, float iconPx)
        {
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
