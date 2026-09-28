using System;
using System.Globalization;
using UBOAT.Game.Scene.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Phase-0 experiment only. This is deliberately not the Sternspreiz
    /// production effect: it checks whether UBOAT invokes IOpaqueImageEffect
    /// on each sextant PIP, whether the callback can blit/draw, and whether
    /// the supplied depth target occludes a test draw. No mode applies a PSF;
    /// mode 3 only suppresses the existing star renderer during each PIP pass.
    /// Mode 4 additionally redraws that same mesh with a flat diagnostic tint.
    ///
    /// PlayerPrefs RadioNav.SternspreizProbe: 0=off, 1=source visible,
    /// 2=fixture source culled and sentinel mesh custom-drawn,
    /// 3=actual StarSkyRoot renderer suppressed in each PIP (pass-through only),
    /// 4=actual StarSkyRoot renderer suppressed and redrawn tinted (Phase 1b).
    /// Default is off. Modes 3/4 never draw or process any other celestial content.
    /// </summary>
    internal sealed class SternspreizPipProbeEffect : MonoBehaviour, IOpaqueImageEffect, IDisposable
    {
        internal const string PrefsKey = "RadioNav.SternspreizProbe";
        internal const int SourceVisibleMode = 1;
        internal const int CustomDrawMode = 2;
        internal const int StarSourceSuppressionMode = 3;
        internal const int StarRedrawMode = 4;
        internal const string ComponentName = "SternspreizPipProbeEffect";

        private const string LogPrefix = "[RadioNav][Sternspreiz][Phase0]";
        private const int VisibleDrawPass = 0;
        private const int DepthDrawPass = 0;

        private Camera pipCamera;
        private Transform sentinelTransform;
        private Transform depthOccluderTransform;
        private Mesh sentinelMesh;
        private MeshFilter starsMeshFilter;
        private Material visibleDrawMaterial;
        private Material depthDrawMaterial;
        private Material starRedrawMaterial;
        private Texture2D redrawTexture;
        private CommandBuffer commandBuffer;
        private bool rightEye;
        private bool configured;
        private string setupError;

        private int callbackCount;
        private int lastCallbackFrame = -1;
        private int lastMode = -1;
        private int lastLoggedMode = -1;
        private string lastLoggedStatus;
        private bool lastSourceValid;
        private bool lastDestinationValid;
        private bool lastDepthHandleValid;
        private bool lastTargetsAlias;
        private bool lastDrawIssued;
        private bool lastDepthDrawIssued;
        private bool lastSourceSuppressionVerified;
        private int lastWidth;
        private int lastHeight;
        private int lastSentinelMeshId;
        private int lastSentinelVertexCount;
        private int lastStarsMeshId;
        private int lastStarsVertexCount;
        private Mesh firstStarsMesh;
        private bool starsMeshStayedStable;
        private bool disposed;

        internal static int CurrentMode
        {
            get
            {
                int mode = PlayerPrefs.GetInt(PrefsKey, 0);
                return mode == SourceVisibleMode || mode == CustomDrawMode
                    || mode == StarSourceSuppressionMode || mode == StarRedrawMode ? mode : 0;
            }
        }

        internal static Shader ResolveProbeShader()
        {
            // UBOAT strips Unity's built-in Unlit/Color shader. Prefer the
            // built-in immediate-mode color shader used by this Unity version,
            // with the sprite shader as a fallback for stripped bundles.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            return shader != null ? shader : Shader.Find("Sprites/Default");
        }

        /// <summary>Configures this camera-local experiment. Shared sentinel
        /// geometry is supplied by SextantPipFactory; this component only owns
        /// its per-eye test materials and command buffer.</summary>
        public bool ConfigureProbe(Transform sourceTransform, Transform occluderTransform,
            Mesh sourceMesh, MeshFilter celestialMeshFilter, bool isRightEye)
        {
            if (sourceTransform == null || occluderTransform == null || sourceMesh == null)
            {
                setupError = "sentinel-source-or-occluder-missing";
                return false;
            }

            Shader shader = ResolveProbeShader();
            if (shader == null)
            {
                setupError = "no compatible built-in color shader";
                Debug.LogWarning(LogPrefix + " " + gameObject.name + " setup failed: " + setupError);
                return false;
            }

            pipCamera = GetComponent<Camera>();
            if (pipCamera == null)
            {
                setupError = "PIP Camera missing";
                return false;
            }

            sentinelTransform = sourceTransform;
            depthOccluderTransform = occluderTransform;
            sentinelMesh = sourceMesh;
            starsMeshFilter = celestialMeshFilter;
            rightEye = isRightEye;
            firstStarsMesh = starsMeshFilter != null ? starsMeshFilter.sharedMesh : null;
            starsMeshStayedStable = firstStarsMesh != null;

            if (visibleDrawMaterial == null || depthDrawMaterial == null
                || starRedrawMaterial == null)
            {
                DestroyMaterial(visibleDrawMaterial);
                DestroyMaterial(depthDrawMaterial);
                visibleDrawMaterial = new Material(shader);
                visibleDrawMaterial.name = rightEye
                    ? "RadioNavSternspreizPhase0_RightVisible"
                    : "RadioNavSternspreizPhase0_LeftVisible";
                visibleDrawMaterial.hideFlags = HideFlags.DontSave;
                visibleDrawMaterial.color = rightEye
                    ? new Color(0.05f, 0.35f, 1f, 1f)
                    : new Color(0f, 1f, 0.85f, 1f);

                depthDrawMaterial = new Material(shader);
                depthDrawMaterial.name = rightEye
                    ? "RadioNavSternspreizPhase0_RightDepth"
                    : "RadioNavSternspreizPhase0_LeftDepth";
                depthDrawMaterial.hideFlags = HideFlags.DontSave;
                depthDrawMaterial.color = new Color(1f, 0f, 0.8f, 1f);

                CreateRedrawMaterial(shader);
            }

            setupError = null;
            configured = true;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} CONFIG pip={1} sentinelMesh={2} starsFilter={3}",
                LogPrefix, pipCamera.name, sentinelMesh.GetInstanceID(),
                starsMeshFilter != null));
            return true;
        }

        /// <summary>Phase-1b diagnostic tint material: flat chartreuse, unlike
        /// the white native/mod stars and unlike every mode-2 sentinel color,
        /// so a redrawn layer is distinguishable from an unsuppressed underlay.
        /// Backed by the Sprites/Default additive path (the same shader family
        /// as the native star layer): Internal-Colored with normal blending
        /// leaves sub-pixel low-alpha quads below the render jitter floor
        /// (verified live 2026-09-28), while SrcAlpha/One accumulation
        /// reproduces native visibility in green. The white 1x1 texture is
        /// required: an unbound sampler reads black and black adds nothing
        /// (second live lesson, same night).</summary>
        private void CreateRedrawMaterial(Shader fallbackShader)
        {
            DestroyMaterial(starRedrawMaterial);
            starRedrawMaterial = null;
            if (redrawTexture != null)
            {
                UnityEngine.Object.Destroy(redrawTexture);
                redrawTexture = null;
            }
            Shader redrawShader = Shader.Find("Sprites/Default");
            if (redrawShader != null)
            {
                starRedrawMaterial = new Material(redrawShader);
                // An unbound sampler reads black, and black adds nothing:
                // give the additive material an explicit white texel so the
                // draw deposits tinted vertex colors like the native layer.
                redrawTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                redrawTexture.name = rightEye
                    ? "RadioNavSternspreizPhase1b_RightRedrawTex"
                    : "RadioNavSternspreizPhase1b_LeftRedrawTex";
                redrawTexture.hideFlags = HideFlags.DontSave;
                redrawTexture.SetPixel(0, 0, Color.white);
                redrawTexture.Apply();
                // LOUD diagnostic blend: ignore the small vertex alpha and
                // deposit full tint per covered pixel, so the ~7 in-view stars
                // rise above the exposure-breathing jitter floor. Photometry is
                // deliberately broken here; placement is what is tested.
                starRedrawMaterial.mainTexture = redrawTexture;
                starRedrawMaterial.SetFloat("_SrcBlend",
                    (float)UnityEngine.Rendering.BlendMode.One);
                starRedrawMaterial.SetFloat("_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.One);
                starRedrawMaterial.SetInt("_ZWrite", 0);
            }
            else
            {
                starRedrawMaterial = new Material(fallbackShader);
            }
            starRedrawMaterial.name = rightEye
                ? "RadioNavSternspreizPhase1b_RightRedraw"
                : "RadioNavSternspreizPhase1b_LeftRedraw";
            starRedrawMaterial.hideFlags = HideFlags.DontSave;
            starRedrawMaterial.color = new Color(0.55f, 1f, 0.1f, 1f);
        }

        /// <summary>
        /// Runs in UBOAT's opaque image-effect stage. The test path first
        /// copies source to destination, then draws the shared sentinel mesh.
        /// A front cyan/blue draw must be visible; a magenta draw placed behind
        /// a known opaque sentinel occluder must be hidden if zBuffer is usable.
        /// </summary>
        public void RenderImageOpaque(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer, int width, int height,
            RenderTargetIdentifier upscalingBiasMask, CullingResults cullResults)
        {
            int previousMode = lastMode;
            int mode = CurrentMode;
            bool sourceValid = !source.Equals(default(RenderTargetIdentifier));
            bool destinationValid = !destination.Equals(default(RenderTargetIdentifier));
            bool depthValid = !zBuffer.Equals(default(RenderTargetIdentifier));
            bool targetsAlias = source.Equals(destination);
            bool drawIssued = false;

            lastSourceValid = sourceValid;
            lastDestinationValid = destinationValid;
            lastDepthHandleValid = depthValid;
            lastTargetsAlias = targetsAlias;
            lastWidth = width;
            lastHeight = height;
            lastCallbackFrame = Time.frameCount;
            callbackCount++;
            lastMode = mode;

            // An attached probe can be switched off without restarting the
            // sextant. In OFF mode it must be a true no-op: no Blit and no
            // DrawMesh, so the normal pipeline owns the image unchanged.
            if (mode == 0)
            {
                lastDrawIssued = false;
                lastDepthDrawIssued = false;
                lastSourceSuppressionVerified = false;
                return;
            }

            if (!TargetsAreValid(sourceValid, destinationValid, width, height))
            {
                lastDrawIssued = false;
                lastDepthDrawIssued = false;
                lastSourceSuppressionVerified = false;
                RecordMeshTelemetry();
                LogCallback("invalid-target", mode, false);
                return;
            }

            // The custom fixture draw, the actual-star suppression probe and the
            // Phase-1b star redraw all require proof that the registered source
            // was disabled for this exact PIP render. Mode 3 stays pass-through
            // and never draws; only mode 4 redraws, using the existing star mesh.
            bool requiresSourceSuppression = mode == CustomDrawMode
                || mode == StarSourceSuppressionMode || mode == StarRedrawMode;
            if (requiresSourceSuppression
                && !SternspreizPipRenderScopePatch.IsSourceSuppressedForCurrentRender(pipCamera))
            {
                setupError = "registered-source-not-suppressed-for-current-pip-render";
                lastDrawIssued = false;
                lastDepthDrawIssued = false;
                lastSourceSuppressionVerified = false;
                RecordMeshTelemetry();
                LogCallback("source-not-suppressed", mode, false);
                return;
            }

            setupError = null;
            lastSourceSuppressionVerified = requiresSourceSuppression;
            try
            {
                drawIssued = ExecuteProbeDraw(context, source, destination,
                    zBuffer, depthValid, targetsAlias, mode);
            }
            catch (Exception e)
            {
                setupError = e.GetType().Name + ": " + e.Message;
                lastDrawIssued = false;
                lastDepthDrawIssued = false;
                lastSourceSuppressionVerified = false;
                LogCallback("execute-failed", mode, drawIssued);
                return;
            }

            lastDrawIssued = drawIssued;
            RecordMeshTelemetry();
            if (callbackCount == 1 || mode != previousMode)
                LogCallback("executed", mode, drawIssued);
        }

        private static bool TargetsAreValid(bool sourceValid, bool destinationValid,
            int width, int height)
        {
            return sourceValid && destinationValid && width > 0 && height > 0;
        }

        private bool ExecuteProbeDraw(ScriptableRenderContext context,
            RenderTargetIdentifier source, RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer, bool depthValid, bool targetsAlias, int mode)
        {
            EnsureCommandBuffer();
            commandBuffer.Clear();
            if (!targetsAlias)
                commandBuffer.Blit(source, destination);
            if (mode == CustomDrawMode && CanDrawSentinels())
                lastDepthDrawIssued = QueueSentinelDraws(destination, zBuffer, depthValid);
            else
                lastDepthDrawIssued = false;
            bool starDrawIssued = mode == StarRedrawMode
                && DrawStarsMesh(destination, zBuffer, depthValid);
            context.ExecuteCommandBuffer(commandBuffer);
            return (mode == CustomDrawMode && CanDrawSentinels()) || starDrawIssued;
        }

        private void EnsureCommandBuffer()
        {
            if (commandBuffer != null)
                return;
            commandBuffer = new CommandBuffer();
            commandBuffer.name = "RadioNav Sternspreiz Phase0 Probe";
        }

        private bool CanDrawSentinels()
        {
            return configured && pipCamera != null && sentinelMesh != null
                && sentinelTransform != null && depthOccluderTransform != null
                && visibleDrawMaterial != null
                && depthDrawMaterial != null;
        }

        private bool QueueSentinelDraws(RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer, bool depthValid)
        {
            if (depthValid)
                commandBuffer.SetRenderTarget(destination, zBuffer);
            else
                commandBuffer.SetRenderTarget(destination);
            commandBuffer.SetViewProjectionMatrices(
                pipCamera.worldToCameraMatrix, pipCamera.projectionMatrix);
            DrawAtTransform(commandBuffer, sentinelTransform, visibleDrawMaterial,
                VisibleDrawPass);
            return DrawBehindOccluder(commandBuffer);
        }

        private void DrawAtTransform(CommandBuffer buffer, Transform source,
            Material material, int pass)
        {
            buffer.DrawMesh(sentinelMesh, source.localToWorldMatrix,
                material, 0, pass);
        }

        /// <summary>Phase-1b redraw: draws the already-existing star mesh with
        /// the diagnostic tint material. The mesh vertices are world-space (as
        /// built by StarsMesh), so the matrix is identity; nothing is cloned,
        /// rebuilt, or generated per star.</summary>
        private bool DrawStarsMesh(RenderTargetIdentifier destination,
            RenderTargetIdentifier zBuffer, bool depthValid)
        {
            // Draw the renderer's instance mesh (meshFilter.mesh), not
            // sharedMesh: StarsMesh builds via the instance property, and only
            // the instance is guaranteed to carry the live index buffer.
            Mesh starsMesh = starsMeshFilter != null ? starsMeshFilter.mesh : null;
            if (!configured || pipCamera == null || starsMesh == null
                || starRedrawMaterial == null)
                return false;
            if (starsMesh.subMeshCount < 1 || starsMesh.GetIndexCount(0) < 1)
                return false;
            if (depthValid)
                commandBuffer.SetRenderTarget(destination, zBuffer);
            else
                commandBuffer.SetRenderTarget(destination);
            commandBuffer.SetViewProjectionMatrices(
                pipCamera.worldToCameraMatrix, pipCamera.projectionMatrix);
            commandBuffer.DrawMesh(starsMesh, Matrix4x4.identity,
                starRedrawMaterial, 0, VisibleDrawPass);
            return true;
        }

        private bool DrawBehindOccluder(CommandBuffer buffer)
        {
            Vector3 occluderViewport = pipCamera.WorldToViewportPoint(
                depthOccluderTransform.position);
            if (occluderViewport.z <= pipCamera.nearClipPlane)
                return false;

            float rearDistance = Mathf.Max(25f, occluderViewport.z + 2f);
            Vector3 rearPosition = pipCamera.ViewportToWorldPoint(new Vector3(
                occluderViewport.x, occluderViewport.y, rearDistance));
            // Keep the blocker plane's orientation and shrink slightly so
            // perspective/obliquity cannot expose a magenta edge around it.
            float apparentSizeRatio = rearDistance / occluderViewport.z * 0.8f;
            Vector3 rearScale = depthOccluderTransform.lossyScale * apparentSizeRatio;
            Matrix4x4 rearMatrix = Matrix4x4.TRS(rearPosition,
                depthOccluderTransform.rotation, rearScale);
            buffer.DrawMesh(sentinelMesh, rearMatrix,
                depthDrawMaterial, 0, DepthDrawPass);
            return true;
        }

        private void RecordMeshTelemetry()
        {
            lastSentinelMeshId = sentinelMesh != null ? sentinelMesh.GetInstanceID() : 0;
            lastSentinelVertexCount = sentinelMesh != null ? sentinelMesh.vertexCount : 0;
            Mesh starsMesh = starsMeshFilter != null ? starsMeshFilter.sharedMesh : null;
            lastStarsMeshId = starsMesh != null ? starsMesh.GetInstanceID() : 0;
            lastStarsVertexCount = starsMesh != null ? starsMesh.vertexCount : 0;
            if (starsMesh == null)
                starsMeshStayedStable = false;
            else if (firstStarsMesh == null)
                firstStarsMesh = starsMesh;
            else if (starsMesh != firstStarsMesh)
                starsMeshStayedStable = false;
        }

        private void LogCallback(string status, int mode, bool drawIssued)
        {
            if (lastLoggedMode == mode && lastLoggedStatus == status)
                return;
            lastLoggedMode = mode;
            lastLoggedStatus = status;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} CALLBACK pip={1} status={2} mode={3} count={4} frame={5} "
                + "source={6} destination={7} depthHandle={8} alias={9} size={10}x{11} "
                + "drawIssued={12} sourceSuppressed={13} sentinelMesh={14}/{15} "
                + "starsMesh={16}/{17} setupError={18}",
                LogPrefix, pipCamera != null ? pipCamera.name : gameObject.name,
                status, mode, callbackCount, Time.frameCount,
                lastSourceValid, lastDestinationValid, lastDepthHandleValid,
                lastTargetsAlias, lastWidth, lastHeight, drawIssued,
                lastSourceSuppressionVerified,
                lastSentinelMeshId, lastSentinelVertexCount,
                lastStarsMeshId, lastStarsVertexCount,
                string.IsNullOrEmpty(setupError) ? "none" : setupError));
        }

        /// <summary>JSON used by the existing /sextant snapshot endpoint.</summary>
        internal string SnapshotJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"configured\":{0},\"callbackCount\":{1},\"lastFrame\":{2},"
                + "\"enabled\":{3},\"mode\":{4},\"sourceValid\":{5},\"destinationValid\":{6},"
                + "\"depthHandleValid\":{7},\"targetsAlias\":{8},\"sourceSuppressed\":{9},"
                + "\"width\":{10},\"height\":{11},\"drawIssued\":{12},"
                + "\"depthDrawIssued\":{13},\"sentinelMeshId\":{14},\"sentinelVertices\":{15},"
                + "\"starsMeshId\":{16},\"starsVertices\":{17},"
                + "\"starsMeshRead\":{18},\"starsMeshStable\":{19},\"setupError\":\"{20}\"}}",
                configured ? "true" : "false", callbackCount, lastCallbackFrame,
                enabled ? "true" : "false", lastMode, lastSourceValid ? "true" : "false",
                lastDestinationValid ? "true" : "false",
                lastDepthHandleValid ? "true" : "false",
                lastTargetsAlias ? "true" : "false",
                lastSourceSuppressionVerified ? "true" : "false", lastWidth, lastHeight,
                lastDrawIssued ? "true" : "false",
                lastDepthDrawIssued ? "true" : "false", lastSentinelMeshId,
                lastSentinelVertexCount, lastStarsMeshId, lastStarsVertexCount,
                starsMeshFilter != null && lastStarsMeshId != 0 ? "true" : "false",
                starsMeshStayedStable ? "true" : "false",
                RadioNavProbeJson.Escape(setupError ?? string.Empty));
        }

        public void OnDestroy()
        {
            Dispose();
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
            DestroyMaterial(visibleDrawMaterial);
            DestroyMaterial(depthDrawMaterial);
            DestroyMaterial(starRedrawMaterial);
            if (redrawTexture != null)
            {
                UnityEngine.Object.Destroy(redrawTexture);
                redrawTexture = null;
            }
            visibleDrawMaterial = null;
            depthDrawMaterial = null;
            starRedrawMaterial = null;
            sentinelTransform = null;
            depthOccluderTransform = null;
            sentinelMesh = null;
            starsMeshFilter = null;
            firstStarsMesh = null;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
                UnityEngine.Object.Destroy(material);
        }
    }
}
