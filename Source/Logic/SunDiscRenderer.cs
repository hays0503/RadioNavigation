using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Physical-size solar disc for the sextant: date-dependent angular
    /// diameter, orange body with limb darkening and 3-5 random sunspots.
    /// Rendered only into the PIP cameras (layer 7, OR-ed into their culling
    /// masks; the bit is absent from every stock camera mask) so the main
    /// view never sees it. Positioned along the SH5 apparent direction at
    /// CameraResolver.SafeRadius. The native Sun is isolated per PIP render;
    /// drawing a smaller disc on top of its halo would not fix a measured limb.
    /// </summary>
    [DefaultExecutionOrder(6000)]
    public sealed class SunDiscRenderer : MonoBehaviour
    {
        public const int SunDiscLayer = 7;
        public const int SunDiscLayerMask = 1 << SunDiscLayer;
        internal static double ConfiguredTableDiameterDeg { get; set; }

        /// <summary>
        /// Last configured physical diameter. A sight snapshots THIS size,
        /// not the nominal fallback and not a live value read after recording.
        /// </summary>
        internal static double AngularDiameterDeg { get; private set; } = SunDiscScale.TrueAngularDiameterDeg;
        private const int TextureSize = 256;
        private const string LogPrefix = "[RadioNav][SunDisc]";

        /// <summary>Visibility gate set by SextantController (open). Filters
        /// change glare/dimming, not the source or diameter of a measured body.</summary>
        public static bool Active { get; set; }

        internal static Renderer MeasuringRenderer
        {
            get { return instance != null ? instance.meshRenderer : null; }
        }

        internal static bool MeasuringDiscReady
        {
            get { return instance != null && instance.bundledShaderResolved
                && instance.meshRenderer != null && instance.meshRenderer.enabled
                && instance.gameObject.activeInHierarchy; }
        }

        /// <summary>
        /// SH5 apparent solar centre, refreshed every late frame by
        /// SextantController. Non-normalized is fine - normalized in use.
        /// </summary>
        public static Vector3 SunDirection { get; set; }

        private static SunDiscRenderer instance;

        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private Material material;
        private Texture2D discTexture;
        private Color probeHomeColor;
        private bool probeRadianceChanged;
        private readonly System.Random rng = new System.Random();

        private struct SpotData
        {
            public float x;
            public float y;
            public float radius;
        }

        /// <summary>Regenerates the sunspot pattern (called on each sextant open).</summary>
        public static void RandomizeDisc()
        {
            if (instance != null)
                instance.RebuildTexture();
        }

        /// <summary>Clears the last sun direction (called on sextant close).</summary>
        public static void ResetDirection()
        {
            SunDirection = Vector3.zero;
            ConfiguredTableDiameterDeg = 0;
        }

        private void Awake()
        {
            instance = this;
            gameObject.layer = SunDiscLayer;

            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.enabled = false;

            BuildQuad(meshFilter);
            RebuildTexture();

            // Celestial shader ladder (bundled -> Sprites/Default -> legacy
            // alpha blended). The bundled shader carries the Sky-stage pass so
            // the clouds composite over the disc; the premultiplied
            // One/OneMinusSrcAlpha blend below is the disc's own blend.
            Shader shader = CelestialShaderResolver.ResolveShader(
                "Legacy Shaders/Particles/Alpha Blended");
            if (shader != null)
            {
                material = new Material(shader);
                material.mainTexture = discTexture;
                float radiance = (float)SextantShadeFilter.SolarSceneRadiance;
                material.SetColor("_Color", new Color(radiance, radiance, radiance, 1f));
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                meshRenderer.sharedMaterial = material;
                bundledShaderResolved = CelestialShaderResolver.IsBundled(shader);
                SkyRenderQueuePolicy.ApplySkyStage(material, bundledShaderResolved);
            }
            else
            {
                Debug.LogWarning(LogPrefix + " no suitable shader found - disc hidden");
            }

            Debug.Log(LogPrefix + " initialized, layer=" + SunDiscLayer);
        }

        private bool bundledShaderResolved;
        private int shaderUpgradeCountdown = 30;

        /// <summary>Bounded colour-path diagnosis, only in an explicit render
        /// session. No preferences, geometry, direction or calibration writes.
        /// Capture the original colour once and restore it on close/disable.</summary>
        public bool SetRadianceForProbe(float radiance)
        {
            if (!ProbeContextActive() || material == null
                || !SextantShadeFilter.IsProbeRadiance(radiance))
                return false;
            if (!probeRadianceChanged)
            {
                probeHomeColor = material.color;
                probeRadianceChanged = true;
            }
            material.SetColor("_Color", new Color(radiance, radiance, radiance, 1f));
            return true;
        }

        private bool ProbeContextActive()
        {
            SextantController controller = SextantController.Instance;
            return instance == this && Active && controller != null && controller.RenderOnlyForProbe;
        }

        private void ReconcileRadianceProbe()
        {
            if (probeRadianceChanged && !ProbeContextActive())
                RestoreProbeRadiance();
        }

        private void RestoreProbeRadiance()
        {
            if (!probeRadianceChanged)
                return;
            if (material != null)
                material.SetColor("_Color", probeHomeColor);
            probeRadianceChanged = false;
        }

        private void OnDisable()
        {
            RestoreProbeRadiance();
        }

        /// <summary>
        /// The mod bundle may not be loaded when the sky roots are created, so
        /// the material starts on the stock fallback and upgrades once the
        /// bundled shader becomes available (bounded retries, same ladder as
        /// StarsMesh). The upgrade also applies the Sky-stage queue.
        /// </summary>
        private void TryUpgradeToBundledShader()
        {
            if (bundledShaderResolved || material == null || shaderUpgradeCountdown <= 0)
                return;

            Shader bundled = CelestialShaderResolver.LoadBundled();
            if (bundled != null)
            {
                material.shader = bundled;
                bundledShaderResolved = true;
                SkyRenderQueuePolicy.ApplySkyStage(material, true);
                Debug.Log(LogPrefix + " celestial shader resolved to bundled " + bundled.name);
                return;
            }
            shaderUpgradeCountdown--;
        }

        private void OnDestroy()
        {
            RestoreProbeRadiance();
            if (instance == this)
                instance = null;
            if (discTexture != null)
                Destroy(discTexture);
            if (material != null)
                Destroy(material);
            // BuildQuad assigns our own mesh (named) to sharedMesh — free it;
            // a foreign mesh (game reassigned) must survive.
            if (meshFilter != null && meshFilter.sharedMesh != null
                && meshFilter.sharedMesh.name == "RadioNavSunDiscQuad")
                Destroy(meshFilter.sharedMesh);
        }

        private static void BuildQuad(MeshFilter meshFilter)
        {
            Mesh mesh = new Mesh();
            mesh.name = "RadioNavSunDiscQuad";
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
            meshFilter.sharedMesh = mesh;
        }

        private void RebuildTexture()
        {
            if (discTexture != null)
                Destroy(discTexture);
            discTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            discTexture.name = "RadioNavSunDisc";
            discTexture.hideFlags = HideFlags.DontSave;
            discTexture.wrapMode = TextureWrapMode.Clamp;
            discTexture.filterMode = FilterMode.Bilinear;

            int spotCount = rng.Next(3, 6);
            SpotData[] spots = new SpotData[spotCount];
            for (int i = 0; i < spotCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = (float)(rng.NextDouble() * 0.65);
                spots[i].x = (float)Math.Cos(angle) * dist;
                spots[i].y = (float)Math.Sin(angle) * dist;
                spots[i].radius = 0.03f + (float)rng.NextDouble() * 0.09f;
            }

            float half = TextureSize * 0.5f;
            const float discR = (float)CelestialDiscRaster.LimbRadius;
            Color center = new Color(1f, 0.72f, 0.22f, 1f);
            Color limb = new Color(0.62f, 0.28f, 0.04f, 1f);
            Color spotColor = new Color(0.28f, 0.13f, 0.02f, 1f);

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);

                    float alpha = (float)CelestialDiscRaster.Coverage(r, TextureSize);

                    if (alpha <= 0f)
                    {
                        discTexture.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }

                    float limbFactor = Mathf.Clamp01(r / discR);
                    Color c = Color.Lerp(center, limb, limbFactor * limbFactor);

                    float rn = r / discR;
                    for (int i = 0; i < spots.Length; i++)
                    {
                        if (rn >= 0.98f)
                            break;
                        float sx = dx - spots[i].x * discR;
                        float sy = dy - spots[i].y * discR;
                        float sd = (float)Math.Sqrt(sx * sx + sy * sy);
                        if (sd < spots[i].radius)
                        {
                            float t = 1f - sd / spots[i].radius;
                            t = t * t * (3f - 2f * t);
                            c = Color.Lerp(c, spotColor, t * 0.9f);
                        }
                    }

                    // Premultiplied alpha: the material blends
                    // One/OneMinusSrcAlpha, so rgb must fade with alpha or
                    // the disc edge shows a bright orange ring.
                    c.a = alpha;
                    c.r *= alpha;
                    c.g *= alpha;
                    c.b *= alpha;
                    discTexture.SetPixel(x, y, c);
                }
            }
            discTexture.Apply();
            if (material != null)
                material.mainTexture = discTexture;
            Debug.Log(LogPrefix + " texture rebuilt, spots=" + spots.Length);
        }

        private void LateUpdate()
        {
            ReconcileRadianceProbe();
            TryUpgradeToBundledShader();

            if (!Active || material == null)
            {
                meshRenderer.enabled = false;
                if (!Active && SunDirection.sqrMagnitude > 0.01f)
                    SunDirection = Vector3.zero;
                return;
            }

            // Live sky render-queue override (clouds must blend over us).
            SkyRenderQueuePolicy.TryApply(material, SkyRenderQueueSwitch.QueueOverride);
            // Live cloud occlusion (UniStorm writes no depth).
            CloudOcclusionBinding.Apply(material);

            Vector3 dir = SunDirection;
            if (dir.sqrMagnitude < 0.01f)
            {
                meshRenderer.enabled = false;
                return;
            }
            dir.Normalize();

            Camera cam;
            if (!CameraResolver.TryResolve(out cam))
            {
                meshRenderer.enabled = false;
                return;
            }

            float radius = CameraResolver.SafeRadius(cam);
            AngularDiameterDeg = ConfiguredTableDiameterDeg;
            if (AngularDiameterDeg < 0.5 || AngularDiameterDeg > 0.56)
            {
                meshRenderer.enabled = false;
                return;
            }
            float size = (float)CelestialDiscRaster.QuadSide(radius, AngularDiameterDeg);

            transform.position = cam.transform.position + dir * radius;
            transform.rotation = Quaternion.LookRotation(cam.transform.position - transform.position, Vector3.up);
            transform.localScale = new Vector3(size, size, 1f);
            meshRenderer.enabled = true;
        }
    }
}
