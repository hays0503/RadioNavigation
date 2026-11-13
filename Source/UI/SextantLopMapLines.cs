using System;
using System.Collections.Generic;
using TMPro;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Native chart lines and endpoints. Replaced only on I/O;
    /// loaded rows are adopted without changing their saved positions.</summary>
    internal sealed class SextantLopMapLines : ISextantLopChart
    {
        private MapChartingUI charting;
        private MapChartingDatabase database;
        private readonly List<MapPointUI> endpoints = new List<MapPointUI>(12);

        private bool Resolve()
        {
            if (charting == null)
                charting = Object.FindObjectOfType<MapChartingUI>(true);
            database = MapSaveSanitizer.ResolveDatabase();
            return charting != null && database != null;
        }

        public bool TryReplace(SextantLopSegment[] lines)
        {
            try
            {
                if (!Resolve())
                    return false;
                RemoveOwnGeometry();
                endpoints.Clear();
                foreach (SextantLopSegment segment in lines)
                    Create(segment);
                return true;
            }
            catch (Exception error)
            {
                Warn(error);
                return false; // optional chart output must not cancel a committed sight
            }
        }

        public bool TryAdopt()
        {
            try
            {
                if (!Resolve())
                    return false;
                endpoints.Clear();
                foreach (MapPointUI point in Object.FindObjectsOfType<MapPointUI>(true))
                {
                    if (point == null || point.Data == null || !SextantLopPlot.OwnsEndpoint(point.Data.Name))
                        continue;
                    endpoints.Add(point);
                    StylePoint(point, false);
                }
                foreach (MapLineUI line in Object.FindObjectsOfType<MapLineUI>(true))
                {
                    if (IsOwnLine(line))
                        StyleLine(line, IsTransferred(line.Data.PointA.Name));
                }
                return true;
            }
            catch (Exception error)
            {
                Warn(error);
                return false;
            }
        }

        internal void UpdateInteraction()
        {
            if (charting == null)
                return;
            bool interactive = charting.CurrentTool != MapChartingUI.Tool.Points;
            foreach (MapPointUI point in endpoints)
            {
                if (point != null)
                    point.Interactive = interactive; // keep reserved Data.Name out of the rename popup
            }
        }

        private void Create(SextantLopSegment segment)
        {
            MapPointUI a = charting.CreatePoint(Coordinates.ToWorldPosition((float)segment.StartLat, (float)segment.StartLon),
                SextantLopPlot.EndpointKey(segment.Slot, segment.Transferred, false));
            MapPointUI b = charting.CreatePoint(Coordinates.ToWorldPosition((float)segment.EndLat, (float)segment.EndLon),
                SextantLopPlot.EndpointKey(segment.Slot, segment.Transferred, true));
            endpoints.Add(a);
            endpoints.Add(b);
            StylePoint(a, segment.Coincident);
            StylePoint(b, segment.Coincident);
            MapLineUI line = charting.CreateLine(a, b);
            StyleLine(line, segment.Transferred);
        }

        private static bool IsTransferred(string name)
        {
            int slot; bool moved, end;
            return SextantLopPlot.TryKey(name, out slot, out moved, out end) && moved;
        }

        private static Color LineColor(bool transferred)
        {
            return transferred ? new Color(0.9f, 0.32f, 0.08f, 0.95f) : new Color(0.12f, 0.36f, 0.8f, 0.8f);
        }

        private static void StylePoint(MapPointUI point, bool coincident)
        {
            int slot; bool moved, end;
            if (!SextantLopPlot.TryKey(point.Data.Name, out slot, out moved, out end))
                return;
            Image image = point.GetComponent<Image>();
            if (image != null)
            {
                SextantLopMapMaterial.Apply(image);
                image.color = LineColor(moved);
            }
            TextMeshProUGUI label = point.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
                return;
            if (label.font != null)
                label.fontSharedMaterial = label.font.material;
            string kind = moved ? "T" : "O";
            if (coincident)
                kind = "O=T";
            label.text = "LOP " + slot + " " + kind;
            label.color = LineColor(moved);
            label.gameObject.SetActive(!end);
        }

        private static void StyleLine(MapLineUI line, bool transferred)
        {
            if (line == null)
                return;
            RawImage image = line.GetComponentInChildren<RawImage>(true);
            if (image != null)
            {
                // Preserve UI tint/dash alpha without testing the map plane's
                // depth. Never modify the shared default material.
                SextantLopMapMaterial.Apply(image);
                image.color = LineColor(transferred);
            }
        }

        private static bool IsOwnLine(MapLineUI line)
        {
            return line != null && IsOwnLineData(line.Data);
        }

        private static bool IsOwnLineData(MapLineData line)
        {
            return line?.PointA != null && line.PointB != null
                && SextantLopPlot.OwnsLine(line.PointA.Name, line.PointB.Name);
        }

        private void RemoveOwnGeometry()
        {
            // Remove database rows BEFORE deferred Unity destruction: a same-frame
            // save must not resurrect the previous series.
            var removedLines = new HashSet<MapLineData>();
            for (int i = database.Lines.Count - 1; i >= 0; i--)
            {
                MapLineData line = database.Lines[i];
                if (!IsOwnLineData(line))
                    continue;
                removedLines.Add(line);
                database.RemoveLine(line);
            }
            foreach (MapLineUI line in Object.FindObjectsOfType<MapLineUI>(true))
            {
                if (line != null && removedLines.Contains(line.Data))
                    Object.Destroy(line.gameObject);
            }
            HashSet<MapPointData> detachedPoints;
            var removedPoints = RemoveEndpointRows(out detachedPoints);
            foreach (MapPointUI point in Object.FindObjectsOfType<MapPointUI>(true))
            {
                if (point == null || point.Data == null)
                    continue;
                if (removedPoints.Contains(point.Data))
                    Object.Destroy(point.gameObject);
                else if (detachedPoints.Contains(point.Data))
                {
                    point.Name = "LOP anchor";
                    point.Interactive = true;
                }
            }
        }

        private HashSet<MapPointData> RemoveEndpointRows(out HashSet<MapPointData> detached)
        {
            var removed = new HashSet<MapPointData>();
            detached = new HashSet<MapPointData>();
            var referenced = ReferencedByPlayerShapes();
            for (int i = database.Points.Count - 1; i >= 0; i--)
            {
                MapPointData point = database.Points[i];
                if (point == null || !SextantLopPlot.OwnsEndpoint(point.Name))
                    continue;
                if (referenced.Contains(point))
                {
                    point.Name = "LOP anchor"; // detach ownership; keep the player's shape and Data refs
                    detached.Add(point);
                }
                else
                {
                    database.RemovePoint(point);
                    removed.Add(point);
                }
            }
            return removed;
        }

        private HashSet<MapPointData> ReferencedByPlayerShapes()
        {
            var result = new HashSet<MapPointData>();
            foreach (MapLineData line in database.Lines)
            {
                if (line == null) continue;
                result.Add(line.PointA); result.Add(line.PointB);
            }
            foreach (MapCircleData circle in database.Circles)
            {
                if (circle == null) continue;
                result.Add(circle.PointA); result.Add(circle.PointB);
            }
            foreach (MapAngleData angle in database.Angles)
            {
                if (angle == null) continue;
                result.Add(angle.PointA); result.Add(angle.PointB); result.Add(angle.PointC);
            }
            foreach (MapBearingUI bearing in Object.FindObjectsOfType<MapBearingUI>(true))
            {
                if (bearing == null || bearing.Data == null) continue;
                result.Add(bearing.Data.PointA); result.Add(bearing.Data.PointB);
            }
            return result;
        }

        private static void Warn(Exception error)
        {
            if (WarnOnce.ShouldLog("SextantLopMapLines.output"))
                Debug.LogWarning("[RadioNav][Map] LOP plot failed; sight retained: " + error);
        }
    }

    /// <summary>One material per owned graphic, released with that graphic.
    /// Native map lines use ZTest Always; UI/Default normally inherits the
    /// world canvas's LEqual and can intersect the map plane at world Y=0.</summary>
    internal sealed class SextantLopMapMaterial : MonoBehaviour
    {
        private Material ownedMaterial;

        internal static void Apply(Graphic graphic)
        {
            SextantLopMapMaterial owner = graphic.GetComponent<SextantLopMapMaterial>()
                ?? graphic.gameObject.AddComponent<SextantLopMapMaterial>();
            owner.Bind(graphic);
        }

        private void Bind(Graphic graphic)
        {
            if (ownedMaterial == null)
            {
                ownedMaterial = new Material(Graphic.defaultGraphicMaterial);
                ownedMaterial.name = "RadioNav LOP UI";
                ownedMaterial.hideFlags = HideFlags.DontSave;
                ownedMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            graphic.material = ownedMaterial;
        }

        public void OnDestroy()
        {
            if (ownedMaterial != null)
                Destroy(ownedMaterial);
        }
    }
}
