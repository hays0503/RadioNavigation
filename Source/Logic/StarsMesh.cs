using System;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Renders stars as billboards from StarCatalog (STAR_SKY_PLAN §9).
    /// Best practice: rebuild mesh only when camera moves > threshold, no allocation in Update.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class StarsMesh : MonoBehaviour
    {
        // NOTE: no local WorldRadiusM — the single source of truth lives in
        // CameraResolver (SafeRadius); the dead duplicate was removed (iter6).
        private const float MinStarSizePx = 2f;
        private const float MaxStarSizePx = 20f;
        private const float NavStarMultiplier = 1.9f;
        private const float NavStarAlphaMultiplier = 1.5f;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private IPlayerPositionProvider positionProvider;

        private Vector3 lastCamPos;
        private Quaternion lastCamRot;
        private double lastLstDeg;
        private float lastLat;
        private float lastLogTime;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

            // Create additive blend material (Sprites/Default or fallback)
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
            else
            {
                Debug.LogWarning("[RadioNav][StarsMesh] No suitable shader found, using default");
            }

            Debug.Log("[RadioNav][StarsMesh] Initialized with " + StarCatalog.Stars.Length + " stars");
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

            // Star sky is visible only from outside the boat (bridge/deck):
            // hidden inside (periscope, interior) and on the map.
            bool outside = SkyVisibilityResolver.IsPlayerOutside();
            if (meshRenderer.enabled != outside)
                meshRenderer.enabled = outside;
            if (!outside)
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
            bool needsRebuild = MeshRebuildPolicy.NeedsRebuild(
                cam.transform.position, lastCamPos,
                cam.transform.rotation, lastCamRot,
                lstDeg, lastLstDeg, lat, lastLat, true);

            if (!needsRebuild && meshFilter.mesh != null && meshFilter.mesh.vertexCount > 0)
                return;

            lastCamPos = cam.transform.position;
            lastCamRot = cam.transform.rotation;
            lastLstDeg = lstDeg;
            lastLat = lat;

            RebuildMesh(cam, lat, lstDeg);
        }

        private void RebuildMesh(Camera cam, float latDeg, double lstDeg)
        {
            vertices.Clear();
            triangles.Clear();
            colors.Clear();
            uvs.Clear();

            int visibleCount = 0;
            float safeRadius = CameraResolver.SafeRadius(cam);

            foreach (var star in StarCatalog.Stars)
            {
                double raRad = star.RaDeg * Mathf.Deg2Rad;
                double decRad = star.DecDeg * Mathf.Deg2Rad;

                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                    out double altRad, out double azRad, out double e, out double n, out double u);

                if (altRad <= 0) continue; // below horizon

                // Convert ENU to world direction (Unity: X=east, Y=up, Z=north)
                Vector3 dir = new Vector3((float)e, (float)u, (float)n).normalized;
                Vector3 worldPos = cam.transform.position + dir * safeRadius;

                // Size based on magnitude (brighter = larger)
                float size = SizeFromMagnitude(star.Vmag, star.IsNav);

                // Color from B-V index
                Color color = ColorFromBV(star.Bv, star.Vmag);

                // Navigation stars render brighter (alpha boosted, clamped)
                if (star.IsNav)
                    color.a = Mathf.Clamp01(color.a * NavStarAlphaMultiplier);

                // Billboard quad (4 vertices, 2 triangles)
                BillboardQuadBuilder.AddQuad(vertices, triangles, colors, uvs, worldPos, dir, size, color);
                visibleCount++;
            }

            // Build mesh
            Mesh mesh = meshFilter.mesh;
            if (mesh == null)
                mesh = meshFilter.mesh = new Mesh();

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            if (visibleCount > 0 && Time.time - lastLogTime > 5f)
            {
                lastLogTime = Time.time;
                Debug.Log($"[RadioNav][StarsMesh] Rendered {visibleCount}/{StarCatalog.Stars.Length} stars");
            }
        }

        private float SizeFromMagnitude(float vmag, bool isNav)
        {
            // Brighter stars (lower vmag) = larger size
            // Range: vmag -1.5 (Sirius) → 6.0 (faintest) → size 20px → 2px
            float t = Mathf.InverseLerp(6f, -1.5f, vmag);
            float size = Mathf.Lerp(MinStarSizePx, MaxStarSizePx, t);
            if (isNav) size *= NavStarMultiplier;
            return size * 0.1f; // scale down for world units
        }

        private Color ColorFromBV(float bv, float vmag)
        {
            // B-V index → RGB (simplified Stellarium-like map)
            // B-V < 0: blue, 0: white, 0.5: yellow-white, 1.0: orange, 1.5+: red
            float r, g, b;
            if (bv < -0.4f)
            {
                r = 0.6f; g = 0.7f; b = 1.0f; // blue
            }
            else if (bv < 0.0f)
            {
                float t = Mathf.InverseLerp(-0.4f, 0.0f, bv);
                r = Mathf.Lerp(0.6f, 1.0f, t);
                g = Mathf.Lerp(0.7f, 1.0f, t);
                b = 1.0f;
            }
            else if (bv < 0.5f)
            {
                float t = Mathf.InverseLerp(0.0f, 0.5f, bv);
                r = 1.0f;
                g = Mathf.Lerp(1.0f, 0.95f, t);
                b = Mathf.Lerp(1.0f, 0.8f, t);
            }
            else if (bv < 1.0f)
            {
                float t = Mathf.InverseLerp(0.5f, 1.0f, bv);
                r = 1.0f;
                g = Mathf.Lerp(0.95f, 0.7f, t);
                b = Mathf.Lerp(0.8f, 0.4f, t);
            }
            else if (bv < 1.5f)
            {
                float t = Mathf.InverseLerp(1.0f, 1.5f, bv);
                r = 1.0f;
                g = Mathf.Lerp(0.7f, 0.4f, t);
                b = Mathf.Lerp(0.4f, 0.2f, t);
            }
            else
            {
                r = 1.0f; g = 0.3f; b = 0.1f; // deep red
            }

            // Alpha based on magnitude (brighter = more opaque)
            float alpha = Mathf.InverseLerp(6f, -1.5f, vmag);
            return new Color(r, g, b, alpha);
        }
    }
}
