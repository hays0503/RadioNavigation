using System;
using System.Globalization;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sternspreizglas (astigmatizer lens) for the sextant ocular.
    ///
    /// Physics (Celestaire instrument description, see docs/sternspreiz-lens-v1.md):
    /// the glass spreads a point source into a *horizontal line* and sits in the
    /// index (star) beam only, so the horizon is untouched and only stars are
    /// streaked. The line lies horizontal in the OBSERVER's frame, is centred on
    /// the star, and its length is angular (zoom independent).
    ///
    /// Rendering: one batched mesh of thin quads per visible star, drawn into
    /// each sextant PIP through the proven IOpaqueImageEffect callback (the same
    /// path the Phase-0 sentinel proved writes pixels). The star field comes
    /// from the mod's own catalog, not from the star mesh: no per-frame rebuild
    /// of the 5046-star mesh, no global blur, Main Camera untouched.
    ///
    /// PlayerPrefs RadioNav.SternspreizLens: 1 = lens swung in (default 0).
    /// PlayerPrefs RadioNav.SternspreizLengthDeg: angular line length (baseline
    /// value only - no published optical measurement exists). The across-line
    /// thickness is an independent angle, so making the line longer does not
    /// turn the historical short dash into a fat rhombus. The line also rocks
    /// very slightly (±0.25° over 8 s) as requested; its centre stays on the
    /// star.
    /// </summary>
    internal sealed class SternspreizLensEffect : MonoBehaviour, IOpaqueImageEffect, IDisposable
    {
        internal const string PrefsKey = "RadioNav.SternspreizLens";
        internal const string LengthPrefsKey = "RadioNav.SternspreizLengthDeg";
        internal const string ComponentName = "SternspreizLensEffect";
        internal const float DefaultLengthDeg = 1.5f;
        internal const float BaselineThicknessDeg = 0.189f;
        internal const float MinLengthDeg = 0.02f;
        internal const float MaxLengthDeg = 3f;
        internal const int MaxStars = 48;
        private const float LensOpacity = 0.52f;
        // uv 0.5 of the PIP target is the split between the horizon half and
        // the alidade half of the ocular (SextantOcularBuilder uvRects).
        private const float InteriorHalfSplit = 0.5f;
        // Guard radius for the "compact source" test, in extended-solar-disc
        // radii. Half the solar disc separates the two cases that matter
        // inside the boat: a bare bulb is far below it, an instrument glass
        // or a lit panel is well above it. Sizing it angularly keeps the
        // selectivity zoom-invariant (both the guard and the source grow
        // with the focal length).
        private const float CompactnessRadiusScale = 0.5f;
        // Interior line length/thickness relative to the star baseline. The
        // star line is sized for a point PSF; a lamp bulb is far larger, so
        // the same angular length would be swallowed by the source itself.
        // A real cylindrical lens draws a line whose length comes from the
        // aperture, not from the source, so the interior pass opens it up.
        private const float InteriorStreakLengthScale = 2.5f;
        private const float InteriorStreakThicknessScale = 1.5f;
        private const string InteriorShaderName = "RadioNav/SternspreizInteriorOptics";
        private const string InteriorMaterialAssetName = "RadioNavInteriorOpticsMaterial";
        private const string InteriorShaderAssetName = "RadioNavInteriorOptics";
        private static readonly int InteriorSceneTargetId = Shader.PropertyToID("_RadioNavInteriorScene");
        private static readonly int InteriorGlowTargetAId = Shader.PropertyToID("_RadioNavInteriorGlowA");
        private static readonly int InteriorGlowTargetBId = Shader.PropertyToID("_RadioNavInteriorGlowB");
        private static readonly int InteriorGlowTextureId = Shader.PropertyToID("_RadioNavGlowTex");
        private static readonly int InteriorStreakHalfLengthId = Shader.PropertyToID("_StreakHalfLengthTexels");
        private static readonly int InteriorStreakHalfThicknessId = Shader.PropertyToID("_StreakHalfThicknessTexels");
        private static readonly int InteriorStreakDirectionId = Shader.PropertyToID("_StreakDirection");
        private static readonly int InteriorCompactnessRadiusId = Shader.PropertyToID("_CompactnessRadiusPixels");

        private const string LogPrefix = "[RadioNav][Sternspreiz][Lens]";
        private const string MaterialAssetName = "RadioNavSternspreizStreak";
        private const string ShaderAssetName = "RadioNavStarField";
        private Camera pipCamera;
        private bool rightEye;
        private bool configured;
        private string setupError;
        private Mesh streakMesh;
        private Material streakMaterial;
        private Material interiorMaterial;
        private Texture2D profileTexture;
        private CommandBuffer commandBuffer;
        private IPlayerPositionProvider positionProvider;
        private bool interiorMaterialResolved;
        private float interiorLogTimer;

        private readonly LensStar[] stars = new LensStar[MaxStars];
        private float[] positions;
        private float[] colors;
        private float[] uvs;
        private int[] indices;
        private Vector3[] vertices;
        private Color[] vertexColors;
        private Vector2[] vertexUvs;

        private int lastStarCount;
        private int lastEmitted;
        private int lastRebuildFrame = -1;
        private Vector3 lastCamPos;
        private Quaternion lastCamRot;
        private double lastLstDeg = double.NaN;
        private float lastLat = float.NaN;
        private float lastRebuildRealtime;
        private float lastTiltRebuildRealtime = float.NegativeInfinity;
        private float lastTiltDeg = float.NaN;

        private int callbackCount;
        private int lastCallbackFrame = -1;
        private int drawCount;
        private bool lastDrew;
        private string lastMode = "off";
        private bool disposed;

        internal static bool Enabled
        {
            get { return PlayerPrefs.GetInt(PrefsKey, 0) == 1; }
        }

        internal static float LengthDeg
        {
            get
            {
                float value = PlayerPrefs.GetFloat(LengthPrefsKey, DefaultLengthDeg);
                if (value < MinLengthDeg)
                    value = MinLengthDeg;
                if (value > MaxLengthDeg)
                    value = MaxLengthDeg;
                return value;
            }
        }

        internal bool Configured
        {
            get { return configured; }
        }

        internal int LastStarCount
        {
            get { return lastStarCount; }
        }

        internal int LastEmitted
        {
            get { return lastEmitted; }
        }

        /// <summary>Creates the mesh, the material (bundle first, built-in
        /// fallback) and the baked line profile. Returns false and records
        /// setupError on failure; the caller treats that as a non-fatal miss.</summary>
        public bool ConfigureLens(bool isRightEye)
        {
            pipCamera = GetComponent<Camera>();
            if (pipCamera == null)
            {
                setupError = "PIP Camera missing";
                return false;
            }

            rightEye = isRightEye;
            positionProvider = new GamePlayerPositionProvider();
            EnsureBuffers();
            EnsureProfile();
            if (!EnsureMaterial())
                return false;
            if (streakMesh == null)
                streakMesh = new Mesh();
            streakMesh.name = rightEye
                ? "RadioNavSternspreizLens_Right"
                : "RadioNavSternspreizLens_Left";
            streakMesh.hideFlags = HideFlags.DontSave;
            streakMesh.MarkDynamic();

            setupError = null;
            configured = true;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} CONFIG pip={1} material={2} lengthDeg={3:F2} maxStars={4}",
                LogPrefix, pipCamera.name, streakMaterial != null ? streakMaterial.shader.name : "none",
                LengthDeg, MaxStars));
            return true;
        }

        /// <summary>Opaque image-effect stage: pass the scene image through and
        /// add the streaks on top. Disabled lens is a true no-op (no blit, no
        /// draw): the normal pipeline owns the image.</summary>
        public void RenderImageOpaque(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer, int width, int height,
            RenderTargetIdentifier upscalingBiasMask, CullingResults cullResults)
        {
            callbackCount++;
            lastCallbackFrame = Time.frameCount;
            lastDrew = false;
            lastMode = "off";

            if (!Enabled || !configured || !rightEye || disposed || !SextantController.IsOpen)
                return;

            if (CanSightSky())
            {
                RenderStarField(context, source, destination, zBuffer);
                return;
            }

            if (IsPlayerInsideBoat())
                RenderInteriorOptics(context, source, destination, width, height);
        }

        private void RenderStarField(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer)
        {
            if (streakMesh == null)
                return;
            if (ShouldRebuild())
                RebuildField();
            else
            {
                float tilt = SternspreizLensTilt.AngleDeg(Time.unscaledTime);
                if (SternspreizLensTilt.ShouldUpdate(lastTiltDeg, tilt,
                        lastTiltRebuildRealtime, Time.realtimeSinceStartup))
                    RebuildStreakMesh(tilt);
            }
            if (lastEmitted <= 0)
                return;

            DrawStreaks(context, source, destination, zBuffer);
        }

        private static bool CanSightSky()
        {
            return SkyVisibilityResolver.IsPlayerOutside();
        }

        private static bool IsPlayerInsideBoat()
        {
            return MainCamera.Instance?.IsInsideBoat == true;
        }

        private bool ShouldRebuild()
        {
            if (lastRebuildFrame < 0)
                return true;
            if (!positionProvider.TryGetPosition(out float lat, out _))
                return false;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double lstDeg = AstroMath.LstDeg(AstroMath.ToJulianDay(utc), 0d);
            if (!MeshRebuildPolicy.NeedsRebuild(pipCamera.transform.position, lastCamPos,
                    pipCamera.transform.rotation, lastCamRot, lstDeg, lastLstDeg, lat, lastLat,
                    true))
                return false;
            return MeshRebuildPolicy.RebuildIntervalElapsed(
                lastRebuildRealtime, Time.realtimeSinceStartup);
        }

        private void RebuildField()
        {
            if (!positionProvider.TryGetPosition(out float lat, out float lon))
                return;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);
            float fogginess = SkyVisibilityResolver.GetFogginess();
            double limitingMag = StarSkyConditions.LimitingMagnitude(utc, lat, lon);
            double adaptation = StarsMesh.CurrentAdaptation;

            lastStarCount = SternspreizLensField.CollectVisibleStars(lat, lstDeg, jd,
                (float)limitingMag, fogginess, adaptation, MaxStars, stars);

            RebuildStreakMesh(SternspreizLensTilt.AngleDeg(Time.unscaledTime));

            lastRebuildFrame = Time.frameCount;
            lastCamPos = pipCamera.transform.position;
            lastCamRot = pipCamera.transform.rotation;
            lastLstDeg = lstDeg;
            lastLat = lat;
            lastRebuildRealtime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Re-emits the capped streak mesh in the live instrument frame. When
        /// only the small roll changes, the already-collected star set is
        /// reused; the 5046-entry catalog is not rescanned for the wobble.
        /// </summary>
        private void RebuildStreakMesh(float tiltDeg)
        {
            LensCamera camera = SternspreizLensTilt.ApplyRoll(BuildCamera(), tiltDeg);
            LensOptics optics = new LensOptics
            {
                LineLengthDeg = LengthDeg,
                ThicknessDeg = BaselineThicknessDeg,
                DistanceM = CameraResolver.SafeRadius(pipCamera)
            };
            lastEmitted = SternspreizLensField.BuildStreakMesh(stars, lastStarCount,
                camera, optics, positions, colors, uvs, indices);
            ApplySkyFade();
            UploadMesh();
            lastTiltDeg = tiltDeg;
            lastTiltRebuildRealtime = Time.realtimeSinceStartup;
        }

        private LensCamera BuildCamera()
        {
            Transform t = pipCamera.transform;
            return new LensCamera
            {
                Px = t.position.x, Py = t.position.y, Pz = t.position.z,
                Rx = t.right.x, Ry = t.right.y, Rz = t.right.z,
                Ux = t.up.x, Uy = t.up.y, Uz = t.up.z,
                Fx = t.forward.x, Fy = t.forward.y, Fz = t.forward.z,
                TanHalfFovY = Mathf.Tan(pipCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
            };
        }

        /// <summary>The point-star layer tints its whole material by the global
        /// sky visibility (moonwash/twilight/fog); the streak layer applies the
        /// same factor so lines and points rise and fall together.</summary>
        private void ApplySkyFade()
        {
            float fade = SkyVisibilityResolver.GetVisibility();
            if (fade >= 1f)
                return;
            int count = lastEmitted * SternspreizLensField.VerticesPerStreak;
            for (int v = 0; v < count; v++)
                colors[v * 4 + 3] *= fade;
        }

        private void UploadMesh()
        {
            int vertexCount = lastEmitted * SternspreizLensField.VerticesPerStreak;
            if (streakMesh == null)
                return;

            if (vertexCount == 0)
            {
                streakMesh.Clear();
                return;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                vertexColors[i] = new Color(colors[i * 4], colors[i * 4 + 1], colors[i * 4 + 2], colors[i * 4 + 3]);
                vertexUvs[i] = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            }

            streakMesh.Clear();
            streakMesh.SetVertices(vertices, 0, vertexCount);
            streakMesh.SetColors(vertexColors, 0, vertexCount);
            streakMesh.SetUVs(0, vertexUvs, 0, vertexCount);
            streakMesh.SetTriangles(indices, 0, lastEmitted * SternspreizLensField.IndicesPerStreak, 0);
            streakMesh.RecalculateBounds();
        }

        private void DrawStreaks(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer)
        {
            if (commandBuffer == null)
            {
                commandBuffer = new CommandBuffer();
                commandBuffer.name = "RadioNav Sternspreiz Lens";
            }
            commandBuffer.Clear();
            if (!source.Equals(destination))
                commandBuffer.Blit(source, destination);
            commandBuffer.SetRenderTarget(destination, zBuffer);
            commandBuffer.SetViewProjectionMatrices(
                pipCamera.worldToCameraMatrix, pipCamera.projectionMatrix);
            commandBuffer.DrawMesh(streakMesh, Matrix4x4.identity, streakMaterial, 0, 0);
            context.ExecuteCommandBuffer(commandBuffer);

            drawCount++;
            lastDrew = true;
            lastMode = "outside";
        }

        private void RenderInteriorOptics(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            int width, int height)
        {
            if (width <= 0 || height <= 0 || !EnsureInteriorMaterial())
                return;

            if (commandBuffer == null)
            {
                commandBuffer = new CommandBuffer();
                commandBuffer.name = "RadioNav Sternspreiz Interior Optics";
            }

            int glowWidth = Mathf.Max(1, width / 2);
            int glowHeight = Mathf.Max(1, height / 2);
            UpdateInteriorProjection(width, glowWidth);
            commandBuffer.Clear();
            commandBuffer.GetTemporaryRT(InteriorGlowTargetAId, glowWidth, glowHeight, 0,
                FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            commandBuffer.GetTemporaryRT(InteriorGlowTargetBId, glowWidth, glowHeight, 0,
                FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

            // Usually the pipeline already gives us distinct source/destination
            // targets. Copy only for an aliased callback; all glow work remains
            // half resolution so the interior effect stays bounded.
            bool sourceAliasesDestination = source.Equals(destination);
            RenderTargetIdentifier sceneSource = source;
            if (sourceAliasesDestination)
            {
                commandBuffer.GetTemporaryRT(InteriorSceneTargetId, width, height, 0,
                    FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                commandBuffer.Blit(source, new RenderTargetIdentifier(InteriorSceneTargetId));
                sceneSource = new RenderTargetIdentifier(InteriorSceneTargetId);
            }

            commandBuffer.Blit(sceneSource,
                new RenderTargetIdentifier(InteriorGlowTargetAId), interiorMaterial, 0);
            commandBuffer.Blit(new RenderTargetIdentifier(InteriorGlowTargetAId),
                new RenderTargetIdentifier(InteriorGlowTargetBId), interiorMaterial, 1);
            commandBuffer.Blit(new RenderTargetIdentifier(InteriorGlowTargetBId),
                new RenderTargetIdentifier(InteriorGlowTargetAId), interiorMaterial, 2);
            commandBuffer.SetGlobalTexture(InteriorGlowTextureId,
                new RenderTargetIdentifier(InteriorGlowTargetAId));
            // Scoped to the alidade half by scissor, not by which camera the
            // callback arrived on: the RawImage that shows uv 0.5..1 covers
            // exactly this half of the target, so the horizon half stays
            // byte-identical even when the SRP hands both PIP cameras the
            // same colour buffer.
            commandBuffer.EnableScissorRect(
                new Rect(width * InteriorHalfSplit, 0f, width * (1f - InteriorHalfSplit), height));
            commandBuffer.Blit(sceneSource, destination, interiorMaterial, 3);
            commandBuffer.DisableScissorRect();
            commandBuffer.SetGlobalTexture(InteriorGlowTextureId, Texture2D.blackTexture);
            commandBuffer.ReleaseTemporaryRT(InteriorGlowTargetBId);
            commandBuffer.ReleaseTemporaryRT(InteriorGlowTargetAId);
            if (sourceAliasesDestination)
                commandBuffer.ReleaseTemporaryRT(InteriorSceneTargetId);
            context.ExecuteCommandBuffer(commandBuffer);

            drawCount++;
            lastDrew = true;
            lastMode = "inside";
        }

        private void UpdateInteriorProjection(int targetWidth, int glowWidth)
        {
            if (interiorMaterial == null || pipCamera == null || targetWidth <= 0)
                return;

            double linePixels = SternspreizLineProfile.PixelsForAngle(
                LengthDeg, pipCamera.fieldOfView, targetWidth);
            double thicknessPixels = SternspreizLineProfile.PixelsForAngle(
                BaselineThicknessDeg, pipCamera.fieldOfView, targetWidth);
            // Guard radius for "is this a point source?". A lamp is a few
            // pixels across, an instrument glass or a lit panel is wider than
            // the extended solar disc, so the guard is sized from that disc
            // and stays angularly correct at every zoom.
            double extendedDiscPixels = SternspreizLineProfile.PixelsForAngle(
                SunDiscRenderer.AngularDiameterDeg, pipCamera.fieldOfView, targetWidth);
            float halfResolutionScale = glowWidth / (float)targetWidth * 0.5f;
            float halfLengthTexels = Mathf.Max(
                0.5f, (float)linePixels * InteriorStreakLengthScale * halfResolutionScale);
            float halfThicknessTexels = Mathf.Max(
                0.5f, (float)thicknessPixels * InteriorStreakThicknessScale * halfResolutionScale);
            float compactnessRadiusPixels =
                Mathf.Max(4f, (float)extendedDiscPixels * CompactnessRadiusScale);

            float tiltRadians = SternspreizLensTilt.AngleDeg(Time.unscaledTime) * Mathf.Deg2Rad;
            Vector4 direction = new Vector4(Mathf.Cos(tiltRadians), Mathf.Sin(tiltRadians), 0f, 0f);
            SetFloatIfPresent(interiorMaterial, InteriorStreakHalfLengthId, halfLengthTexels);
            SetFloatIfPresent(interiorMaterial, InteriorStreakHalfThicknessId, halfThicknessTexels);
            SetFloatIfPresent(interiorMaterial, InteriorCompactnessRadiusId, compactnessRadiusPixels);
            if (interiorMaterial?.HasProperty(InteriorStreakDirectionId) == true)
                interiorMaterial.SetVector(InteriorStreakDirectionId, direction);

            interiorLogTimer -= Time.unscaledDeltaTime;
            if (interiorLogTimer > 0f)
                return;
            interiorLogTimer = 1f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} INTERIOROPTICS rt={1}x{2} fov={3:F1} line={4:F2}px halfLen={5:F2}tex "
                + "halfThick={6:F2}tex guard={7:F1}px dir={8:F3},{9:F3}",
                LogPrefix, targetWidth, targetWidth, pipCamera.fieldOfView,
                linePixels * InteriorStreakLengthScale, halfLengthTexels, halfThicknessTexels,
                compactnessRadiusPixels, direction.x, direction.y));
        }

        private void EnsureBuffers()
        {
            if (positions == null)
            {
                positions = new float[SternspreizLensField.PositionFloatCount(MaxStars)];
                colors = new float[SternspreizLensField.ColorFloatCount(MaxStars)];
                uvs = new float[SternspreizLensField.UvFloatCount(MaxStars)];
                indices = new int[SternspreizLensField.IndexCount(MaxStars)];
                vertices = new Vector3[MaxStars * SternspreizLensField.VerticesPerStreak];
                vertexColors = new Color[MaxStars * SternspreizLensField.VerticesPerStreak];
                vertexUvs = new Vector2[MaxStars * SternspreizLensField.VerticesPerStreak];
            }
        }

        private void EnsureProfile()
        {
            if (profileTexture != null)
                return;
            // 96 columns (DefaultSamples) keep the sharp tips' staircase small; the
            // byte normalisation stays value * columns * 255 so on-screen brightness
            // is unchanged when the column count changes.
            int columns = SternspreizLineProfile.DefaultSamples;
            int rows = SternspreizLineProfile.DefaultStreakRows;
            float[] map = SternspreizLineProfile.BuildStreakAlphaMap(columns, rows,
                SternspreizLineProfile.DefaultEdgeFraction,
                SternspreizLineProfile.DefaultWidthTaper,
                SternspreizLineProfile.DefaultMinWidth,
                SternspreizLineProfile.DefaultCoreFraction);
            var pixels = new Color32[map.Length];
            for (int i = 0; i < map.Length; i++)
            {
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(map[i] * columns * 255f), 0, 255);
                pixels[i] = new Color32(255, 255, 255, a);
            }

            profileTexture = new Texture2D(columns, rows, TextureFormat.RGBA32, false);
            profileTexture.name = "RadioNavSternspreizProfile";
            profileTexture.wrapMode = TextureWrapMode.Clamp;
            profileTexture.filterMode = FilterMode.Bilinear;
            profileTexture.hideFlags = HideFlags.DontSave;
            profileTexture.SetPixels32(pixels);
            profileTexture.Apply();
        }

        /// <summary>Bundle material РІвЂ вЂ™ bundle shader РІвЂ вЂ™ built-in sprite fallback,
        /// the same ladder SextantOcularBuilder uses (F229: the ResourceManager
        /// cannot see mod assets).</summary>
        private bool EnsureMaterial()
        {
            if (streakMaterial != null)
                return true;

            Material fromBundle = ModBootstrapper.LoadBundleAsset(MaterialAssetName) as Material;
            if (fromBundle != null)
                streakMaterial = new Material(fromBundle);
            else
            {
                Shader shader = ModBootstrapper.LoadBundleAsset(ShaderAssetName) as Shader
                    ?? Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    setupError = "no lens shader available";
                    Debug.LogWarning(LogPrefix + " " + setupError);
                    return false;
                }
                streakMaterial = new Material(shader);
            }

            streakMaterial.name = MaterialAssetName + (rightEye ? " (right)" : " (left)");
            streakMaterial.hideFlags = HideFlags.DontSave;
            SetProfileTexture();
            // This is a cloned, lens-owned material. Use conventional source-
            // over alpha blending here without changing the additive PointSky
            // material used by the celestial renderer.
            SetFloatIfPresent("_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent("_ZWrite", 0f);
            // DrawMesh explicitly selects pass 0 (Point). Its 2-D baked
            // profile owns softness/taper; these uniforms belong to the
            // unused reference Streak pass and would be a silent no-op here.
            SetColorIfPresent("_Color", new Color(1f, 1f, 1f, LensOpacity));
            return true;
        }

        private bool EnsureInteriorMaterial()
        {
            if (interiorMaterial != null)
                return true;
            if (interiorMaterialResolved)
                return false;
            interiorMaterialResolved = true;

            Material bundled = ModBootstrapper.LoadBundleAsset(InteriorMaterialAssetName) as Material;
            if (bundled != null)
                interiorMaterial = new Material(bundled);
            else
            {
                Shader shader = ModBootstrapper.LoadBundleAsset(InteriorShaderAssetName) as Shader
                    ?? Shader.Find(InteriorShaderName);
                if (shader == null)
                {
                    Debug.LogWarning(LogPrefix + " interior optics shader unavailable; interior lens is a no-op");
                    return false;
                }
                interiorMaterial = new Material(shader);
            }

            interiorMaterial.name = InteriorShaderAssetName + " (right PIP)";
            interiorMaterial.hideFlags = HideFlags.DontSave;
            SetFloatIfPresent(interiorMaterial, "_BrightThreshold", 0.62f);
            SetFloatIfPresent(interiorMaterial, "_BrightKnee", 0.24f);
            SetFloatIfPresent(interiorMaterial, "_CompactnessStart", 0.04f);
            SetFloatIfPresent(interiorMaterial, "_CompactnessEnd", 0.24f);
            SetFloatIfPresent(interiorMaterial, "_CompactnessRadiusPixels", 16f);
            SetFloatIfPresent(interiorMaterial, "_SceneBlur", 0.12f);
            SetFloatIfPresent(interiorMaterial, "_GlowIntensity", 4f);
            SetFloatIfPresent(interiorMaterial, "_GlowClamp", 0.2f);
            SetFloatIfPresent(interiorMaterial, "_StreakEdgeFraction", 0.18f);
            return true;
        }

        private void SetProfileTexture()
        {
            if (streakMaterial == null || profileTexture == null)
                return;
            if (streakMaterial.HasProperty("_MainTex"))
                streakMaterial.mainTexture = profileTexture;
        }

        private void SetColorIfPresent(string property, Color value)
        {
            if (streakMaterial == null)
                return;
            if (!streakMaterial.HasProperty(property))
                return;
            streakMaterial.SetColor(property, value);
        }

        private void SetFloatIfPresent(string property, float value)
        {
            SetFloatIfPresent(streakMaterial, property, value);
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material?.HasProperty(property) != true)
                return;
            material.SetFloat(property, value);
        }

        private static void SetFloatIfPresent(Material material, int property, float value)
        {
            if (material?.HasProperty(property) != true)
                return;
            material.SetFloat(property, value);
        }

        /// <summary>JSON for the existing /sextant snapshot.</summary>
        internal string SnapshotJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"enabled\":{0},\"configured\":{1},\"material\":\"{2}\",\"mode\":\"{3}\",\"lengthDeg\":{4},\"thicknessDeg\":{5},\"tiltDeg\":{6},"
                + "\"stars\":{7},\"emitted\":{8},\"draws\":{9},\"drew\":{10},\"rebuildFrame\":{11},"
                + "\"callbackCount\":{12},\"lastFrame\":{13},\"setupError\":\"{14}\"}}",
                Enabled ? "true" : "false", configured ? "true" : "false",
                streakMaterial != null ? RadioNavProbeJson.Escape(streakMaterial.shader.name) : string.Empty,
                RadioNavProbeJson.Escape(lastMode),
                LengthDeg.ToString("F3", CultureInfo.InvariantCulture),
                BaselineThicknessDeg.ToString("F3", CultureInfo.InvariantCulture),
                (float.IsNaN(lastTiltDeg) || float.IsInfinity(lastTiltDeg) ? 0f : lastTiltDeg)
                    .ToString("F3", CultureInfo.InvariantCulture),
                lastStarCount, lastEmitted, drawCount, lastDrew ? "true" : "false",
                lastRebuildFrame, callbackCount, lastCallbackFrame,
                RadioNavProbeJson.Escape(setupError ?? string.Empty));
        }

        public void OnDestroy()
        {
            Dispose();
        }

        /// <summary>Called by the factory when the lens is suspended (close or
        /// preference off) so the last "drew" flag does not keep reporting a
        /// stale draw while the callback is disabled.</summary>
        internal void NotifySuspended()
        {
            lastDrew = false;
            lastMode = "off";
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (commandBuffer != null)
            {
                commandBuffer.Dispose();
                commandBuffer = null;
            }
            if (streakMesh != null)
            {
                UnityEngine.Object.Destroy(streakMesh);
                streakMesh = null;
            }
            if (streakMaterial != null)
            {
                UnityEngine.Object.Destroy(streakMaterial);
                streakMaterial = null;
            }
            if (interiorMaterial != null)
            {
                UnityEngine.Object.Destroy(interiorMaterial);
                interiorMaterial = null;
            }
            if (profileTexture != null)
            {
                UnityEngine.Object.Destroy(profileTexture);
                profileTexture = null;
            }
            positionProvider = null;
            configured = false;
        }
    }
}
