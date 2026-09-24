using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UBOAT.Game.Sandbox;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Map charting probe for the MCP bridge (2026-11-05): the bridge's own
    /// assembly copy can bind the game types to a stale assembly copy on this
    /// build (the double-load trap), while the mod's live copy is proven to
    /// work — so the map read/edit verbs live here and the bridge forwards to
    /// this class through reflection (McpReflect prefers the live RadioNavigation
    /// assembly for the UBOAT.Mods.RadioNavigation.* namespace).
    ///
    /// All game access goes through the public API:
    ///   MapChartingUI.CreatePoint / CreateLine / CurrentTool,
    ///   MapPointUI.Position / Name / Data / Interactive (raycastTarget),
    ///   UBOAT.Game.Sandbox.Coordinates lat/lon conversion.
    /// The mod's markers are real MapPointUI points, so the same verbs drive
    /// them: move a static one and the reconcile restores it next frame;
    /// delete one and its Destroyed handler runs exactly as for the map eraser.
    /// The only reflection is the best-effort hover precondition
    /// (MapPointUI.hoveredPoint is a private static).
    /// </summary>
    public static class MapProbe
    {
        private const int MaxPoints = 400;
        private const string OwnedPrefix = "RadioNav ";

        private static readonly FieldInfo HoveredPointField = typeof(MapPointUI).GetField(
            "hoveredPoint", BindingFlags.NonPublic | BindingFlags.Static);

        public static string Snapshot()
        {
            MapChartingUI charting = FindCharting();
            if (charting == null)
                return "{\"ok\":false,\"error\":\"no-map\"}";

            StringBuilder sb = new StringBuilder(4096);
            sb.Append("{\"ok\":true");
            sb.Append(",\"tool\":\"").Append(RadioNavProbeJson.Escape(
                charting.CurrentTool.ToString())).Append('"');

            List<MapPointUI> points = CollectPoints();
            int emitted = 0;
            sb.Append(",\"points\":[");
            for (int i = 0; i < points.Count && emitted < MaxPoints; i++)
            {
                MapPointUI point = points[i];
                if (point == null || point.Data == null)
                    continue;
                if (emitted > 0)
                    sb.Append(',');
                AppendPoint(sb, point);
                emitted++;
            }
            sb.Append(']');
            sb.Append(",\"pointCount\":").Append(points.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"truncated\":").Append(points.Count > emitted ? "true" : "false");

            MapLineUI[] lines = UnityEngine.Object.FindObjectsOfType<MapLineUI>(true);
            sb.Append(",\"lines\":[");
            int lineEmitted = 0;
            for (int i = 0; i < lines.Length && lineEmitted < MaxPoints; i++)
            {
                MapLineUI line = lines[i];
                if (line == null || line.Data == null)
                    continue;
                if (lineEmitted > 0)
                    sb.Append(',');
                sb.Append("{\"from\":\"").Append(RadioNavProbeJson.Escape(PointName(line.StartPoint)))
                  .Append("\",\"to\":\"").Append(RadioNavProbeJson.Escape(PointName(line.EndPoint)))
                  .Append("\"}");
                lineEmitted++;
            }
            sb.Append(']');
            sb.Append(",\"lineCount\":").Append(lines.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"circleCount\":")
              .Append(UnityEngine.Object.FindObjectsOfType<MapCircleUI>(true).Length
                  .ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"angleCount\":")
              .Append(UnityEngine.Object.FindObjectsOfType<MapAngleUI>(true).Length
                  .ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        public static string CreatePoint(string latRaw, string lonRaw, string xRaw, string yRaw,
            string name)
        {
            MapChartingUI charting = FindCharting();
            if (charting == null)
                return Error("no-map");

            Vector2 position;
            if (!TryPosition(latRaw, lonRaw, xRaw, yRaw, out position))
                return Error("bad-position");

            if (string.IsNullOrEmpty(name))
                name = "MCP Point";
            MapPointUI created = charting.CreatePoint(position, name);
            if (created == null)
                return Error("create-failed");

            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":true,\"name\":\"").Append(RadioNavProbeJson.Escape(name)).Append('"');
            AppendPositionFields(sb, position);
            sb.Append('}');
            return sb.ToString();
        }

        public static string MovePoint(string name, string latRaw, string lonRaw,
            string xRaw, string yRaw)
        {
            if (string.IsNullOrEmpty(name))
                return Error("name-required");
            Vector2 position;
            if (!TryPosition(latRaw, lonRaw, xRaw, yRaw, out position))
                return Error("bad-position");

            MapPointUI point;
            string error = FindPoint(name, out point);
            if (error != null)
                return error;

            Vector2 old = point.Position;
            point.Position = position;

            StringBuilder sb = new StringBuilder(320);
            sb.Append("{\"ok\":true,\"name\":\"").Append(RadioNavProbeJson.Escape(name)).Append('"');
            sb.Append(",\"old\":{");
            AppendPositionFields(sb, old);
            sb.Append("},\"new\":{");
            AppendPositionFields(sb, position);
            sb.Append("}}");
            return sb.ToString();
        }

        public static string DeletePoint(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Error("name-required");
            MapPointUI point;
            string error = FindPoint(name, out point);
            if (error != null)
                return error;

            // Same path as the map eraser and the Delete key: Destroy fires
            // MapPointUI.Destroyed, which the mod's markers subscribe to.
            UnityEngine.Object.Destroy(point.gameObject);
            return "{\"ok\":true,\"deleted\":true,\"name\":\"" +
                RadioNavProbeJson.Escape(name) + "\"}";
        }

        /// <summary>Deletes only the mod-owned points ("RadioNav …"). The mod
        /// reconciles them back (except markers it suppresses after a manual
        /// erase), so this is the test-artifact cleanup path.</summary>
        public static string ClearOwned()
        {
            List<MapPointUI> points = CollectPoints();
            int deleted = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MapPointUI point = points[i];
                if (point == null || point.Data == null)
                    continue;
                if (!IsOwned(point.Data.Name))
                    continue;
                UnityEngine.Object.Destroy(point.gameObject);
                deleted++;
            }

            return "{\"ok\":true,\"deleted\":" + deleted.ToString(CultureInfo.InvariantCulture) +
                ",\"note\":\"the mod reconciles its markers back (suppressed ones stay hidden)\"}";
        }

        public static string CreateLine(string from, string to)
        {
            MapChartingUI charting = FindCharting();
            if (charting == null)
                return Error("no-map");
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return Error("name-required");

            MapPointUI start;
            string error = FindPoint(from, out start);
            if (error != null)
                return error;
            MapPointUI end;
            error = FindPoint(to, out end);
            if (error != null)
                return error;

            MapLineUI line = charting.CreateLine(start, end);
            if (line == null)
                return Error("create-failed");
            return "{\"ok\":true,\"from\":\"" + RadioNavProbeJson.Escape(from) +
                "\",\"to\":\"" + RadioNavProbeJson.Escape(to) + "\"}";
        }

        public static string SetTool(string toolRaw)
        {
            MapChartingUI charting = FindCharting();
            if (charting == null)
                return Error("no-map");

            MapChartingUI.Tool tool;
            if (!TryTool(toolRaw, out tool))
                return Error("bad-tool");

            MapChartingUI.Tool old = charting.CurrentTool;
            charting.CurrentTool = tool;
            return "{\"ok\":true,\"old\":\"" + RadioNavProbeJson.Escape(old.ToString()) +
                "\",\"new\":\"" + RadioNavProbeJson.Escape(charting.CurrentTool.ToString()) + "\"}";
        }

        /// <summary>Sets (or clears, with an empty name) the hovered map point
        /// so the charting tools see an anchor without a real pointer. The
        /// private static may be renamed by obfuscation — reported, not fatal.</summary>
        public static string Hover(string name)
        {
            if (HoveredPointField == null)
                return Error("hover-unavailable");

            if (string.IsNullOrEmpty(name))
            {
                HoveredPointField.SetValue(null, null);
                return "{\"ok\":true,\"hovered\":null}";
            }

            MapPointUI point;
            string error = FindPoint(name, out point);
            if (error != null)
                return error;
            HoveredPointField.SetValue(null, point);
            return "{\"ok\":true,\"hovered\":\"" + RadioNavProbeJson.Escape(name) + "\"}";
        }

        // ---- helpers ---------------------------------------------------------

        private static MapChartingUI FindCharting()
        {
            // includeInactive: the charting UI's panel is inactive while the
            // map is closed, but CreatePoint/CurrentTool work regardless.
            return UnityEngine.Object.FindObjectOfType<MapChartingUI>(true);
        }

        /// <summary>All charting points as a union of the scene scan and the
        /// charting UI's own registry (the latter covers the declutter-hidden
        /// points on builds where the scan sees only the active ones).
        /// Deduplicated by instance id.</summary>
        private static List<MapPointUI> CollectPoints()
        {
            var result = new List<MapPointUI>();
            var seen = new HashSet<int>();

            MapPointUI[] all = UnityEngine.Object.FindObjectsOfType<MapPointUI>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MapPointUI point = all[i];
                if (point != null && seen.Add(point.GetInstanceID()))
                    result.Add(point);
            }

            MapChartingUI charting = FindCharting();
            if (charting != null)
            {
                FieldInfo[] fields = typeof(MapChartingUI).GetFields(
                    BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (!field.FieldType.IsGenericType)
                        continue;
                    Type[] args = field.FieldType.GetGenericArguments();
                    if (args.Length != 1 || args[0].Name != "MapPointUI")
                        continue;
                    if (!(field.GetValue(charting) is System.Collections.IEnumerable list))
                        continue;
                    foreach (object item in list)
                    {
                        MapPointUI point = item as MapPointUI;
                        if (point != null && seen.Add(point.GetInstanceID()))
                            result.Add(point);
                    }
                }
            }
            return result;
        }

        private static string FindPoint(string name, out MapPointUI point)
        {
            point = null;
            List<MapPointUI> points = CollectPoints();
            int matches = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MapPointUI candidate = points[i];
                if (candidate == null || candidate.Data == null)
                    continue;
                if (!string.Equals(candidate.Data.Name, name, StringComparison.Ordinal))
                    continue;
                if (point == null)
                    point = candidate;
                matches++;
            }

            if (point == null)
                return Error("point-not-found");
            if (matches > 1)
                return "{\"ok\":false,\"error\":\"ambiguous-name\",\"matches\":" +
                    matches.ToString(CultureInfo.InvariantCulture) + "}";
            return null;
        }

        private static bool TryPosition(string latRaw, string lonRaw, string xRaw, string yRaw,
            out Vector2 position)
        {
            position = Vector2.zero;
            float lat;
            float lon;
            if (TryFloat(latRaw, out lat) && TryFloat(lonRaw, out lon))
            {
                if (lat < -90f || lat > 90f)
                    return false;
                position = Coordinates.ToWorldPosition(lat, lon);
                return true;
            }

            float x;
            float y;
            if (TryFloat(xRaw, out x) && TryFloat(yRaw, out y))
            {
                position = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private static bool TryFloat(string raw, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(raw))
                return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryTool(string raw, out MapChartingUI.Tool tool)
        {
            tool = MapChartingUI.Tool.Route;
            if (string.IsNullOrEmpty(raw))
                return false;
            int index;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                if (index < 0 || index > (int)MapChartingUI.Tool.Bearing)
                    return false;
                tool = (MapChartingUI.Tool)index;
                return true;
            }

            foreach (MapChartingUI.Tool candidate in Enum.GetValues(typeof(MapChartingUI.Tool)))
            {
                if (string.Equals(candidate.ToString(), raw, StringComparison.OrdinalIgnoreCase))
                {
                    tool = candidate;
                    return true;
                }
            }
            return false;
        }

        private static void AppendPoint(StringBuilder sb, MapPointUI point)
        {
            string name = point.Data != null ? point.Data.Name : null;
            sb.Append("{\"name\":\"").Append(RadioNavProbeJson.Escape(name)).Append('"');
            sb.Append(",\"owned\":").Append(IsOwned(name) ? "true" : "false");
            sb.Append(",\"kind\":\"").Append(RadioNavProbeJson.Escape(KindOf(name))).Append('"');
            AppendPositionFields(sb, point.Position);
            sb.Append(",\"active\":").Append(point.gameObject.activeSelf ? "true" : "false");
            sb.Append(",\"interactive\":").Append(point.Interactive ? "true" : "false");
            Vector3 world = point.transform.position;
            sb.Append(",\"world\":[")
              .Append(world.x.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(world.y.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(world.z.ToString("F1", CultureInfo.InvariantCulture)).Append(']');
            sb.Append('}');
        }

        private static void AppendPositionFields(StringBuilder sb, Vector2 position)
        {
            Vector2 latLon = Coordinates.ToLatLong(position);
            sb.Append(",\"lat\":").Append(latLon.x.ToString("F5", CultureInfo.InvariantCulture));
            sb.Append(",\"lon\":").Append(latLon.y.ToString("F5", CultureInfo.InvariantCulture));
            sb.Append(",\"x\":").Append(position.x.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append(",\"y\":").Append(position.y.ToString("F1", CultureInfo.InvariantCulture));
        }

        private static string PointName(MapPointUI point)
        {
            return point?.Data?.Name;
        }

        private static bool IsOwned(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.StartsWith(OwnedPrefix, StringComparison.Ordinal);
        }

        /// <summary>Marker kind parsed from the mod's adoption key.</summary>
        private static string KindOf(string name)
        {
            if (name == null || !IsOwned(name))
                return "foreign";
            if (name.StartsWith(OwnedPrefix + "FIX", StringComparison.Ordinal))
                return "fix";
            if (name.StartsWith(OwnedPrefix + "LOP", StringComparison.Ordinal))
                return "lop";
            if (name.StartsWith(OwnedPrefix + "LH", StringComparison.Ordinal))
                return "lighthouse";
            if (name.StartsWith(OwnedPrefix + "STN", StringComparison.Ordinal))
                return "station";
            return "owned";
        }

        private static string Error(string code)
        {
            return "{\"ok\":false,\"error\":\"" + code + "\"}";
        }
    }
}
