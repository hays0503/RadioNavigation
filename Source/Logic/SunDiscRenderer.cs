using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Real-size solar disc for the sextant's filtered view: 0.53° angular
    /// diameter, orange body with limb darkening and 3-5 random sunspots.
    /// Rendered only into the PIP cameras (layer 7, OR-ed into their culling
    /// masks; the bit is absent from every stock camera mask) so the main
    /// view never sees it. Positioned along the game sun direction at
    /// CameraResolver.SafeRadius so it covers the game's own sun sprite;
    /// the mirrored post stack blooms the bright disc into a halo.
    /// </summary>
    public sealed class SunDiscRenderer : MonoBehaviour
    {
        public const int SunDiscLayer = 7;
        public const int SunDiscLayerMask = 1 << SunDiscLayer;

        /// <summary>
        /// Readable solar disc: the real 0.53° scaled by the player-measured
        /// 4.0625× (GIMP overlay 48px→195px) so the filtered view shows the
        /// disc big enough to read sunspots. Angular size - scales with zoom.
        /// </summary>
        private const float AngularDiameterDeg = 0.53f * 4.0625f;
        private const float AngularRadiusRad = AngularDiameterDeg * 0.5f * Mathf.Deg2Rad;
        private const int TextureSize = 256;
        private const string LogPrefix = "[RadioNav][SunDisc]";

        /// <summary>Visibility gate set by SextantController (open &amp;&amp; filters &gt; 0).</summary>
        public static bool Active { get; set; }

        /// <summary>
        /// Game sun direction (fallback: Meeus), refreshed every frame by
        /// SextantController. Non-normalized is fine - normalized in use.
        /// </summary>
        public static Vector3 SunDirection { get; set; }

        private static SunDiscRenderer instance;

        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private Material material;
        private Texture2D discTexture;
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
            const float discR = 0.93f;
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

                    float alpha;
                    if (r <= discR) alpha = 1f;
                    else if (r >= 1f) alpha = 0f;
                    else alpha = 1f - (r - discR) / (1f - discR);

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
            float size = 2f * radius * (float)Math.Tan(AngularRadiusRad);

            transform.position = cam.transform.position + dir * radius;
            transform.rotation = Quaternion.LookRotation(cam.transform.position - transform.position, Vector3.up);
            transform.localScale = new Vector3(size, size, 1f);
            meshRenderer.enabled = true;
        }
    }
}
