using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Map;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    public static class LopMapProbe
    {
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private sealed class Fixture
        {
            internal readonly MapChartingDatabase Db;
            internal readonly MapChartingUI Native;
            internal readonly SextantLopMapLines Adapter = new SextantLopMapLines();
            internal readonly SextantLopSegment[] Segments = { new SextantLopSegment { Slot = 1 }, new SextantLopSegment { Slot = 2 } };
            internal Fixture()
            {
                World.Reset();
                Graphic.defaultGraphicMaterial = new Material();
                Graphic.defaultGraphicMaterial.SetInt("unity_GUIZTestMode", 4);
                Db = MapSaveSanitizer.Database = new MapChartingDatabase();
                Native = new GameObject("chart").AddComponent<MapChartingUI>();
            }
            internal MapPointUI Point(int slot, bool end)
            {
                return Object.FindObjectsOfType<MapPointUI>(true).Single(p => p.Data.Name == SextantLopPlot.EndpointKey(slot, false, end));
            }
            internal void Clear() { Check(Adapter.TryReplace(new SextantLopSegment[0]), "Clear failed"); }
        }

        public static void PlayerLineAndDeferredDestroy()
        {
            var f = new Fixture();
            Check(f.Adapter.TryReplace(f.Segments), "Initial creation failed");
            var anchor = f.Point(1, false);
            var oldData = anchor.Data;
            var unused = f.Point(1, true);
            var foreign = f.Native.CreatePoint(new Vector2(3, 4), "RadioNav SIGHTLINE O1 A custom");
            var playerLine = f.Native.CreateLine(anchor, foreign);
            MapLineData playerData = playerLine.Data;
            World.Events.Clear();
            f.Clear();
            Check(f.Db.Lines.Count == 1 && f.Db.Lines[0] == playerData, "Player line row lost or owned line retained");
            Check(f.Db.Points.Count == 2 && f.Db.Points.Contains(oldData) && f.Db.Points.Contains(foreign.Data), "Wrong endpoint rows removed");
            Check(anchor.Data == oldData && playerData.PointA == oldData && anchor.Name == "LOP anchor", "Anchor/reference not preserved");
            Check(anchor.Interactive && anchor.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text == "LOP anchor", "Detached endpoint UI not synchronized");
            Check(!anchor.gameObject.Queued && !foreign.gameObject.Queued && !playerLine.gameObject.Queued, "Player geometry queued for destruction");
            Check(unused.gameObject.Queued && !unused.gameObject.Destroyed, "Destroy is not deferred");
            Check(World.Events[0] == "remove-line" && World.Events.FindIndex(e => e.StartsWith("remove-point:"))
                < World.Events.FindIndex(e => e == "destroy:" + unused.gameObject.name), "Rows not removed before Destroy");
            World.FlushDestroy();
            Check(!playerLine.Destroyed && !anchor.Destroyed && unused.Destroyed, "Deferred cleanup destroys player geometry");
            f.Clear(); World.FlushDestroy();
            Check(f.Db.Lines.Single() == playerData && playerData.PointA == oldData, "Repeated cleanup changed player references");
        }

        public static void OtherPlayerShapes()
        {
            var f = new Fixture();
            Check(f.Adapter.TryReplace(f.Segments), "Initial creation failed");
            var a = f.Point(1, false); var b = f.Point(1, true);
            var c = f.Point(2, false); var unused = f.Point(2, true);
            var foreign = f.Native.CreatePoint(new Vector2(8, 9), "user");
            var circle = new MapCircleData { PointA = a.Data, PointB = foreign.Data };
            var angle = new MapAngleData { PointA = foreign.Data, PointB = b.Data, PointC = a.Data };
            var bearing = new GameObject("bearing").AddComponent<MapBearingUI>();
            bearing.Data = new MapLineData { PointA = c.Data, PointB = foreign.Data };
            f.Db.Circles.Add(circle); f.Db.Angles.Add(angle);
            MapPointData ad = a.Data, bd = b.Data, cd = c.Data;
            f.Clear(); World.FlushDestroy();
            Check(f.Db.Lines.Count == 0 && f.Db.Circles.Single() == circle && f.Db.Angles.Single() == angle, "Foreign shape rows changed");
            Check(circle.PointA == ad && angle.PointB == bd && angle.PointC == ad && bearing.Data.PointA == cd, "Shape Data refs replaced");
            Check(new[] { a, b, c }.All(p => !p.Destroyed && p.Name == "LOP anchor"), "Referenced endpoint was destroyed");
            Check(unused.Destroyed && f.Db.Points.Count == 4 && !bearing.Destroyed, "Wrong unreferenced cleanup");
            Check(Object.FindObjectsOfType<MapLineUI>(true).Length == 0, "Owned UI line survived between retained anchors");
        }

        public static void RetryPointCreation()
        {
            var f = new Fixture();
            f.Native.FailPointAt = 2;
            Check(!f.Adapter.TryReplace(f.Segments) && f.Db.Points.Count == 1, "Failure was not injected after first endpoint");
            var orphan = Object.FindObjectsOfType<MapPointUI>(true).Single();
            f.Native.FailPointAt = -1;
            Check(f.Adapter.TryReplace(f.Segments), "Retry failed");
            Check(!f.Db.Points.Contains(orphan.Data) && orphan.gameObject.Queued, "Orphan was not removed before retry publication");
            World.FlushDestroy();
            Check(f.Db.Points.Count == 4 && f.Db.Lines.Count == 2, "Retry duplicated rows");
            Check(Object.FindObjectsOfType<MapPointUI>(true).Length == 4 && Object.FindObjectsOfType<MapLineUI>(true).Length == 2, "Retry left stale UI");
        }

        public static void RetryLineCreation()
        {
            var f = new Fixture();
            f.Native.FailAfterLineRegistration = true;
            Check(!f.Adapter.TryReplace(f.Segments) && f.Db.Lines.Count == 1, "Failure was not injected after line registration");
            var stale = Object.FindObjectsOfType<MapLineUI>(true).Single();
            f.Native.FailAfterLineRegistration = false;
            Check(f.Adapter.TryReplace(f.Segments), "Retry failed");
            World.FlushDestroy();
            Check(stale.Destroyed && f.Db.Points.Count == 4 && f.Db.Lines.Count == 2, "Retry retained old geometry");
            Check(Object.FindObjectsOfType<MapLineUI>(true).All(line => f.Db.Lines.Contains(line.Data)), "UI line references absent native row");
        }

        public static void MaterialLifetime()
        {
            var f = new Fixture();
            Check(f.Adapter.TryReplace(f.Segments), "Initial creation failed");
            var pointGraphic = f.Point(1, false).GetComponent<Image>();
            var lineGraphic = Object.FindObjectsOfType<MapLineUI>(true).First().GetComponent<RawImage>();
            foreach (Graphic graphic in new Graphic[] { pointGraphic, lineGraphic })
            {
                Material material = graphic.material;
                Check(material != Graphic.defaultGraphicMaterial && material.Source == Graphic.defaultGraphicMaterial, "Owned UI copy not assigned to graphic");
                Check(material.Values["unity_GUIZTestMode"] == 8 && material.hideFlags == HideFlags.DontSave, "Wrong depth/lifetime flags");
                Check(graphic.GetComponent<SextantLopMapMaterial>() != null, "Missing owner");
                SextantLopMapMaterial.Apply(graphic);
                Check(graphic.material == material && graphic.gameObject.Components.OfType<SextantLopMapMaterial>().Count() == 1, "Apply reallocates material/owner");
                Check(f.Adapter.TryAdopt() && graphic.material == material, "Adopt reallocates material");
            }
            Material pointMaterial = pointGraphic.material, lineMaterial = lineGraphic.material;
            f.Clear();
            Check(!pointMaterial.Destroyed && !lineMaterial.Destroyed, "Material destruction was not deferred with graphics");
            World.FlushDestroy();
            Check(pointMaterial.Destroyed && lineMaterial.Destroyed, "Owned materials leaked");
            Check(!Graphic.defaultGraphicMaterial.Destroyed && Graphic.defaultGraphicMaterial.Values["unity_GUIZTestMode"] == 4, "Shared default was mutated or destroyed");
        }
    }
}
