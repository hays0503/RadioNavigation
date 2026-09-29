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
        // NOTE: no local WorldRadiusM — the single source of truth lives in
        // CameraResolver (SafeRadius); the dead duplicate was removed (iter6).
        private const float LineWidthArcMin = 2f; // angular width in arcminutes
        private static readonly Color LineColor = new Color(0.55f, 0.22f, 0.15f, 0.4f); // reddish-brown

        /// <summary>Minimum on-screen line width (P7): the 2′ angular width is
        /// sub-pixel in the 512² ocular at ×1.</summary>
        private const float MinLinePixels = 1.5f;

        /// <summary>Altitude above which a line vertex reaches full alpha (P7).</summary>
        private const double HorizonFadeDeg = 3.0;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Material material;
        private IPlayerPositionProvider positionProvider;

        private Vector3 lastCamPos;
        private double lastLstDeg;
        private float lastLat;
        private ConstellationViewKey lastViewKey;
        private bool hasViewKey;
        private float lastLogTime;
        private float lastRebuildRealtime;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        // RebuildMesh scratch: one polyline at a time, reused across
        // rebuilds — no per-polyline List alloc (class contract above).
        private readonly List<Vector3> worldPointsScratch = new List<Vector3>(64);
        private readonly List<float> altitudeScratch = new List<float>(64);
        private Vector3 rebuildCamPos;

        /// <summary>Base key of the constellation toggle combo; effective binding is Shift+Z (SEXTANT_PLAN v1.2 — plain Z opens the sextant). Game occupies F4-F10/F11; Z verified free (source grep + 80-axis dump).</summary>
        private const KeyCode ToggleKey = KeyCode.Z;

        /// <summary>Current layer state, persisted via ConstellationTogglePrefs. Default ON.</summary>
        public static bool Enabled { get; private set; } = ConstellationTogglePrefs.Load();

        public static void SetEnabled(bool value)
        {
            if (Enabled == value)
                return;

            Enabled = value;
            ConstellationTogglePrefs.Save(value);
            Debug.Log("[RadioNav][ConstellationMesh] constellations " + (value ? "ON" : "OFF"));
        }

        private void Update()
        {
            // SEXTANT_PLAN v1.2: constellations moved to Shift+Z (the game queues
            // an action on bare Ctrl), plain Z belongs to the sextant; while the
            // sextant overlay is open stars must not toggle mid-shot.
            if (Input.GetKeyDown(ToggleKey)
                && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                && !SextantController.IsOpen)
                SetEnabled(!Enabled);
        }

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            positionProvider = new GamePlayerPositionProvider();

            // Celestial shader ladder (bundled -> Sprites/Default -> legacy
            // additive). The bundled shader carries the Sky-stage pass; without
            // it the Deep pipeline draws no scene pixels (the old Sprites/Default
            // material had no matching pass) and the UniStorm clouds could not
            // composite over the lines. Blend values are the additive ones this
            // renderer always intended (Sprites/Default silently hard-coded
            // One/OneMinusSrcAlpha, so they used to be inert).
            Shader shader = CelestialShaderResolver.ResolveShader();
            if (shader != null)
            {
                material = new Material(shader);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_ZWrite", 0);
                meshRenderer.material = material;
                bundledShaderResolved = CelestialShaderResolver.IsBundled(shader);
                SkyRenderQueuePolicy.ApplySkyStage(material, bundledShaderResolved);
            }

            Debug.Log("[RadioNav][ConstellationMesh] Initialized with " + ConstellationLines.Polylines.Length + " line segments");
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
                Debug.Log("[RadioNav][ConstellationMesh] celestial shader resolved to bundled " + bundled.name);
                return;
            }
            shaderUpgradeCountdown--;
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
            TryUpgradeToBundledShader();

            Camera cam;
            if (!CameraResolver.TryResolve(out cam))
                return;

            // User toggle (F9 / console / panel button) gates the whole layer.
            if (!Enabled)
            {
                if (meshRenderer.enabled)
                    meshRenderer.enabled = false;
                return;
            }

            // Constellation lines follow the star sky: only outside the boat.
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

            // Ocular-aware width: capture the view key (open edge, zoom step,
            // UI scale) and rebuild immediately when it changes, so the line
            // width follows the view the player is actually looking at.
            ConstellationViewKey key = ConstellationWidthPolicy.Capture(
                SextantController.IsOpen, SextantController.CurrentFovDivisor, SextantController.UiScale());
            bool keyChanged = ConstellationWidthPolicy.KeyChanged(key, lastViewKey);

            // Rebuild mesh only if camera/position/LST changed significantly
            bool cameraMoved = Vector3.Distance(cam.transform.position, lastCamPos)
                > MeshRebuildPolicy.MaxCameraMoveM;
            bool timeDrift = Math.Abs(MeshRebuildPolicy.AngularDifferenceDeg(lstDeg, lastLstDeg))
                    > MeshRebuildPolicy.MaxLstDriftDeg
                || Math.Abs(lat - lastLat) > MeshRebuildPolicy.MaxLatitudeDriftDeg;
            bool needsRebuild = !hasViewKey || ConstellationWidthPolicy.NeedsRebuild(
                cameraMoved, keyChanged, timeDrift,
                MeshRebuildPolicy.RebuildIntervalElapsed(lastRebuildRealtime, Time.realtimeSinceStartup));

            if (!needsRebuild && meshFilter.mesh != null && meshFilter.mesh.vertexCount > 0)
                return;

            lastCamPos = cam.transform.position;
            lastLstDeg = lstDeg;
            lastLat = lat;
            lastViewKey = key;
            hasViewKey = true;
            lastRebuildRealtime = Time.realtimeSinceStartup;

            RebuildMesh(cam, lat, lstDeg, jd);
        }

        private void RebuildMesh(Camera cam, float latDeg, double lstDeg, double jd)
        {
            vertices.Clear();
            triangles.Clear();
            colors.Clear();

            int visibleSegments = 0;
            float safeRadius = CameraResolver.SafeRadius(cam);
            rebuildCamPos = cam.transform.position;
            // The width follows the view the player is looking at: ocular FOV
            // (main FOV / zoom divisor) + 600-unit on-screen height x uiScale
            // while the sextant is open, the main camera otherwise.
            ConstellationWidthPolicy.WidthInputs(SextantController.IsOpen, cam.fieldOfView,
                SextantController.CurrentFovDivisor, SextantController.UiScale(), cam.pixelHeight,
                SextantController.OcularSizeUnits(),
                out double fovRad, out double screenPixels);
            float lineWidthRad = LineWidthArcMin / 60f * Mathf.Deg2Rad; // convert arcmin to radians
            // Minimum on-screen width (P7): at 2′ the line is ~0.26 px in the
            // 512² ocular at ×1 — invisible without this floor.
            float lineWidth = ConstellationLineMath.WorldWidth(lineWidthRad, safeRadius,
                fovRad, screenPixels, MinLinePixels);

            var points = worldPointsScratch;
            var alts = altitudeScratch;

            foreach (var polyline in ConstellationLines.Polylines)
            {
                if (polyline.Length < 2) continue;

                // Convert all points to world positions, keeping the altitude
                // so the segments can be CLIPPED at the horizon instead of
                // joined across it (P7).
                points.Clear();
                alts.Clear();
                for (int i = 0; i < polyline.Length; i++)
                {
                    int starIdx = polyline[i];
                    if (starIdx < 0 || starIdx >= StarCatalog.Stars.Length) continue;

                    var star = StarCatalog.Stars[starIdx];
                    // Precess the catalog row to the sight date (P1b).
                    CelestialEphemeris.PrecessCatalog(star.RaDeg, star.DecDeg, jd,
                        out double raRad, out double decRad);

                    AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                        out double altRad, out _, out double e, out double n, out double u);

                    Vector3 dir = new Vector3((float)e, (float)u, (float)n).normalized;
                    Vector3 worldPos = cam.transform.position + dir * safeRadius;
                    points.Add(worldPos);
                    alts.Add((float)(altRad * Mathf.Rad2Deg));
                }

                for (int i = 0; i < points.Count - 1; i++)
                {
                    if (!ConstellationLineMath.ClipToHorizon(alts[i], alts[i + 1],
                            out double t0, out double t1))
                        continue; // the whole segment is below the horizon
                    if (t1 - t0 < 1e-4)
                        continue;

                    Vector3 a = Vector3.Lerp(points[i], points[i + 1], (float)t0);
                    Vector3 b = Vector3.Lerp(points[i], points[i + 1], (float)t1);
                    float altA = (float)(alts[i] + (alts[i + 1] - alts[i]) * t0);
                    float altB = (float)(alts[i] + (alts[i + 1] - alts[i]) * t1);
                    AddLineSegment(a, b, lineWidth,
                        ConstellationLineMath.HorizonFade(altA, HorizonFadeDeg),
                        ConstellationLineMath.HorizonFade(altB, HorizonFadeDeg));
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

            if (visibleSegments > 0 && Time.time - lastLogTime > 5f)
            {
                lastLogTime = Time.time;
                Debug.Log($"[RadioNav][ConstellationMesh] Rendered {visibleSegments} line segments");
            }
        }

        private void AddLineSegment(Vector3 p0, Vector3 p1, float width,
            float alpha0, float alpha1)
        {
            // Create quad between p0 and p1 with given width. The perpendicular
            // is built against the VIEW direction (p0.normalized included the
            // camera's world offset — P7).
            Vector3 direction = (p1 - p0).normalized;
            Vector3 viewDir = (p0 - rebuildCamPos).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, viewDir).normalized;
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

            // Per-vertex horizon fade: each end carries its own alpha.
            Color c0 = LineColor;
            c0.a *= alpha0;
            Color c1 = LineColor;
            c1.a *= alpha1;
            colors.Add(c0);
            colors.Add(c0);
            colors.Add(c1);
            colors.Add(c1);
        }
    }
}
