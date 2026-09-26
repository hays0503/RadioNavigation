using System;
using System.Reflection;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Eraser/Delete interception for the mod's static map markers (author
    /// 2026-09-25). Vanilla <c>MapChartingUI</c> destroys only the hovered
    /// point (<c>UpdateEraserTool</c> and the Delete branch), and a shape
    /// (line / angle / circle / bearing) dies only with one of its endpoints —
    /// so a line between two protected static markers was unremovable in game.
    ///
    /// This component runs in the <c>Update</c> phase, the game's eraser in
    /// <c>MapChartingUI.LateUpdate</c> (verified in the game source). When the
    /// eraser targets one of our markers it destroys the SHAPES anchored to
    /// that marker and clears <c>MapPointUI.hoveredPoint</c>, so the vanilla
    /// code sees nothing to destroy and the marker itself survives (the
    /// reconcile keeps it anyway). A marker without shapes is left untouched.
    ///
    /// Unity runs every <c>Update</c> before every <c>LateUpdate</c>, so the
    /// hover is cleared before the vanilla code can act; the
    /// <see cref="DefaultExecutionOrderAttribute"/> -1000 does NOT establish
    /// that (it only orders within a phase) — the PHASE does. Pinned by the
    /// SourceCheck gate <c>MapShapeEraser_RunsInUpdatePhase</c>.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class MapShapeEraser : MonoBehaviour
    {
        private static readonly FieldInfo HoveredPointField = typeof(MapPointUI).GetField(
            "hoveredPoint", BindingFlags.NonPublic | BindingFlags.Static);

        private MapChartingUI charting;

        public void Update()
        {
            HandleEraserInput();
        }

        /// <summary>Eraser held over one of our markers, or Delete pressed on
        /// it: remove the anchored shapes and hide the point from the vanilla
        /// destroy branch. The scene scans live here rather than in
        /// <see cref="Update"/> because they run only while the user is erasing
        /// (input-gated), not per frame — the same reason RN0006's one-hop blind
        /// spot (F237) does not apply to them. The PHASE is what makes the
        /// interception correct, so it is pinned by a gate, not by an analyzer.</summary>
        private void HandleEraserInput()
        {
            if (!IsEraserInput())
                return;
            MapPointUI hovered = MapPointUI.HoveredPoint;
            if (hovered == null || hovered.Data == null)
                return;
            if (!MapMarkerPoints.IsOwned(hovered.Data.Name))
                return;

            int removed = RemoveShapesFor(hovered.Data);
            ClearHoveredPoint();
            if (removed > 0)
            {
                Debug.Log("[RadioNav][Map] eraser removed " + removed +
                    " shape(s) anchored to '" + hovered.Data.Name + "' (marker kept)");
            }
        }

        private bool IsEraserInput()
        {
            if (charting == null)
                charting = FindObjectOfType<MapChartingUI>(true);
            if (charting == null)
                return false;

            if (Input.GetKeyDown(KeyCode.Delete))
                return true;
            return charting.CurrentTool == MapChartingUI.Tool.Eraser && Input.GetMouseButton(0);
        }

        /// <summary>Probe hook (MCP/reflect): removes the shapes anchored to
        /// the point with this exact name. Returns the removed count, or -1
        /// when the point was not found.</summary>
        public static int RemoveShapesForPointName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            MapPointUI[] points = FindObjectsOfType<MapPointUI>(true);
            for (int i = 0; i < points.Length; i++)
            {
                MapPointUI point = points[i];
                if (point == null || point.Data == null)
                    continue;
                if (string.Equals(point.Data.Name, name, StringComparison.Ordinal))
                    return RemoveShapesFor(point.Data);
            }
            return -1;
        }

        private static int RemoveShapesFor(MapPointData data)
        {
            return RemoveLineShapes(data)
                + RemoveAngleShapes(data)
                + RemoveCircleShapes(data)
                + RemoveBearingShapes(data);
        }

        private static int RemoveLineShapes(MapPointData data)
        {
            int removed = 0;
            MapLineUI[] lines = FindObjectsOfType<MapLineUI>(true);
            for (int i = 0; i < lines.Length; i++)
            {
                MapLineUI line = lines[i];
                if (line == null || line.Data == null)
                    continue;
                if (!References(line.Data.PointA, data) && !References(line.Data.PointB, data))
                    continue;
                Destroy(line.gameObject);
                removed++;
            }
            return removed;
        }

        private static int RemoveAngleShapes(MapPointData data)
        {
            int removed = 0;
            MapAngleUI[] angles = FindObjectsOfType<MapAngleUI>(true);
            for (int i = 0; i < angles.Length; i++)
            {
                MapAngleUI angle = angles[i];
                if (angle == null || angle.Data == null)
                    continue;
                bool anchored = References(angle.Data.PointA, data)
                    || References(angle.Data.PointB, data)
                    || References(angle.Data.PointC, data);
                if (!anchored)
                    continue;
                Destroy(angle.gameObject);
                removed++;
            }
            return removed;
        }

        private static int RemoveCircleShapes(MapPointData data)
        {
            int removed = 0;
            MapCircleUI[] circles = FindObjectsOfType<MapCircleUI>(true);
            for (int i = 0; i < circles.Length; i++)
            {
                MapCircleUI circle = circles[i];
                if (circle == null || circle.Data == null)
                    continue;
                if (!References(circle.Data.PointA, data) && !References(circle.Data.PointB, data))
                    continue;
                Destroy(circle.gameObject);
                removed++;
            }
            return removed;
        }

        private static int RemoveBearingShapes(MapPointData data)
        {
            int removed = 0;
            MapBearingUI[] bearings = FindObjectsOfType<MapBearingUI>(true);
            for (int i = 0; i < bearings.Length; i++)
            {
                MapBearingUI bearing = bearings[i];
                if (bearing == null || bearing.Data == null)
                    continue;
                if (!References(bearing.Data.PointA, data) && !References(bearing.Data.PointB, data))
                    continue;
                Destroy(bearing.gameObject);
                removed++;
            }
            return removed;
        }

        private static bool References(MapPointData candidate, MapPointData target)
        {
            return candidate != null && ReferenceEquals(candidate, target);
        }

        private static void ClearHoveredPoint()
        {
            if (HoveredPointField == null)
                return;
            HoveredPointField.SetValue(null, null);
        }
    }
}
