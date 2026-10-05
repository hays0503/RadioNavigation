using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Projects only the four EXACT active-series keys into the
    /// native charting database. Foreign/anonymous points are never owned by
    /// inference. Valid points and their shapes retain the original Data refs.</summary>
    internal static class SextantMapSave
    {
        private static readonly FieldInfo BearingsField = typeof(MapChartingDatabase).GetField(
            "bearingCircles", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static SextantSaveSnapshot RestoreLegacyNamedMarkers()
        {
            var state = new SextantSaveSnapshot();
            MapChartingDatabase db = MapSaveSanitizer.ResolveDatabase();
            if (db == null)
                return state;
            int found = 0;
            foreach (MapPointData point in db.Points)
            {
                if (point == null)
                    continue;
                int index = FixMarkerSnapshot.IndexOfKey(point.Name);
                if (index < 0)
                    continue;
                Vector2 geo = Coordinates.ToLatLong(point.Position);
                if (!GeoValid(geo))
                    continue;
                found |= 1 << index;
                SetLegacyPosition(state.Markers, index, geo);
            }
            // Partial old geometry (e.g. a dismissed pin with two surviving
            // vertices) must not be culled or completed by invented points.
            state.Markers.HasFix = found != 0;
            state.Markers.HasTriangle = (found & 14) != 0;
            state.Markers.SuppressedMask = found != 0 ? (~found & 15) : 0;
            state.Markers.Label = "FIX (legacy save; observations unavailable)";
            return state;
        }

        private static bool GeoValid(Vector2 geo)
        {
            return !float.IsNaN(geo.x) && !float.IsNaN(geo.y)
                && Mathf.Abs(geo.x) <= 90 && Mathf.Abs(geo.y) <= 180;
        }

        private static void SetLegacyPosition(FixMarkerSnapshot state, int index, Vector2 geo)
        {
            if (index == 0)
            {
                state.Lat = geo.x; state.Lon = geo.y;
            }
            else
            {
                state.VertexLat[index - 1] = geo.x; state.VertexLon[index - 1] = geo.y;
            }
        }

        internal static void TrimInactiveMarkers(FixMarkerSnapshot state)
        {
            MapChartingDatabase db = MapSaveSanitizer.ResolveDatabase();
            if (db == null)
                return;
            var removed = new HashSet<MapPointData>();
            for (int i = db.Points.Count - 1; i >= 0; i--)
            {
                MapPointData point = db.Points[i];
                if (point == null)
                    continue;
                int index = FixMarkerSnapshot.IndexOfKey(point.Name);
                if (index < 0 || state.IsVisible(index))
                    continue;
                RemoveAnchoredShapes(db, point);
                db.RemovePoint(point);
                removed.Add(point);
            }
            if (removed.Count == 0)
                return;
            MapPointUI[] scenePoints = UnityEngine.Object.FindObjectsOfType<MapPointUI>(true);
            for (int i = 0; i < scenePoints.Length; i++)
            {
                MapPointUI point = scenePoints[i];
                if (point != null && removed.Contains(point.Data))
                    UnityEngine.Object.Destroy(point.gameObject);
            }
        }

        private static void RemoveAnchoredShapes(MapChartingDatabase db, MapPointData point)
        {
            RemoveMatching(db.Lines,
                l => l != null && (ReferenceEquals(l.PointA, point) || ReferenceEquals(l.PointB, point)), db.RemoveLine);
            RemoveMatching(db.Circles,
                c => c != null && (ReferenceEquals(c.PointA, point) || ReferenceEquals(c.PointB, point)), db.RemoveCircle);
            RemoveMatching(db.Angles,
                a => a != null && (ReferenceEquals(a.PointA, point) || ReferenceEquals(a.PointB, point)
                    || ReferenceEquals(a.PointC, point)), db.RemoveAngle);
            var bearings = BearingsField?.GetValue(db) as IList<MapBearingCircleData>;
            if (bearings != null)
                RemoveMatching(bearings,
                    b => b != null && (ReferenceEquals(b.PointA, point) || ReferenceEquals(b.PointB, point)),
                    db.RemoveBearingCircle);
        }

        private static void RemoveMatching<T>(IList<T> items, Func<T, bool> predicate, Action<T> remove)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (predicate(items[i]))
                    remove(items[i]);
            }
        }
    }
}
