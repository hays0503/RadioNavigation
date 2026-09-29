using System;
using System.Globalization;
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

        private const string LogPrefix = "[RadioNav][Sternspreiz][Lens]";
        private const string MaterialAssetName = "RadioNavSternspreizStreak";
        private const string ShaderAssetName = "RadioNavStarField";
        private Camera pipCamera;
        private bool rightEye;
        private bool configured;
        private string setupError;
        private Mesh streakMesh;
        private Material streakMaterial;
        private Texture2D profileTexture;
        private CommandBuffer commandBuffer;
        private IPlayerPositionProvider positionProvider;

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

            if (!Enabled || !configured || streakMesh == null || disposed)
                return;
            if (!CanSightSky())
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
            if (!SkyVisibilityResolver.IsPlayerOutside())
                return false;
            return SextantController.IsOpen;
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
            // DrawMesh explicitly selects pass 0 (Point). Its 2-D baked
            // profile owns softness/taper; these uniforms belong to the
            // unused reference Streak pass and would be a silent no-op here.
            SetColorIfPresent("_Color", Color.white);
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

        /// <summary>JSON for the existing /sextant snapshot.</summary>
        internal string SnapshotJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"enabled\":{0},\"configured\":{1},\"material\":\"{2}\",\"lengthDeg\":{3},\"thicknessDeg\":{4},\"tiltDeg\":{5},"
                + "\"stars\":{6},\"emitted\":{7},\"draws\":{8},\"drew\":{9},\"rebuildFrame\":{10},"
                + "\"callbackCount\":{11},\"lastFrame\":{12},\"setupError\":\"{13}\"}}",
                Enabled ? "true" : "false", configured ? "true" : "false",
                streakMaterial != null ? RadioNavProbeJson.Escape(streakMaterial.shader.name) : string.Empty,
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
