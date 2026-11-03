using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Local triangle picking; no scene colliders or physics layers.</summary>
    internal sealed class CPlathPointerGeometry : IDisposable
    {
        internal const int Nothing = -2, Micrometer = -1;
        const int ChunkTriangles = 128;
        sealed class Surface
        {
            internal Transform transform;
            internal Renderer renderer;
            internal SkinnedMeshRenderer skin;
            internal Mesh baked;
            internal Vector3[] vertices, screen;
            internal int[] triangles;
            internal Bounds[] chunks;
            internal int target;
            internal void RebuildBounds()
            {
                int count = (triangles.Length+ChunkTriangles*3-1)/(ChunkTriangles*3);
                if (chunks == null || chunks.Length != count) chunks = new Bounds[count];
                for (int c = 0; c < count; c++)
                {
                    int first = c*ChunkTriangles*3, end = Math.Min(first+ChunkTriangles*3, triangles.Length);
                    var bounds = new Bounds(vertices[triangles[first]], Vector3.zero);
                    for (int i = first+1; i < end; i++) bounds.Encapsulate(vertices[triangles[i]]);
                    bounds.Expand(Mathf.Max(bounds.size.magnitude*1e-6f, 1e-7f));
                    chunks[c] = bounds;
                }
            }
        }
        readonly List<Surface> surfaces = new List<Surface>();
        double lastReading = double.NaN;

        internal CPlathPointerGeometry(GameObject model)
        {
            if (!model) throw new ArgumentNullException(nameof(model));
            try
            {
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (!renderer || !filter.sharedMesh) continue;
                    var surface = new Surface { transform = filter.transform, renderer = renderer,
                        vertices = filter.sharedMesh.vertices, triangles = filter.sharedMesh.triangles,
                        target = Target(filter.transform, model.transform) };
                    surface.screen = new Vector3[surface.vertices.Length];
                    surface.RebuildBounds(); surfaces.Add(surface);
                }
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    surfaces.Add(new Surface { transform = skin.transform, renderer = skin, skin = skin,
                        baked = new Mesh(), triangles = skin.sharedMesh.triangles, target = Nothing });
            }
            catch { Dispose(); throw; }
        }

        static int Target(Transform node, Transform root)
        {
            for (Transform current = node; current != null && current != root; current = current.parent)
            {
                string name = current.name;
                if (name.EndsWith("_Pivot", StringComparison.Ordinal)) name = name.Substring(0, name.Length-6);
                for (int i = 1; i <= 4; i++)
                    if (name == "Index_Shade_"+i.ToString("00", CultureInfo.InvariantCulture)) return i-1;
                for (int i = 1; i <= 3; i++)
                    if (name == "Horizon_Shade_"+i.ToString("00", CultureInfo.InvariantCulture)) return i+3;
                if (name.StartsWith("Micrometer_", StringComparison.Ordinal)) return Micrometer;
            }
            return node.name == "Scale_Reading_Index" ? Micrometer : Nothing;
        }

        // ReadHold is static except for arc-dependent arm IK. Camera registration
        // is rigid and cancels in skin-local space, so idle hover need not rebake.
        internal void RefreshSkins(double reading)
        {
            if (lastReading == reading) return;
            foreach (var surface in surfaces)
            {
                if (!surface.skin) continue;
                surface.skin.BakeMesh(surface.baked, false);
                surface.vertices = surface.baked.vertices;
                surface.RebuildBounds();
            }
            lastReading = reading;
        }

        static bool Visible(Surface surface, Camera camera)
        {
            return surface.renderer && surface.renderer.enabled && surface.renderer.gameObject.activeInHierarchy &&
                (camera.cullingMask & (1 << surface.renderer.gameObject.layer)) != 0;
        }

        Surface Cast(Camera camera, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity; Surface closest = null;
            foreach (var surface in surfaces)
            {
                if (!Visible(surface, camera) || surface.vertices == null || surface.chunks == null) continue;
                Matrix4x4 inverse = surface.transform.worldToLocalMatrix;
                Vector3 direction = inverse.MultiplyVector(ray.direction);
                float scale = direction.magnitude;
                if (scale < 1e-12f) continue;
                direction /= scale;
                Vector3 origin = inverse.MultiplyPoint3x4(ray.origin);
                var localRay = new Ray(origin, direction);
                for (int c = 0; c < surface.chunks.Length; c++)
                {
                    float entry;
                    if (!surface.chunks[c].IntersectRay(localRay, out entry) || entry/scale > distance) continue;
                    int first = c*ChunkTriangles*3, end = Math.Min(first+ChunkTriangles*3, surface.triangles.Length);
                    for (int i = first; i < end; i += 3)
                    {
                        float at;
                        if (Triangle(origin, direction, surface.vertices[surface.triangles[i]],
                            surface.vertices[surface.triangles[i+1]], surface.vertices[surface.triangles[i+2]], out at) && at/scale < distance)
                        { distance = at/scale; closest = surface; }
                    }
                }
            }
            return closest;
        }

        static bool Triangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float at)
        {
            at = 0;
            Vector3 edge = b-a, other = c-a, p = Vector3.Cross(direction, other);
            float determinant = Vector3.Dot(edge, p);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1/determinant;
            Vector3 offset = origin-a;
            float u = Vector3.Dot(offset, p)*inverse;
            if (u < -1e-5f || u > 1.00001f) return false;
            Vector3 q = Vector3.Cross(offset, edge);
            float v = Vector3.Dot(direction, q)*inverse;
            if (v < -1e-5f || u+v > 1.00001f) return false;
            at = Vector3.Dot(other, q)*inverse;
            return at >= 0;
        }

        internal int Pick(Camera camera, Vector2 point, float edgePaddingPixels)
        {
            if (!camera || !camera.pixelRect.Contains(point)) return Nothing;
            int exact = ExactPick(camera, point);
            if (exact != Nothing || edgePaddingPixels <= 0) return exact;
            return EdgePick(camera, point, edgePaddingPixels);
        }

        int ExactPick(Camera camera, Vector2 point)
        {
            float distance;
            var hit = Cast(camera, camera.ScreenPointToRay(point), out distance);
            return hit != null ? hit.target : Nothing;
        }

        // Parked filters can be nearly edge-on. A small silhouette allowance
        // helps, but is accepted only if that edge is actually visible.
        int EdgePick(Camera camera, Vector2 point, float padding)
        {
            float best = padding*padding; int target = Nothing;
            foreach (var surface in surfaces)
            {
                if (surface.target < 0 || !Visible(surface, camera)) continue;
                Project(surface, camera);
                target = Edges(surface, camera, point, target, ref best);
            }
            return target;
        }

        static void Project(Surface surface, Camera camera)
        {
            for (int i = 0; i < surface.vertices.Length; i++)
                surface.screen[i] = camera.WorldToScreenPoint(surface.transform.TransformPoint(surface.vertices[i]));
        }

        int Edges(Surface surface, Camera camera, Vector2 point, int target, ref float best)
        {
            for (int i = 0; i < surface.triangles.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                    target = Edge(surface, camera, point, surface.screen[surface.triangles[i+edge]],
                        surface.screen[surface.triangles[i+(edge+1)%3]], target, ref best);
            return target;
        }

        int Edge(Surface surface, Camera camera, Vector2 point, Vector3 a, Vector3 b, int target, ref float best)
        {
            if (a.z <= camera.nearClipPlane || b.z <= camera.nearClipPlane) return target;
            Vector2 av = a, bv = b, delta = bv-av;
            float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point-av, delta)/delta.sqrMagnitude) : 0;
            Vector2 nearest = av+delta*t;
            float error = (nearest-point).sqrMagnitude;
            if (error >= best) return target;
            float distance;
            var visible = Cast(camera, camera.ScreenPointToRay(nearest), out distance);
            if (visible == null || visible.target != surface.target) return target;
            best = error;
            return surface.target;
        }

        internal static string AngleText(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "Угол: —";
            int tenths = (int)Math.Round(Math.Max(0, Math.Min(130, degrees))*600, MidpointRounding.AwayFromZero);
            return "Угол: "+(tenths/600).ToString(CultureInfo.InvariantCulture)+"° "+
                ((tenths%600)/10.0).ToString("00.0", CultureInfo.GetCultureInfo("ru-RU"))+"′";
        }

        internal static Rect TooltipPixels(Vector2 pointer, Vector2 size, Vector2 screen, float margin)
        {
            margin = Mathf.Max(0, Mathf.Min(margin, Mathf.Min(screen.x, screen.y)*.25f));
            size.x = Mathf.Min(size.x, Mathf.Max(1, screen.x-2*margin));
            size.y = Mathf.Min(size.y, Mathf.Max(1, screen.y-2*margin));
            float x = pointer.x+16, top = pointer.y-18;
            if (x+size.x > screen.x-margin) x = pointer.x-size.x-16;
            if (top-size.y < margin) top = pointer.y+size.y+18;
            x = Mathf.Clamp(x, margin, Mathf.Max(margin, screen.x-size.x-margin));
            top = Mathf.Clamp(top, Mathf.Min(screen.y-margin, size.y+margin), screen.y-margin);
            return new Rect(x, top-size.y, size.x, size.y);
        }

        public void Dispose()
        {
            foreach (var surface in surfaces)
                if (surface.baked)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(surface.baked);
                    else UnityEngine.Object.DestroyImmediate(surface.baked);
                }
            surfaces.Clear();
        }
    }
}
