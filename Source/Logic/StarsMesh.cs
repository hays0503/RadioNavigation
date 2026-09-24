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
                // Gaussian PSF sprite: without a texture the quads rendered as
                // solid squares (P3). Straight white + SrcAlpha/One blend.
                spriteTexture = BuildSpriteTexture();
                material.mainTexture = spriteTexture;
                meshRenderer.material = material;
            }
            else
            {
                Debug.LogWarning("[RadioNav][StarsMesh] No suitable shader found, using default");
            }

            Debug.Log("[RadioNav][StarsMesh] Initialized with " + StarCatalog.Stars.Length + " stars");
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

        /// <summary>Limiting V magnitude for the current sky. The Sun's zenith
        /// distance comes from the MOD's own ephemeris, not the game's
        /// TOD_Sky.SunZenith: live 2026-09-22 the reflected member returned a
        /// stale daytime value at night (74° instead of ~140°), which drove the
        /// twilight shift to daylight and hid almost the whole field. Our sun
        /// matches the game sun to 0.001°, so this is both safer and equal.</summary>
        private static double ComputeLimitingMagnitude(DateTime utc, float lat, float lon)
        {
            double sunZenith = CelestialForecast.SunZenithDeg(lat, lon, utc);
            if (double.IsNaN(sunZenith))
                sunZenith = 108.0; // fail dark: never wash the field out

            double moonJd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double moonAltDeg = 0.0;
            if (AstroMath.MoonRaDec(moonJd, out double moonRa, out double moonDec))
            {
                AstroMath.RaDecToEnu(moonRa, moonDec, AstroMath.LstDeg(moonJd, lon), lat,
                    out double altRad, out _, out _, out _, out _);
                moonAltDeg = altRad * 180.0 / Math.PI;
            }
            double illumination = AstroMath.MoonIllumination(moonJd);
            return SkyBrightness.LimitingMagnitude(sunZenith, illumination, moonAltDeg);
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
                BillboardQuadBuilder.AddQuad(vertices, triangles, colors, uvs, worldPos, dir, sizePx * 0.1f, color);
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

        /// <summary>B-V index → RGB (simplified Stellarium-like map). Alpha is
        /// NOT set here: it comes from StarBrightness (single source).</summary>
        private static Color ColorFromBV(float bv)
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

            return new Color(r, g, b, 1f);
        }
    }
}
