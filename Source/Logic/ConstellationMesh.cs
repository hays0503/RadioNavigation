using System;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Renders constellation lines as thin quads between stars (STAR_SKY_PLAN §9).
    /// Best practice: rebuild mesh only when camera moves > threshold, no allocation in Update.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ConstellationMesh : MonoBehaviour
    {
        private const float WorldRadiusM = 3900f;
        private const float LineWidthArcMin = 2f; // angular width in arcminutes
        private static readonly Color LineColor = new Color(0.55f, 0.22f, 0.15f, 0.4f); // reddish-brown

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private IPlayerPositionProvider positionProvider;

        private Vector3 lastCamPos;
        private double lastLstDeg;
        private float lastLat;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

            // Create additive blend material
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                material = new Material(shader);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_ZWrite", 0);
                meshRenderer.material = material;
            }

            Debug.Log("[RadioNav][ConstellationMesh] Initialized with " + ConstellationLines.Polylines.Length + " line segments");
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
            if (meshFilter.mesh != null)
                Destroy(meshFilter.mesh);
        }

        private void LateUpdate()
        {
            Camera cam;
            if (!CameraResolver.TryResolve(out cam))
                return;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return;

            if (material != null)
                material.color = new Color(1f, 1f, 1f, SkyVisibilityResolver.GetVisibility());

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            // Rebuild mesh only if camera/position/LST changed significantly
            bool needsRebuild = Vector3.Distance(cam.transform.position, lastCamPos) > 10f
                             || Math.Abs(lstDeg - lastLstDeg) > 0.1
                             || Math.Abs(lat - lastLat) > 0.01f;

            if (!needsRebuild && meshFilter.mesh != null && meshFilter.mesh.vertexCount > 0)
                return;

            lastCamPos = cam.transform.position;
            lastLstDeg = lstDeg;
            lastLat = lat;

            RebuildMesh(cam, lat, lstDeg);
        }

        private void RebuildMesh(Camera cam, float latDeg, double lstDeg)
        {
            vertices.Clear();
            triangles.Clear();
            colors.Clear();

            int visibleSegments = 0;
            float safeRadius = CameraResolver.SafeRadius(cam);
            float lineWidthRad = LineWidthArcMin / 60f * Mathf.Deg2Rad; // convert arcmin to radians

            foreach (var polyline in ConstellationLines.Polylines)
            {
                if (polyline.Length < 2) continue;

                // Convert all points to world positions
                List<Vector3> worldPoints = new List<Vector3>();
                for (int i = 0; i < polyline.Length; i++)
                {
                    int starIdx = polyline[i];
                    if (starIdx < 0 || starIdx >= StarCatalog.Stars.Length) continue;

                    var star = StarCatalog.Stars[starIdx];
                    double raRad = star.RaDeg * Mathf.Deg2Rad;
                    double decRad = star.DecDeg * Mathf.Deg2Rad;

                    AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                        out double altRad, out _, out double e, out double n, out double u);

                    if (altRad <= 0) continue; // below horizon

                    Vector3 dir = new Vector3((float)e, (float)u, (float)n).normalized;
                    Vector3 worldPos = cam.transform.position + dir * safeRadius;
                    worldPoints.Add(worldPos);
                }

                // Draw segments between consecutive visible points
                for (int i = 0; i < worldPoints.Count - 1; i++)
                {
                    Vector3 p0 = worldPoints[i];
                    Vector3 p1 = worldPoints[i + 1];
                    AddLineSegment(p0, p1, lineWidthRad * safeRadius);
                    visibleSegments++;
                }
            }

            // Build mesh
            Mesh mesh = meshFilter.mesh;
            if (mesh == null)
                mesh = meshFilter.mesh = new Mesh();

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();

            if (visibleSegments > 0)
                Debug.Log($"[RadioNav][ConstellationMesh] Rendered {visibleSegments} line segments");
        }

        private void AddLineSegment(Vector3 p0, Vector3 p1, float width)
        {
            // Create quad between p0 and p1 with given width
            Vector3 direction = (p1 - p0).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, (p0 - Vector3.zero).normalized).normalized;
            if (perpendicular.sqrMagnitude < 0.001f)
                perpendicular = Vector3.up;

            float halfWidth = width * 0.5f;
            Vector3 v0 = p0 - perpendicular * halfWidth;
            Vector3 v1 = p0 + perpendicular * halfWidth;
            Vector3 v2 = p1 + perpendicular * halfWidth;
            Vector3 v3 = p1 - perpendicular * halfWidth;

            int baseIdx = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx + 3);

            colors.Add(LineColor);
            colors.Add(LineColor);
            colors.Add(LineColor);
            colors.Add(LineColor);
        }
    }
}
