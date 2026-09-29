using System;
using System.Collections.Generic;
using System.Globalization;
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
        // Photometry (size/alpha from V magnitude) lives in StarBrightness;
        // the gaussian PSF field lives in StarSpriteMath (both unit-tested).

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private Texture2D spriteTexture;
        private IPlayerPositionProvider positionProvider;

        // Sky brightness / adaptation rebuild triggers (P4).
        private const double MaxLimitingDriftMag = 0.1;
        private const float AdaptationRebuildInterval = 0.2f; // 5 Hz while ramping
        private readonly DarkAdaptationState adaptation = new DarkAdaptationState();
        private double lastLimitingMag = double.NaN;
        private float lastAdaptationRebuildTime;
        private float lastRebuildRealtime;
        private static StarsMesh instance;

        /// <summary>Shade filters / red light preserve dark adaptation (P4).
        /// Set by SextantController while any filter is down.</summary>
        public static bool Shielded { get; set; }

        /// <summary>Current dark-adaptation factor of the point-star layer
        /// (1 = fully adapted). The Sternspreiz streak field applies the same
        /// value so lines and points fade together.</summary>
        public static float CurrentAdaptation
        {
            get { return instance != null ? (float)instance.adaptation.Value : 1f; }
        }

        /// <summary>White flash / sun glare resets dark adaptation (P4).
        /// Called from the glare coordinator's pain rising edge.</summary>
        public static void NotifyWhiteFlash()
        {
            if (instance != null)
                instance.adaptation.Reset();
        }

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
            instance = this;
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

            material = CreateStarMaterial();
            if (material != null)
                meshRenderer.material = material;
            UsesBundledStarShader = material != null && material.shader != null
                && material.shader.name == "RadioNav/SternspreizStreak";

            Debug.Log("[RadioNav][StarsMesh] Initialized with " + StarCatalog.Stars.Length + " stars");
        }

        /// <summary>
        /// Star material ladder: the mod's bundled shader first (it carries the
        /// scene passes the Deep pipeline actually draws - live 2026-09-28 the
        /// stock sprite shader contributed zero pixels), then the built-in
        /// sprite shader, then the legacy additive one. The bundled shader also
        /// honors _SrcBlend/_DstBlend/_ZWrite, so the additive star light below
        /// is real instead of a silent no-op on Sprites/Default.
        /// </summary>
        private Material CreateStarMaterial()
        {
            Shader shader = ResolveStarShader();
            if (shader == null)
            {
                Debug.LogWarning("[RadioNav][StarsMesh] No suitable shader found, using default");
                return null;
            }

            var created = new Material(shader);
            created.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            created.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            created.SetFloat("_ZWrite", 0f);
            // Gaussian PSF sprite: without a texture the quads rendered as
            // solid squares (P3). Straight white + SrcAlpha/One blend.
            spriteTexture = BuildSpriteTexture();
            created.mainTexture = spriteTexture;
            return created;
        }

        private const string BundledStarShader = "RadioNavStarField";

        /// <summary>True once the star layer actually draws with the bundled
        /// star shader (the one the Deep pipeline renders). The game's
        /// decorative star sphere is hidden only in that state, so a loading or
        /// shader failure can never leave an empty sky.</summary>
        public static bool UsesBundledStarShader { get; private set; }

        private bool bundledShaderResolved;
        private int shaderUpgradeCountdown = 30;

        private static Shader ResolveStarShader()
        {
            Shader bundled = ModBootstrapper.LoadBundleAsset(BundledStarShader) as Shader;
            if (bundled != null)
                return bundled;
            Shader fallback = Shader.Find("Sprites/Default");
            return fallback != null ? fallback : Shader.Find("Legacy Shaders/Particles/Additive");
        }

        /// <summary>
        /// The mod bundle may not be loaded when the sky roots are created, so
        /// the star material starts on the built-in fallback and upgrades once
        /// the bundled shader becomes available (bounded retries, no per-frame
        /// reflection storm after that).
        /// </summary>
        private void TryUpgradeToBundledShader()
        {
            if (bundledShaderResolved || material == null || shaderUpgradeCountdown <= 0)
                return;

            Shader bundled = ModBootstrapper.LoadBundleAsset(BundledStarShader) as Shader;
            if (bundled != null)
            {
                material.shader = bundled;
                bundledShaderResolved = true;
                UsesBundledStarShader = true;
                Debug.Log("[RadioNav][StarsMesh] star shader resolved to bundled " + bundled.name);
                return;
            }
            shaderUpgradeCountdown--;
        }

        /// <summary>Builds the 64² gaussian sprite from StarSpriteMath.</summary>
        private static Texture2D BuildSpriteTexture()
        {
            float[] field = StarSpriteMath.BuildAlphaField(
                StarSpriteMath.SpriteSize, StarSpriteMath.SpriteSigma);
            Color32[] pixels = new Color32[field.Length];
            for (int i = 0; i < field.Length; i++)
            {
                byte a = (byte)(field[i] * 255f + 0.5f);
                pixels[i] = new Color32(255, 255, 255, a);
            }
            Texture2D tex = new Texture2D(StarSpriteMath.SpriteSize, StarSpriteMath.SpriteSize,
                TextureFormat.RGBA32, false);
            tex.name = "RadioNavStarSprite";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.DontSave;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
            if (spriteTexture != null)
                Destroy(spriteTexture);
            if (material != null)
                Destroy(material);
            if (meshFilter.mesh != null)
                Destroy(meshFilter.mesh);
        }

        /// <summary>One-shot evidence that the star renderer is actually
        /// submitted to a camera: it fires only for cameras that render this
        /// object. Without it a silent sky is indistinguishable from a culled
        /// one (live 2026-09-28: the layer was believed invisible until the
        /// shader pass and the quad scale were fixed).</summary>
        private static bool willRenderLogged;

        private void OnWillRenderObject()
        {
            if (willRenderLogged)
                return;
            willRenderLogged = true;
            Camera current = Camera.current;
            Debug.Log("[RadioNav][StarsMesh] OnWillRenderObject camera="
                + (current != null ? current.name : "none")
                + " shader=" + (material != null && material.shader != null ? material.shader.name : "none"));
        }

        private void LateUpdate()
        {
            Camera cam;
            if (!CameraResolver.TryResolve(out cam))
                return;

            TryUpgradeToBundledShader();

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
            {
                material.color = new Color(1f, 1f, 1f, SkyVisibilityResolver.GetVisibility());
                // Live sky render-queue override (clouds must blend over us).
                SkyRenderQueuePolicy.TryApply(material, SkyRenderQueueSwitch.QueueOverride);
                // Live cloud occlusion (UniStorm writes no depth).
                CloudOcclusionBinding.Apply(material);
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            // Sky brightness (P4): twilight and moonwash raise the limiting
            // magnitude, marine haze eats low stars, and the eye adaptation
            // ramps the whole layer after stepping onto the bridge.
            float fogginess = SkyVisibilityResolver.GetFogginess();
            double limitingMag = ComputeLimitingMagnitude(utc, lat, lon);
            adaptation.Step(Time.unscaledDeltaTime, true, Shielded);
            float adapt = (float)adaptation.Value;

            bool adapting = adaptation.IsRamping
                && Time.unscaledTime - lastAdaptationRebuildTime > AdaptationRebuildInterval;

            if (!NeedsRebuild(cam, lstDeg, lat, limitingMag, adapting)
                && meshFilter.mesh != null && meshFilter.mesh.vertexCount > 0)
                return;

            lastCamPos = cam.transform.position;
            lastCamRot = cam.transform.rotation;
            lastLstDeg = lstDeg;
            lastLat = lat;
            lastLimitingMag = limitingMag;
            lastRebuildRealtime = Time.realtimeSinceStartup;
            if (adapting)
                lastAdaptationRebuildTime = Time.unscaledTime;

            RebuildMesh(cam, lat, lstDeg, jd, limitingMag, fogginess, adapt);
        }

        /// <summary>Rebuild decision (P7): camera movement rebuilds at once,
        /// while the time-driven drift (LST/latitude at ×32, moonwash, the
        /// adaptation ramp) is throttled to MinRebuildIntervalSeconds so the
        /// 5046-star mesh is not rebuilt ~1.3×/s per mesh.</summary>
        private bool NeedsRebuild(Camera cam, double lstDeg, float lat,
            double limitingMag, bool adapting)
        {
            if (Vector3.Distance(cam.transform.position, lastCamPos)
                    > MeshRebuildPolicy.MaxCameraMoveM
                || Quaternion.Angle(cam.transform.rotation, lastCamRot)
                    > MeshRebuildPolicy.MaxCameraRotationDeg)
                return true;

            bool timeDrift = Math.Abs(MeshRebuildPolicy.AngularDifferenceDeg(lstDeg, lastLstDeg))
                    > MeshRebuildPolicy.MaxLstDriftDeg
                || Math.Abs(lat - lastLat) > MeshRebuildPolicy.MaxLatitudeDriftDeg
                || double.IsNaN(lastLimitingMag)
                || Math.Abs(limitingMag - lastLimitingMag) > MaxLimitingDriftMag
                || adapting;
            return timeDrift && MeshRebuildPolicy.RebuildIntervalElapsed(
                lastRebuildRealtime, Time.realtimeSinceStartup);
        }

        /// <summary>Limiting V magnitude for the current sky. Delegates to the
        /// Unity-free StarSkyConditions helper so the point layer and the
        /// Sternspreiz streak field share one condition model.</summary>
        private static double ComputeLimitingMagnitude(DateTime utc, float lat, float lon)
        {
            return StarSkyConditions.LimitingMagnitude(utc, lat, lon);
        }

        private void RebuildMesh(Camera cam, float latDeg, double lstDeg, double jd,
            double limitingMag, float fogginess, float adapt)
        {
            vertices.Clear();
            triangles.Clear();
            colors.Clear();
            uvs.Clear();

            int visibleCount = 0;
            float safeRadius = CameraResolver.SafeRadius(cam);
            // StarBrightness reports a SCREEN size in pixels; the quad lives on
            // the sky shell, so the world size must come through the projection:
            // worldPerPixel = 2*R*tan(fov/2)/viewportHeight. Without this the
            // old "sizePx * 0.1" built quads of ~0.2-2 m at ~3825 m, i.e. ~0.05
            // screen pixels - the layer contributed no visible pixels at all
            // (measured live 2026-09-28, see docs/sternspreiz-lens-v1.md).
            float worldPerPixel = StarQuadScale.WorldUnitsPerPixel(
                safeRadius, cam.fieldOfView, cam.pixelHeight);

            foreach (var star in StarCatalog.Stars)
            {
                // Hip=0 is the no-HIP sentinel (17 rows): rendering it shows a
                // star that can never be identified (P3).
                if (star.Hip == 0)
                    continue;

                // Catalog epoch is 1926.0, not the 1942 its header claims:
                // precess every row to the sight date (P1b).
                CelestialEphemeris.PrecessCatalog(star.RaDeg, star.DecDeg, jd,
                    out double raRad, out double decRad);

                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                    out double altRad, out double azRad, out double e, out double n, out double u);

                double altDeg = altRad * 180.0 / Math.PI;
                if (altDeg <= AtmosphericExtinction.CullAltitudeDeg)
                    continue; // below the horizon (fade already zero at 0°)

                // INV-ALPHA (P4): vertex alpha is assembled ONCE here —
                // Pogson base × sky visibility (moonwash/twilight) × horizon
                // fade × eye adaptation. The material tint stays the only
                // global multiplier (GetVisibility).
                double hazeLoss = SkyBrightness.HazeLoss(altDeg, fogginess);
                float skyFactor = SkyBrightness.VisibilityFactor(limitingMag, star.Vmag, hazeLoss);
                if (skyFactor <= 0.002f)
                    continue; // below the limiting magnitude: no quad at all

                float horizonFade = AtmosphericExtinction.HorizonFade(altDeg);
                double vEff = star.Vmag + AtmosphericExtinction.MagnitudeLoss(altDeg);
                double bvEff = star.Bv + AtmosphericExtinction.BvShift(altDeg);

                // Convert ENU to world direction (Unity: X=east, Y=up, Z=north)
                Vector3 dir = new Vector3((float)e, (float)u, (float)n).normalized;
                Vector3 worldPos = cam.transform.position + dir * safeRadius;

                // Pogson photometry (P3): size and alpha from the flux law,
                // one navigation boost instead of the old stacked ×1.9/×1.5.
                StarBrightness.BrightnessFor((float)vEff, star.IsNav,
                    out float sizePx, out float alpha);
                alpha *= skyFactor * horizonFade * adapt;

                // Color from B-V index (reddened by extinction); alpha is assembled above.
                Color color = ColorFromBV((float)bvEff);
                color.a = alpha;

                // Billboard quad (4 vertices, 2 triangles)
                BillboardQuadBuilder.AddQuad(vertices, triangles, colors, uvs, worldPos, dir,
                    sizePx * worldPerPixel, color);
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
                Debug.Log($"[RadioNav][StarsMesh] Rendered {visibleCount}/{StarCatalog.Stars.Length} stars "
                    + $"pxScale={worldPerPixel.ToString("F2", CultureInfo.InvariantCulture)} "
                    + $"verts={vertices.Count} radius={safeRadius.ToString("F0", CultureInfo.InvariantCulture)} "
                    + $"fov={cam.fieldOfView.ToString("F1", CultureInfo.InvariantCulture)} "
                    + $"h={cam.pixelHeight} adapt={adapt.ToString("F2", CultureInfo.InvariantCulture)} "
                    + $"tint={(material != null ? material.color.a.ToString("F2", CultureInfo.InvariantCulture) : "0")} "
                    + $"shader={(material != null && material.shader != null ? material.shader.name : "none")}");
            }
        }

        /// <summary>B-V index → RGB (simplified Stellarium-like map). Alpha is
        /// NOT set here: it comes from StarBrightness (single source). The map
        /// itself lives in the Unity-free StarColor helper so the Sternspreiz
        /// streak field shares one source of truth.</summary>
        private static Color ColorFromBV(float bv)
        {
            StarColor.FromBv(bv, out float r, out float g, out float b);
            return new Color(r, g, b, 1f);
        }
    }
}
