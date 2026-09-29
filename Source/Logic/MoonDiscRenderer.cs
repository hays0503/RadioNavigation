using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Moon disc with a real terminator (CELESTIAL_REALISM_PLAN P2). The old
    /// PlanetRenderer faded a flat quad to transparent at new moon; here the
    /// phase is baked into a texture (MoonPhaseDisc) and the disc is drawn at
    /// the topocentric Moon position, so a crescent looks like a crescent and
    /// the dark limb still hides the stars behind it.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class MoonDiscRenderer : MonoBehaviour
    {
        /// <summary>Texture resolution of the baked phase disc.</summary>
        private const int TextureSize = 128;

        /// <summary>Real angular diameter is ~0.52°; ×2 keeps the disc
        /// readable on screen without turning it into a cartoon.</summary>
        private const float DiameterDeg = 0.52f * 2f;

        /// <summary>Phase change (radians) that triggers a texture rebake.</summary>
        private const double RebakePhaseDelta = 0.035; // ~2°

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private Texture2D discTexture;
        private IPlayerPositionProvider positionProvider;

        private double lastPhaseAngle = double.NaN;
        private double lastLimbAngle = double.NaN;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

            BuildQuad(meshFilter);
            RebuildTexture(0.0, 0.0);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader != null)
            {
                material = new Material(shader);
                material.mainTexture = discTexture;
                // Premultiplied alpha (the texture carries rgb·a): the lit limb
                // is opaque and the dark side still occludes the stars.
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                meshRenderer.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning("[RadioNav][MoonDisc] no suitable shader found - disc hidden");
            }
        }

        private void OnDestroy()
        {
            if (discTexture != null)
                Destroy(discTexture);
            if (material != null)
                Destroy(material);
            if (meshFilter != null && meshFilter.sharedMesh != null
                && meshFilter.sharedMesh.name == "RadioNavMoonQuad")
                Destroy(meshFilter.sharedMesh);
        }

        private static void BuildQuad(MeshFilter filter)
        {
            Mesh mesh = new Mesh();
            mesh.name = "RadioNavMoonQuad";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
        }

        private void RebuildTexture(double phaseAngle, double limbAngle)
        {
            if (discTexture != null)
                Destroy(discTexture);
            float[] alpha = MoonPhaseDisc.BuildAlphaField(TextureSize, phaseAngle, limbAngle);
            Color32[] pixels = new Color32[alpha.Length];
            for (int i = 0; i < alpha.Length; i++)
            {
                float a = alpha[i];
                // The lit side is bright grey; the unlit side stays a very dark
                // disc (premultiplied, so it reads as a body, not a hole).
                byte level = (byte)((0.06f + 0.88f * a) * a * 255f + 0.5f);
                byte alphaByte = (byte)(a * 255f + 0.5f);
                pixels[i] = new Color32(level, level, level, alphaByte);
            }
            discTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            discTexture.name = "RadioNavMoonDisc";
            discTexture.hideFlags = HideFlags.DontSave;
            discTexture.wrapMode = TextureWrapMode.Clamp;
            discTexture.filterMode = FilterMode.Bilinear;
            discTexture.SetPixels32(pixels);
            discTexture.Apply();
            if (material != null)
                material.mainTexture = discTexture;

            lastPhaseAngle = phaseAngle;
            lastLimbAngle = limbAngle;
        }

        private void LateUpdate()
        {
            Camera cam;
            float lat, lon;
            if (!CameraResolver.TryResolve(out cam)
                || !positionProvider.TryGetPosition(out lat, out lon))
            {
                meshRenderer.enabled = false;
                return;
            }

            bool outside = SkyVisibilityResolver.IsPlayerOutside();
            if (!outside)
            {
                meshRenderer.enabled = false;
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);
            if (!AstroMath.MoonTopocentricRaDec(jd, lat, lstDeg,
                    out double moonRa, out double moonDec))
            {
                meshRenderer.enabled = false;
                return;
            }
            AstroMath.RaDecToEnu(moonRa, moonDec, lstDeg, lat,
                out double altRad, out _, out double e, out double n, out double u);
            if (altRad <= 0.0)
            {
                meshRenderer.enabled = false;
                return;
            }

            // Phase from the same ephemerides the worksheet uses.
            AstroMath.SunRaDec(SunEphemerisCalibration.JdTt(utc),
                out double sunRa, out double sunDec);
            double phaseAngle = MoonPhaseDisc.PhaseAngle(sunRa, sunDec, moonRa, moonDec);
            double limbAngle = MoonPhaseDisc.BrightLimbAngle(sunRa, sunDec, moonRa, moonDec);
            if (double.IsNaN(lastPhaseAngle)
                || Math.Abs(phaseAngle - lastPhaseAngle) > RebakePhaseDelta
                || Math.Abs(limbAngle - lastLimbAngle) > RebakePhaseDelta)
                RebuildTexture(phaseAngle, limbAngle);

            if (material != null)
            {
                material.color = new Color(1f, 1f, 1f, SkyVisibilityResolver.GetVisibility());
                // Live sky render-queue override (clouds must blend over us).
                SkyRenderQueuePolicy.TryApply(material, SkyRenderQueueSwitch.QueueOverride);
            }

            float radius = CameraResolver.SafeRadius(cam);
            float size = 2f * radius
                * (float)Math.Tan(DiameterDeg * 0.5f * Mathf.Deg2Rad);
            Vector3 direction = new Vector3((float)e, (float)u, (float)n).normalized;
            transform.position = cam.transform.position + direction * radius;
            transform.rotation = Quaternion.LookRotation(
                cam.transform.position - transform.position, Vector3.up);
            transform.localScale = new Vector3(size, size, 1f);
            meshRenderer.enabled = true;
        }
    }
}
