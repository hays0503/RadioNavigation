using System;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Renders the four supported planets as distant camera-facing quads.
    /// The Moon has its own renderer (MoonDiscRenderer) since P2: it needs a
    /// baked phase texture, not a flat additive quad.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class PlanetRenderer : MonoBehaviour
    {
        private const int PlanetCount = 4;
        private const float MinPlanetSize = 0.35f;
        private const float MaxPlanetSize = 1.25f;

        private static readonly Color[] PlanetColors =
        {
            new Color(1.00f, 0.98f, 0.92f, 0.95f), // Venus
            new Color(1.00f, 0.42f, 0.24f, 0.95f), // Mars
            new Color(1.00f, 0.88f, 0.62f, 0.95f), // Jupiter
            new Color(1.00f, 0.82f, 0.48f, 0.95f)  // Saturn
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
        private float lastRebuildRealtime;

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
            {
                material.color = new Color(1f, 1f, 1f, SkyVisibilityResolver.GetVisibility());
                // Live sky render-queue override (clouds must blend over us).
                SkyRenderQueuePolicy.TryApply(material, SkyRenderQueueSwitch.QueueOverride);
                // Live cloud occlusion (UniStorm writes no depth).
                CloudOcclusionBinding.Apply(material);
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, longitude);
            bool cameraMoved = Vector3.Distance(cam.transform.position, lastCameraPosition)
                    > MeshRebuildPolicy.MaxCameraMoveM
                || Quaternion.Angle(cam.transform.rotation, lastCameraRotation)
                    > MeshRebuildPolicy.MaxCameraRotationDeg;
            bool timeDrift = Math.Abs(MeshRebuildPolicy.AngularDifferenceDeg(lstDeg, lastLstDeg))
                    > MeshRebuildPolicy.MaxLstDriftDeg
                || Math.Abs(latitude - lastLatitude) > MeshRebuildPolicy.MaxLatitudeDriftDeg;
            bool needsRebuild = !meshRenderer.enabled || cameraMoved
                || (timeDrift && MeshRebuildPolicy.RebuildIntervalElapsed(
                        lastRebuildRealtime, Time.realtimeSinceStartup));

            if (!needsRebuild)
                return;

            lastCameraPosition = cam.transform.position;
            lastCameraRotation = cam.transform.rotation;
            lastLstDeg = lstDeg;
            lastLatitude = latitude;
            lastRebuildRealtime = Time.realtimeSinceStartup;
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
                bool hasCoordinates = AstroMath.PlanetRaDec(planetId, jd, out ra, out dec);
                if (!hasCoordinates)
                    continue;

                AstroMath.RaDecToEnu(ra, dec, lstDeg, latitude,
                    out double altitude, out _, out double e, out double n, out double u);
                if (altitude <= 0)
                    continue;

                Vector3 direction = new Vector3((float)e, (float)u, (float)n).normalized;
                Vector3 center = cam.transform.position + direction * radius;
                Color color = PlanetColors[planetId];
                BillboardQuadBuilder.AddQuad(vertices, triangles, colors, uvs, center, direction, SizeForPlanet(planetId), color);
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
                default: return MinPlanetSize;
            }
        }

    }
}
