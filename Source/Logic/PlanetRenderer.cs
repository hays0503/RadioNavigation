using System;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Renders the four supported planets as distant camera-facing quads.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class PlanetRenderer : MonoBehaviour
    {
        private const int PlanetCount = 5;
        private const float MinPlanetSize = 0.35f;
        private const float MaxPlanetSize = 1.25f;

        private static readonly Color[] PlanetColors =
        {
            new Color(1.00f, 0.98f, 0.92f, 0.95f), // Venus
            new Color(1.00f, 0.42f, 0.24f, 0.95f), // Mars
            new Color(1.00f, 0.88f, 0.62f, 0.95f), // Jupiter
            new Color(1.00f, 0.82f, 0.48f, 0.95f), // Saturn
            new Color(0.85f, 0.88f, 0.95f, 1.00f)  // Moon
        };

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private IPlayerPositionProvider positionProvider;
        private readonly List<Vector3> vertices = new List<Vector3>(PlanetCount * 4);
        private readonly List<int> triangles = new List<int>(PlanetCount * 6);
        private readonly List<Color> colors = new List<Color>(PlanetCount * 4);
        private readonly List<Vector2> uvs = new List<Vector2>(PlanetCount * 4);

        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;
        private double lastLstDeg = double.NaN;
        private float lastLatitude = float.NaN;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

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
                Debug.LogWarning("[RadioNav][PlanetRenderer] No suitable shader found");
            }
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
            if (meshFilter != null && meshFilter.mesh != null)
                Destroy(meshFilter.mesh);
        }

        private void LateUpdate()
        {
            Camera cam;
            float latitude, longitude;
            if (!CameraResolver.TryResolve(out cam) || !positionProvider.TryGetPosition(out latitude, out longitude))
            {
                meshRenderer.enabled = false;
                return;
            }

            // Planets follow the star sky: only outside the boat.
            bool outside = SkyVisibilityResolver.IsPlayerOutside();
            if (!outside)
            {
                meshRenderer.enabled = false;
                return;
            }

            if (material != null)
                material.color = new Color(1f, 1f, 1f, SkyVisibilityResolver.GetVisibility());

            double jd = AstroMath.ToJulianDay(GameTimeResolver.GetUtcNow());
            double lstDeg = AstroMath.LstDeg(jd, longitude);
            bool needsRebuild = !meshRenderer.enabled
                || Vector3.Distance(cam.transform.position, lastCameraPosition) > 10f
                || Quaternion.Angle(cam.transform.rotation, lastCameraRotation) > 5f
                || Math.Abs(lstDeg - lastLstDeg) > 0.1
                || Math.Abs(latitude - lastLatitude) > 0.01f;

            if (!needsRebuild)
                return;

            lastCameraPosition = cam.transform.position;
            lastCameraRotation = cam.transform.rotation;
            lastLstDeg = lstDeg;
            lastLatitude = latitude;
            RebuildMesh(cam, latitude, lstDeg, jd);
            meshRenderer.enabled = true;
        }

        private void RebuildMesh(Camera cam, float latitude, double lstDeg, double jd)
        {
            vertices.Clear();
            triangles.Clear();
            colors.Clear();
            uvs.Clear();

            float radius = CameraResolver.SafeRadius(cam);
            for (int planetId = 0; planetId < PlanetCount; planetId++)
            {
                double ra, dec;
                bool hasCoordinates = planetId == 4
                    ? AstroMath.MoonRaDec(jd, out ra, out dec)
                    : AstroMath.PlanetRaDec(planetId, jd, out ra, out dec);
                if (!hasCoordinates)
                    continue;

                AstroMath.RaDecToEnu(ra, dec, lstDeg, latitude,
                    out double altitude, out _, out double e, out double n, out double u);
                if (altitude <= 0)
                    continue;

                Vector3 direction = new Vector3((float)e, (float)u, (float)n).normalized;
                Vector3 center = cam.transform.position + direction * radius;
                Color color = PlanetColors[planetId];
                if (planetId == 4)
                    color.a *= (float)AstroMath.MoonIllumination(jd);
                AddBillboardQuad(center, direction, SizeForPlanet(planetId), color);
            }

            Mesh mesh = meshFilter.mesh;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
        }

        private float SizeForPlanet(int planetId)
        {
            // Venus and Jupiter are the most conspicuous naked-eye planets.
            switch (planetId)
            {
                case 0: return MaxPlanetSize;
                case 2: return 1.05f;
                case 3: return 0.8f;
                case 4: return 1.5f;
                default: return MinPlanetSize;
            }
        }

        private void AddBillboardQuad(Vector3 center, Vector3 forward, float size, Color color)
        {
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward).normalized;
            float halfSize = size * 0.5f;
            int baseIndex = vertices.Count;

            vertices.Add(center - right * halfSize - up * halfSize);
            vertices.Add(center + right * halfSize - up * halfSize);
            vertices.Add(center + right * halfSize + up * halfSize);
            vertices.Add(center - right * halfSize + up * halfSize);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));
        }
    }
}
