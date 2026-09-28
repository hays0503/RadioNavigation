using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PlayWay.Water;
using UBOAT.Game.Scene.Camera;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PIP camera lifecycle for the sextant ocular (P5.4 slice 4a, SRP):
    /// RenderTexture + camera creation (periscope pattern: AttachedCameras
    /// + SRP passes + WaterCamera + bloom-cut volume + render probe),
    /// main-camera water registration, and teardown. Extracted from
    /// SextantController РІР‚вЂќ the controller keeps pose, input and readout.
    /// Creation order and log strings are unchanged from the move.
    /// </summary>
    public sealed class SextantPipFactory
    {
        // RT size follows the screen (P7): half the screen height, clamped by
        // SextantPipSize. Recreated on open when the resolution changed.
        public int RtSize { get; private set; } = SextantPipSize.FallbackSize;

        // NOTE (2026-09-18): the PIP scene render intermittently loses its
        // lighting (one whole RT goes dark, ~3-7% of frames, either eye at
        // random) inside the game's SRP РІР‚вЂќ a game-side transient that this mod
        // could not eliminate. Tried and measured ineffective: render-order
        // overrides (depth), a dummy absorber camera, distinct RT sizes, the
        // SSMS mirror toggle. See SEXTANT_PLAN.md Р’В§16.15.
        private const string LogPrefix = "[RadioNav][Sextant]";

        public Camera LeftCamera { get; private set; }

        public Camera RightCamera { get; private set; }

        public RenderTexture LeftRt { get; private set; }

        public RenderTexture RightRt { get; private set; }

        public bool WaterAttached => registeredWaterData != null;

        private UBOAT.Game.Scene.Rendering.AdditionalCameraData registeredWaterData;
        private bool waterAttachWarned;
        private SextantEffectMirror effectMirror;

        // Per-frame RenderScene sync (see SyncRenderScene): the PIP data
        // components created here plus the view camera's data cached across
        // frames (one GetComponent per view-camera change, never per frame).
        private UBOAT.Game.Scene.Rendering.AdditionalCameraData leftData;
        private UBOAT.Game.Scene.Rendering.AdditionalCameraData rightData;
        private Camera cachedViewCamera;
        private UBOAT.Game.Scene.Rendering.AdditionalCameraData cachedViewData;

        // Sea gate (R5): the game hides the sea inside the boat per camera
        // (WaterCamera.SetCustomWaterRenderList(empty), FPPCameraController
        // .cs:827). The PIP copies the main WaterCamera config once, so the
        // sea kept rendering through the ocular inside the hull; the state
        // is re-applied per frame from SextantPipPose.
        private WaterCamera leftWaterCamera;
        private WaterCamera rightWaterCamera;
        private readonly List<Water> emptyWaterList = new List<Water>();
        private bool waterListSynced;
        private bool waterListInside;

        // Contact-shadow mirror gate (residual-light track, step 3):
        // PlayerPrefs A/B switch, default 1 (mirror on).
        private const string ContactShadowMirrorPrefsKey = "RadioNav.ContactShadowMirror";
        private bool contactShadowMirrorLogged;

        // Route B interior composite: clone interior camera + own
        // VirtualSceneRenderer per PIP camera (see SextantInteriorMirror).
        private readonly SextantInteriorMirror interiorMirror = new SextantInteriorMirror();

        internal SextantInteriorMirror InteriorMirror
        {
            get { return interiorMirror; }
        }

        // Live source references fed to the mirror each frame.
        private Component sourceSsmsFog;
        private Component sourceSsms;
        private Component sourcePpv1;

        // Explicitly opt-in Sternspreiz feasibility fixture. It is absent at
        // the default PlayerPrefs value (0), and is not a celestial renderer:
        // the test mesh is shared by the source sentinel, depth occluder and
        // both callbacks. Mode 3 only registers StarSkyRoot's existing renderer.
        private const string Phase0SourceName = "RadioNavSternspreizPhase0Source";
        private const string Phase0OccluderName = "RadioNavSternspreizPhase0Occluder";
        private const string Phase0RootName = "RadioNavSternspreizPhase0Root";
        private const int Phase0FixtureLayer = 0; // Unity Default; present in Main and both PIP masks.
        private GameObject phase0Root;
        private GameObject phase0Source;
        private GameObject phase0Occluder;
        private Mesh phase0Mesh;
        private Material phase0SourceMaterial;
        private Material phase0OccluderMaterial;
        private MeshFilter phase0StarsMeshFilter;
        private Renderer phase0RegisteredRenderer;
        private Component leftPhase0Effect;
        private Component rightPhase0Effect;
        private Component rightLensEffect;
        private bool lensSetupFailed;
        private int phase0Layer = -1;
        private int phase0OccluderLayer = -1;
        private int phase0ViewMask;
        private bool phase0Ready;
        private bool phase0SetupAttempted;
        private bool ownsPhase0Fixture;
        private string phase0Error;

        internal void Ensure(Camera viewCamera, float fovDivisor,
            RawImage leftViewImage, RawImage rightViewImage,
            SextantEffectMirror mirror, SextantBloomCut bloomCut,
            SextantController owner, float uiScale)
        {
            // The ocular is a fraction of the screen height (2026-11-04), so
            // the render target follows its on-screen pixel size; SextantPipSize
            // clamps the band (256РІР‚В¦1536).
            int desired = SextantPipSize.ForOcularPixels(
                SextantController.OcularSizeUnits() * (uiScale <= 0f ? 1f : uiScale));
            if (LeftCamera != null && RightCamera != null)
            {
                // Same cameras, possibly a new screen size (P7): recreate the
                // targets on open, never mid-sight.
                if (RtSize != desired)
                    ResizeTargets(desired, leftViewImage, rightViewImage);
                PrepareSternspreizPhase0(viewCamera);
                return;
            }

            effectMirror = mirror;
            RtSize = desired;

            LeftRt = CreateTarget("RadioNavSextantLeftRT", desired);
            RightRt = CreateTarget("RadioNavSextantRightRT", desired);

            LeftCamera = ConfigurePipCamera("RadioNavSextantLeftCamera", LeftRt,
                viewCamera, fovDivisor, false, bloomCut, owner);
            RightCamera = ConfigurePipCamera("RadioNavSextantRightCamera", RightRt,
                viewCamera, fovDivisor, true, bloomCut, owner);

            if (leftViewImage != null) leftViewImage.texture = LeftRt;
            if (rightViewImage != null) rightViewImage.texture = RightRt;

            // This only creates a test fixture when the user explicitly sets
            // RadioNav.SternspreizProbe to 1, 2, or 3. Default (0) leaves the
            // production PIP/celestial path untouched.
            PrepareSternspreizPhase0(viewCamera);

            Debug.Log(LogPrefix + " PIP cameras created, RT=" + desired + "x" + desired);
        }

        private static RenderTexture CreateTarget(string name, int size)
        {
            RenderTexture rt = new RenderTexture(size, size, 32,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.name = name;
            return rt;
        }

        /// <summary>Swaps both PIP targets for a new screen size (P7). The
        /// cameras and the view images are repointed before the old targets
        /// are released, so no frame renders into a destroyed texture.</summary>
        private void ResizeTargets(int desired, RawImage leftViewImage, RawImage rightViewImage)
        {
            RenderTexture oldLeft = LeftRt;
            RenderTexture oldRight = RightRt;

            LeftRt = CreateTarget("RadioNavSextantLeftRT", desired);
            RightRt = CreateTarget("RadioNavSextantRightRT", desired);
            if (LeftCamera != null) LeftCamera.targetTexture = LeftRt;
            if (RightCamera != null) RightCamera.targetTexture = RightRt;
            if (leftViewImage != null) leftViewImage.texture = LeftRt;
            if (rightViewImage != null) rightViewImage.texture = RightRt;

            ReleaseTarget(oldLeft);
            ReleaseTarget(oldRight);
            RtSize = desired;
            Debug.Log(LogPrefix + " PIP RT resized to " + desired + "x" + desired);
        }

        private static void ReleaseTarget(RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        public int AttachedCount()
        {
            if (registeredWaterData == null || registeredWaterData.AttachedCameras == null)
                return 0;
            try
            {
                return registeredWaterData.AttachedCameras.Count;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantController.AttachedCameras"))
                    Debug.LogWarning("[RadioNav][Sextant] AttachedCameras read failed: " + e.Message);
                return 0;
            }
        }

        // Water renders itself only for cameras listed on the main camera's
        // AdditionalCameraData (periscope pattern, Periscope.cs:581).
        // Must be MainCamera, not the transient viewCamera (device probe).
        public void AttachToMain()
        {
            if (LeftCamera == null && RightCamera == null)
                return;
            MainCamera main = MainCamera.Instance;
            if (main == null)
                main = UnityEngine.Object.FindObjectOfType<MainCamera>();
            if (main == null)
                return;

            UBOAT.Game.Scene.Rendering.AdditionalCameraData data =
                main.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            if (data == null)
            {
                if (!waterAttachWarned)
                {
                    waterAttachWarned = true;
                    Debug.LogWarning(LogPrefix + " main camera lacks AdditionalCameraData - no water in PIP");
                }
                return;
            }

            registeredWaterData = data;
            // The game's list itself can be null after an update РІР‚вЂќ guard the
            // deref, not just the component.
            if (data.AttachedCameras == null)
            {
                if (WarnOnce.ShouldLog("SextantController.AttachedCamerasList"))
                    Debug.LogWarning(LogPrefix + " AdditionalCameraData.AttachedCameras is null - no water in PIP");
                return;
            }
            if (LeftCamera != null)
            {
                data.AttachedCameras.Remove(LeftCamera);
                data.AttachedCameras.Add(LeftCamera);
            }
            if (RightCamera != null)
            {
                data.AttachedCameras.Remove(RightCamera);
                data.AttachedCameras.Add(RightCamera);
            }
        }

        public void Detach()
        {
            if (registeredWaterData != null && registeredWaterData.AttachedCameras != null)
            {
                if (LeftCamera != null)
                    registeredWaterData.AttachedCameras.Remove(LeftCamera);
                if (RightCamera != null)
                    registeredWaterData.AttachedCameras.Remove(RightCamera);
            }
            registeredWaterData = null;
        }

        public void Destroy()
        {
            Detach();
            DestroySternspreizPhase0();
            DestroyLens();
            interiorMirror.Destroy();
            leftWaterCamera = null;
            rightWaterCamera = null;
            waterListSynced = false;
            contactShadowMirrorLogged = false;
            leftData = null;
            rightData = null;
            cachedViewCamera = null;
            cachedViewData = null;
            if (LeftCamera != null) UnityEngine.Object.Destroy(LeftCamera.gameObject);
            if (RightCamera != null) UnityEngine.Object.Destroy(RightCamera.gameObject);
            LeftCamera = null;
            RightCamera = null;
            if (LeftRt != null)
            {
                LeftRt.Release();
                UnityEngine.Object.Destroy(LeftRt);
                LeftRt = null;
            }
            if (RightRt != null)
            {
                RightRt.Release();
                UnityEngine.Object.Destroy(RightRt);
                RightRt = null;
            }
        }

        public void RefreshMirroredState(Camera viewCamera)
        {
            if (effectMirror != null)
            {
                effectMirror.RefreshSsms(sourceSsmsFog, sourceSsms);
                effectMirror.SyncPostProfile(sourcePpv1);
                // The game recomputes the fog's interior ambient from the
                // interior light probes every frame (FPPCameraController.cs:1042);
                // the one-off PIP fog clone would otherwise keep a stale
                // ambient and overflow the light-combine term (see
                // SextantEffectMirror.SyncInteriorAmbient).
                effectMirror.SyncInteriorAmbient(sourceSsmsFog);
                // PPv2 keeps the adapted exposure per PostProcessLayer; the
                // PIPs' fresh layers need the main layer's state (enabled at
                // night by the game's BrighterNights task).
                effectMirror.SyncAutoExposure();
            }
            SyncRenderScene(viewCamera);
            SyncContactShadows();
        }

        /// <summary>
        /// Ensures the opt-in Sternspreiz diagnostic before SextantPipPose
        /// writes this frame's culling masks. Safe per frame: setup is
        /// idempotent and the default preference remains OFF.
        /// </summary>
        internal void PrepareSternspreizPhase0(Camera viewCamera)
        {
            int mode = SternspreizPipProbeEffect.CurrentMode;
            if (mode == 0)
            {
                if (phase0Ready)
                {
                    UnregisterPhase0RenderTarget();
                    SetPhase0SourcesEnabled(false);
                    SetPhase0EffectsEnabled(false);
                }
                return;
            }

            if (!phase0Ready && !phase0SetupAttempted)
            {
                phase0SetupAttempted = true;
                TryCreateSternspreizPhase0(viewCamera);
            }

            if (phase0Ready)
            {
                // Modes 3/4 are actual-source diagnostics. Keep the disposable
                // fixture completely out of Main and both PIPs.
                SetPhase0SourcesEnabled(mode != SternspreizPipProbeEffect.StarSourceSuppressionMode
                    && mode != SternspreizPipProbeEffect.StarRedrawMode);
                SetPhase0EffectsEnabled(true);
                MeshFilter liveStarsFilter = FindStarsMeshFilter();
                if (liveStarsFilter != phase0StarsMeshFilter)
                {
                    phase0StarsMeshFilter = liveStarsFilter;
                    ConfigurePhase0Effect(leftPhase0Effect, false);
                    ConfigurePhase0Effect(rightPhase0Effect, true);
                }
                SetPhase0RenderTarget(mode);
                UpdateSternspreizPhase0Pose(viewCamera);
            }
        }

        /// <summary>
        /// Per-frame driver of the Sternspreiz lens (production feature):
        /// attaches the streak effect to the ALIDADE PIP only (RightCamera is
        /// the one pitched by the arc) because the glass sits in the index
        /// (star) beam: the real instrument stretches the reflected star and
        /// leaves the direct horizon view untouched. Safe per frame; the
        /// preference gate keeps it OFF by default.
        /// </summary>
        internal void PrepareLens()
        {
            if (!SternspreizLensEffect.Enabled)
            {
                SuspendLens();
                lensSetupFailed = false;
                return;
            }

            if (lensSetupFailed)
                return;

            if (rightLensEffect == null)
                rightLensEffect = EnsureAndConfigureLens(RightCamera, true);
            if (rightLensEffect == null)
            {
                lensSetupFailed = true;
                if (WarnOnce.ShouldLog("SextantPipFactory.LensSetup"))
                    Debug.LogWarning(LogPrefix + " lens effect setup failed; the lens stays off until the preference is toggled");
                return;
            }
            SetLensEffectsEnabled(true);
        }

        private Component EnsureAndConfigureLens(Camera camera, bool isRightEye)
        {
            Component component = EnsureLensEffect(camera);
            if (component == null)
                return null;
            if (!ConfigureLensEffect(component, isRightEye))
                return null;
            return component;
        }

        private static Component EnsureLensEffect(Camera camera)
        {
            if (camera == null)
                return null;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().Name != SternspreizLensEffect.ComponentName)
                    continue;
                return behaviour;
            }
            return camera.gameObject.AddComponent<SternspreizLensEffect>();
        }

        private static bool ConfigureLensEffect(Component component, bool isRightEye)
        {
            if (component == null)
                return false;
            SternspreizLensEffect typed = component as SternspreizLensEffect;
            if (typed != null)
                return typed.ConfigureLens(isRightEye);

            // A second loaded copy of the mod sees the first copy's component
            // only by type name; configure it through reflection.
            MethodInfo configure = component.GetType().GetMethod("ConfigureLens",
                BindingFlags.Public | BindingFlags.Instance);
            if (configure == null)
                return false;
            object result = configure.Invoke(component, new object[] { isRightEye });
            return result is bool && (bool)result;
        }

        internal void SuspendLens()
        {
            SetLensEffectsEnabled(false);
            SternspreizLensEffect typed = rightLensEffect as SternspreizLensEffect;
            if (typed != null)
                typed.NotifySuspended();
        }

        private void SetLensEffectsEnabled(bool enabled)
        {
            // Only the alidade (right) PIP carries the glass; the direct
            // horizon half stays untouched, exactly like the real instrument.
            SetPhase0EffectEnabled(rightLensEffect, enabled);
        }

        private void DestroyLens()
        {
            SuspendLens();
            RemoveLensEffect(RightCamera);
            rightLensEffect = null;
            lensSetupFailed = false;
        }

        private static void RemoveLensEffect(Camera camera)
        {
            if (camera == null)
                return;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().Name != SternspreizLensEffect.ComponentName)
                    continue;
                behaviour.enabled = false;
                UnityEngine.Object.Destroy(behaviour);
            }
        }

        private void TryCreateSternspreizPhase0(Camera viewCamera)
        {
            if (viewCamera == null || LeftCamera == null || RightCamera == null)
            {
                FailSternspreizPhase0("view-or-pip-camera-missing");
                return;
            }

            if (!ResolvePhase0Fixture(viewCamera))
                return;
            AttachPhase0Effects();
        }

        private bool ResolvePhase0Fixture(Camera viewCamera)
        {
            GameObject existingRoot = GameObject.Find(Phase0RootName);
            GameObject existingSource = GameObject.Find(Phase0SourceName);
            GameObject existingOccluder = GameObject.Find(Phase0OccluderName);
            if (existingRoot != null || existingSource != null || existingOccluder != null)
            {
                if (existingRoot == null || existingSource == null || existingOccluder == null)
                {
                    FailSternspreizPhase0("partial-existing-fixture");
                    return false;
                }
                phase0Root = existingRoot;
                phase0Source = existingSource;
                phase0Occluder = existingOccluder;
                if (!ReadExistingPhase0Fixture() || !CanRoutePhase0Fixture(viewCamera))
                    return false;
                ownsPhase0Fixture = false;
                return true;
            }

            phase0Layer = Phase0FixtureLayer;
            phase0OccluderLayer = Phase0FixtureLayer;
            if (!CanRoutePhase0Fixture(viewCamera))
                return false;
            if (!CreateSternspreizPhase0Fixture(phase0Layer, phase0OccluderLayer))
            {
                DestroySternspreizPhase0Fixture();
                return false;
            }
            ownsPhase0Fixture = true;
            return true;
        }

        private bool ReadExistingPhase0Fixture()
        {
            MeshFilter sourceFilter = phase0Source.GetComponent<MeshFilter>();
            MeshFilter occluderFilter = phase0Occluder.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = phase0Source.GetComponent<MeshRenderer>();
            MeshRenderer occluderRenderer = phase0Occluder.GetComponent<MeshRenderer>();
            if (phase0Source.layer != Phase0FixtureLayer
                || phase0Occluder.layer != Phase0FixtureLayer)
                return FailPhase0Fixture("existing-fixture-not-on-default-layer");
            if (sourceFilter == null || occluderFilter == null
                || sourceFilter.sharedMesh == null || sourceFilter.sharedMesh != occluderFilter.sharedMesh)
                return FailPhase0Fixture("existing-fixture-mesh-mismatch");
            if (sourceRenderer == null || occluderRenderer == null)
                return FailPhase0Fixture("existing-fixture-renderer-missing");
            phase0Mesh = sourceFilter.sharedMesh;
            phase0Layer = phase0Source.layer;
            phase0OccluderLayer = phase0Occluder.layer;
            phase0SourceMaterial = sourceRenderer.sharedMaterial;
            phase0OccluderMaterial = occluderRenderer.sharedMaterial;
            return true;
        }

        private bool CanRoutePhase0Fixture(Camera viewCamera)
        {
            int bit = 1 << Phase0FixtureLayer;
            if (viewCamera == null || (viewCamera.cullingMask & bit) == 0
                || LeftCamera == null || (LeftCamera.cullingMask & bit) == 0
                || RightCamera == null || (RightCamera.cullingMask & bit) == 0)
            {
                FailSternspreizPhase0("default-layer-not-included-by-main-and-both-pips");
                return false;
            }
            if (!PassesContainLayer(leftData, bit) || !PassesContainLayer(rightData, bit))
            {
                FailSternspreizPhase0("default-layer-not-included-by-both-pip-passes");
                return false;
            }
            return true;
        }

        private bool FailPhase0Fixture(string reason)
        {
            FailSternspreizPhase0(reason);
            return false;
        }

        private void AttachPhase0Effects()
        {
            phase0StarsMeshFilter = FindStarsMeshFilter();
            leftPhase0Effect = EnsurePhase0Effect(LeftCamera);
            rightPhase0Effect = EnsurePhase0Effect(RightCamera);
            bool leftConfigured = ConfigurePhase0Effect(leftPhase0Effect, false);
            bool rightConfigured = ConfigurePhase0Effect(rightPhase0Effect, true);
            Renderer sourceRenderer = phase0Source != null
                ? phase0Source.GetComponent<Renderer>() : null;
            bool leftRegistered = leftConfigured
                && SternspreizPipRenderScopePatch.Register(LeftCamera, sourceRenderer);
            bool rightRegistered = rightConfigured
                && SternspreizPipRenderScopePatch.Register(RightCamera, sourceRenderer);
            if (leftPhase0Effect == null || rightPhase0Effect == null
                || !leftConfigured || !rightConfigured || !leftRegistered || !rightRegistered)
            {
                FailSternspreizPhase0("pip-effect-or-render-scope-registration-failed");
                SternspreizPipRenderScopePatch.Unregister(LeftCamera, sourceRenderer);
                SternspreizPipRenderScopePatch.Unregister(RightCamera, sourceRenderer);
                RemovePhase0Effect(LeftCamera);
                RemovePhase0Effect(RightCamera);
                if (ownsPhase0Fixture)
                    DestroySternspreizPhase0Fixture();
                phase0Source = null;
                phase0Occluder = null;
                phase0Root = null;
                phase0Mesh = null;
                phase0SourceMaterial = null;
                phase0OccluderMaterial = null;
                phase0Layer = -1;
                phase0OccluderLayer = -1;
                ownsPhase0Fixture = false;
                return;
            }

            phase0Ready = true;
            phase0RegisteredRenderer = sourceRenderer;
            phase0Error = null;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} PROBE READY layer={1} bit=0x{2:X8} mesh={3} starsFilter={4} effects=1/1 mode={5} renderScope={6}",
                LogPrefix, phase0Layer, 1 << phase0Layer,
                phase0Mesh != null ? phase0Mesh.GetInstanceID() : 0,
                phase0StarsMeshFilter != null,
                SternspreizPipProbeEffect.CurrentMode,
                SternspreizPipRenderScopePatch.IsAvailable));
        }

        private static bool PassesContainLayer(
            UBOAT.Game.Scene.Rendering.AdditionalCameraData data, int bit)
        {
            if (data == null || !(data.Passes is Array passes) || passes.Length == 0)
                return false;
            for (int i = 0; i < passes.Length; i++)
            {
                object pass = passes.GetValue(i);
                if (pass == null)
                    return false;
                MemberInfo member = TypeMemberCache.GetInstanceMember(pass.GetType(), "CullingMask");
                PropertyInfo property = member as PropertyInfo;
                FieldInfo field = member as FieldInfo;
                object value = null;
                if (property != null)
                    value = property.GetValue(pass, null);
                else if (field != null)
                    value = field.GetValue(pass);
                if (!(value is int mask) || (mask & bit) != bit)
                    return false;
            }
            return true;
        }

        private bool CreateSternspreizPhase0Fixture(int sourceLayer, int occluderLayer)
        {
            Shader shader = SternspreizPipProbeEffect.ResolveProbeShader();
            if (shader == null)
            {
                FailSternspreizPhase0("no-compatible-built-in-color-shader");
                return false;
            }

            phase0Root = new GameObject(Phase0RootName);
            phase0Root.hideFlags = HideFlags.DontSave;
            phase0Mesh = CreatePhase0SentinelMesh();
            phase0SourceMaterial = CreatePhase0Material(shader,
                "RadioNavSternspreizPhase0_Source", new Color(1f, 0.35f, 0f, 1f));
            phase0OccluderMaterial = CreatePhase0Material(shader,
                "RadioNavSternspreizPhase0_Occluder", new Color(1f, 0.9f, 0f, 1f));
            if (phase0Mesh == null || phase0SourceMaterial == null || phase0OccluderMaterial == null)
            {
                FailSternspreizPhase0("sentinel-mesh-or-material-create-failed");
                return false;
            }

            phase0Source = CreatePhase0Marker(Phase0SourceName, sourceLayer,
                phase0Mesh, phase0SourceMaterial, phase0Root.transform);
            phase0Occluder = CreatePhase0Marker(Phase0OccluderName, occluderLayer,
                phase0Mesh, phase0OccluderMaterial, phase0Root.transform);
            if (phase0Source == null || phase0Occluder == null)
            {
                FailSternspreizPhase0("sentinel-gameobject-create-failed");
                return false;
            }
            ownsPhase0Fixture = true;
            return true;
        }

        private static Mesh CreatePhase0SentinelMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "RadioNavSternspreizPhase0SharedMesh";
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            // Both windings make the test quad visible from either side.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 1, 2, 0, 2, 3, 0 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreatePhase0Material(Shader shader, string name, Color color)
        {
            Material material = new Material(shader);
            material.name = name;
            material.hideFlags = HideFlags.DontSave;
            material.color = color;
            return material;
        }

        private static GameObject CreatePhase0Marker(string name, int layer,
            Mesh mesh, Material material, Transform parent)
        {
            GameObject marker = new GameObject(name);
            marker.hideFlags = HideFlags.DontSave;
            marker.layer = layer;
            MeshFilter filter = marker.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            marker.transform.SetParent(parent, true);
            return marker;
        }

        private Component EnsurePhase0Effect(Camera camera)
        {
            if (camera == null)
                return null;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().Name != SternspreizPipProbeEffect.ComponentName)
                    continue;
                return behaviour;
            }
            return camera.gameObject.AddComponent<SternspreizPipProbeEffect>();
        }

        private bool ConfigurePhase0Effect(Component component, bool isRightEye)
        {
            if (component == null || phase0Source == null || phase0Mesh == null)
                return false;
            SternspreizPipProbeEffect typed = component as SternspreizPipProbeEffect;
            if (typed != null)
                return typed.ConfigureProbe(phase0Source.transform,
                    phase0Occluder.transform, phase0Mesh, phase0StarsMeshFilter, isRightEye);

            // A second loaded copy of this mod can see the first copy's
            // component only by type name. Configure that existing instance
            // rather than adding a second callback to the same PIP camera.
            MethodInfo configure = component.GetType().GetMethod("ConfigureProbe",
                BindingFlags.Public | BindingFlags.Instance);
            if (configure == null)
                return false;
            object result = configure.Invoke(component, new object[]
            {
                phase0Source.transform, phase0Occluder.transform, phase0Mesh,
                phase0StarsMeshFilter, isRightEye
            });
            return result is bool && (bool)result;
        }

        private static MeshFilter FindStarsMeshFilter()
        {
            GameObject root = GameObject.Find("StarSkyRoot");
            return root != null ? root.GetComponent<MeshFilter>() : null;
        }

        private static Renderer FindStarsMeshRenderer()
        {
            MeshFilter filter = FindStarsMeshFilter();
            return filter != null ? filter.GetComponent<MeshRenderer>() : null;
        }

        private bool SetPhase0RenderTarget(int mode)
        {
            Renderer target = null;
            if (mode == SternspreizPipProbeEffect.StarSourceSuppressionMode
                || mode == SternspreizPipProbeEffect.StarRedrawMode)
                target = FindStarsMeshRenderer();
            else if (phase0Source != null)
                target = phase0Source.GetComponent<Renderer>();

            if (target == phase0RegisteredRenderer)
                return target != null;

            UnregisterPhase0RenderTarget();
            if (target == null)
            {
                FailSternspreizPhase0(mode == SternspreizPipProbeEffect.StarSourceSuppressionMode
                    || mode == SternspreizPipProbeEffect.StarRedrawMode
                    ? "StarSkyRoot-MeshRenderer-not-found"
                    : "fixture-source-renderer-not-found");
                return false;
            }

            bool leftRegistered = SternspreizPipRenderScopePatch.Register(LeftCamera, target);
            bool rightRegistered = SternspreizPipRenderScopePatch.Register(RightCamera, target);
            if (!leftRegistered || !rightRegistered)
            {
                SternspreizPipRenderScopePatch.Unregister(LeftCamera, target);
                SternspreizPipRenderScopePatch.Unregister(RightCamera, target);
                FailSternspreizPhase0("source-render-scope-registration-failed");
                return false;
            }

            phase0RegisteredRenderer = target;
            phase0Error = null;
            return true;
        }

        private void UnregisterPhase0RenderTarget()
        {
            if (phase0RegisteredRenderer == null)
                return;
            SternspreizPipRenderScopePatch.Unregister(LeftCamera, phase0RegisteredRenderer);
            SternspreizPipRenderScopePatch.Unregister(RightCamera, phase0RegisteredRenderer);
            phase0RegisteredRenderer = null;
        }

        private void UpdateSternspreizPhase0Pose(Camera viewCamera)
        {
            if (!phase0Ready || viewCamera == null)
                return;
            phase0ViewMask = viewCamera.cullingMask;
            UpdatePhase0Marker(phase0Source, viewCamera, 25f, 0.22f);
            UpdatePhase0Marker(phase0Occluder, viewCamera, 5f, -0.22f);
        }

        private static void UpdatePhase0Marker(GameObject marker, Camera camera,
            float requestedDistance, float viewportY)
        {
            if (marker == null || camera == null)
                return;
            float distance = Mathf.Max(requestedDistance, camera.nearClipPlane * 8f);
            float halfHeight = distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float size = Mathf.Max(0.01f, halfHeight * 2f * 0.035f);
            marker.transform.position = camera.transform.TransformPoint(
                new Vector3(0f, viewportY * halfHeight, distance));
            marker.transform.rotation = camera.transform.rotation;
            marker.transform.localScale = Vector3.one * size;
        }

        private void SetPhase0SourcesEnabled(bool enabled)
        {
            if (phase0Source != null)
            {
                MeshRenderer renderer = phase0Source.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = enabled;
            }
            if (phase0Occluder != null)
            {
                MeshRenderer renderer = phase0Occluder.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        internal void SuspendSternspreizPhase0()
        {
            if (!phase0Ready)
                return;
            // Unregister first: Unregister restores a renderer if a render
            // scope was active, then fixture teardown may safely leave it off.
            UnregisterPhase0RenderTarget();
            SetPhase0SourcesEnabled(false);
            SetPhase0EffectsEnabled(false);
        }

        private void SetPhase0EffectsEnabled(bool enabled)
        {
            SetPhase0EffectEnabled(leftPhase0Effect, enabled);
            SetPhase0EffectEnabled(rightPhase0Effect, enabled);
        }

        private static void SetPhase0EffectEnabled(Component component, bool enabled)
        {
            MonoBehaviour behaviour = component as MonoBehaviour;
            if (behaviour != null)
                behaviour.enabled = enabled;
        }

        private void FailSternspreizPhase0(string reason)
        {
            phase0Error = reason;
            Debug.LogWarning(LogPrefix + " PROBE BLOCKED: " + reason);
        }

        private static void RemovePhase0Effect(Camera camera)
        {
            if (camera == null)
                return;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().Name != SternspreizPipProbeEffect.ComponentName)
                    continue;
                behaviour.enabled = false;
                UnityEngine.Object.Destroy(behaviour);
            }
        }

        private void DestroySternspreizPhase0()
        {
            UnregisterPhase0RenderTarget();
            RemovePhase0Effect(LeftCamera);
            RemovePhase0Effect(RightCamera);
            if (ownsPhase0Fixture)
                DestroySternspreizPhase0Fixture();
            phase0Source = null;
            phase0Occluder = null;
            phase0Root = null;
            phase0Mesh = null;
            phase0SourceMaterial = null;
            phase0OccluderMaterial = null;
            phase0StarsMeshFilter = null;
            phase0RegisteredRenderer = null;
            leftPhase0Effect = null;
            rightPhase0Effect = null;
            phase0Layer = -1;
            phase0OccluderLayer = -1;
            phase0Ready = false;
            phase0SetupAttempted = false;
            ownsPhase0Fixture = false;
        }

        private void DestroySternspreizPhase0Fixture()
        {
            if (phase0Source != null)
            {
                MeshRenderer renderer = phase0Source.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
                UnityEngine.Object.Destroy(phase0Source);
            }
            if (phase0Occluder != null)
            {
                MeshRenderer renderer = phase0Occluder.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
                UnityEngine.Object.Destroy(phase0Occluder);
            }
            if (phase0Mesh != null)
                UnityEngine.Object.Destroy(phase0Mesh);
            if (phase0SourceMaterial != null)
                UnityEngine.Object.Destroy(phase0SourceMaterial);
            if (phase0OccluderMaterial != null)
                UnityEngine.Object.Destroy(phase0OccluderMaterial);
            if (phase0Root != null)
                UnityEngine.Object.Destroy(phase0Root);
        }

        internal string SternspreizPhase0Snapshot()
        {
            string left = SnapshotPhase0Effect(leftPhase0Effect);
            string right = SnapshotPhase0Effect(rightPhase0Effect);
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"requested\":{0},\"mode\":{1},\"ready\":{2},\"layer\":{3},"
                + "\"layerBit\":{4},\"occluderLayer\":{5},\"occluderBit\":{6},"
                + "\"viewMask\":{7},\"leftMask\":{8},\"rightMask\":{9},"
                + "\"sourceId\":{10},\"occluderId\":{11},\"meshId\":{12},"
                + "\"effectCountLeft\":{13},\"effectCountRight\":{14},"
                + "\"sourceScopeLeft\":{15},\"sourceScopeRight\":{16},"
                + "\"lens\":{17},"
                + "\"effects\":{{\"left\":{18},\"right\":{19}}},\"error\":\"{20}\"}}",
                SternspreizPipProbeEffect.CurrentMode == 0 ? "false" : "true",
                SternspreizPipProbeEffect.CurrentMode, phase0Ready ? "true" : "false",
                phase0Layer, phase0Layer >= 0 ? 1 << phase0Layer : 0,
                phase0OccluderLayer,
                phase0OccluderLayer >= 0 ? 1 << phase0OccluderLayer : 0,
                phase0ViewMask,
                LeftCamera != null ? LeftCamera.cullingMask : 0,
                RightCamera != null ? RightCamera.cullingMask : 0,
                phase0Source != null ? phase0Source.GetInstanceID() : 0,
                phase0Occluder != null ? phase0Occluder.GetInstanceID() : 0,
                phase0Mesh != null ? phase0Mesh.GetInstanceID() : 0,
                CountPhase0Effects(LeftCamera), CountPhase0Effects(RightCamera),
                SternspreizPipRenderScopePatch.SnapshotJson(LeftCamera),
                SternspreizPipRenderScopePatch.SnapshotJson(RightCamera),
                SnapshotLens(rightLensEffect),
                left, right, RadioNavProbeJson.Escape(phase0Error ?? string.Empty));
        }

        /// <summary>Lens telemetry: "null" when the effect is absent, so the
        /// snapshot stays valid while the lens preference is off.</summary>
        private static string SnapshotLens(Component component)
        {
            if (component == null)
                return "null";
            SternspreizLensEffect typed = component as SternspreizLensEffect;
            if (typed != null)
                return typed.SnapshotJson();

            MethodInfo snapshot = component.GetType().GetMethod("SnapshotJson",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (snapshot == null)
                return "null";
            object result = snapshot.Invoke(component, null);
            return result as string ?? "null";
        }

        private static int CountPhase0Effects(Camera camera)
        {
            if (camera == null)
                return 0;
            int count = 0;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null
                    && behaviours[i].GetType().Name == SternspreizPipProbeEffect.ComponentName)
                    count++;
            }
            return count;
        }

        private static string SnapshotPhase0Effect(Component component)
        {
            if (component == null)
                return "null";
            SternspreizPipProbeEffect typed = component as SternspreizPipProbeEffect;
            if (typed != null)
                return typed.SnapshotJson();
            MethodInfo snapshot = component.GetType().GetMethod("SnapshotJson",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (snapshot == null)
                return "null";
            object result = snapshot.Invoke(component, null);
            return result as string ?? "null";
        }

        /// <summary>
        /// Mirrors the live <c>AdditionalCameraData.RenderScene</c> of the
        /// view camera onto both PIP cameras. The game recomputes that flag
        /// every frame (<c>GenericCameraController.PreCull:483</c>: false
        /// while the interior <c>VirtualSceneRenderer</c> renders the scene),
        /// so a PIP stuck at the creation-time <c>true</c> draws the exterior
        /// scene on top of the interior composite РІР‚вЂќ the over-bright input the
        /// PPv2 stack blooms into the ocular's white blob. Live paused-frame
        /// A/B (2026-09-18): true = giant white blob with rainbow fringes,
        /// false = clean interior matching the eyes; only reproduces inside
        /// the boat, where the two values differ.
        /// <c>StencilMask</c> is deliberately NOT mirrored: the PIP's interior
        /// clone writes the stencil, so <c>true</c> on the PIP leaks the
        /// exterior through the hull as well (same A/B).
        /// </summary>
        private void SyncRenderScene(Camera viewCamera)
        {
            if (viewCamera == null)
                return;
            if (!ReferenceEquals(viewCamera, cachedViewCamera))
            {
                cachedViewCamera = viewCamera;
                cachedViewData = viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            }
            if (cachedViewData == null)
                return;
            bool value = cachedViewData.RenderScene;
            if (leftData != null && leftData.RenderScene != value)
                leftData.RenderScene = value;
            if (rightData != null && rightData.RenderScene != value)
                rightData.RenderScene = value;

            // The portal system masks the exterior render to the visible
            // portals' viewport bbox with this normalized rect while
            // RenderScene=true (ExteriorViewPortalsController.PreCull:109;
            // full (0,0,1,1) otherwise, :43). Mirroring RenderScene alone left
            // the PIP at the full rect, so whenever a door was in view the
            // PIP drew the exterior scene over the WHOLE frame РІР‚вЂќ the blocky
            // over-dark/over-bright ocular artifact that flashes with the
            // portal state (live 2026-09-18).
            Rect scissor = cachedViewData.CullingScissorRect;
            if (leftData != null && leftData.CullingScissorRect != scissor)
                leftData.CullingScissorRect = scissor;
            if (rightData != null && rightData.CullingScissorRect != scissor)
                rightData.CullingScissorRect = scissor;
        }

        /// <summary>
        /// Mirrors the view camera's screen-space contact-shadow block onto
        /// both PIP cameras (residual-light track, step 3). The PIP data
        /// components are created fresh with ContactShadows=false while every
        /// game camera in the path renders them (main + interior cameras both
        /// true live) РІР‚вЂќ the ocular missed the contact darkening on pipes and
        /// brackets. Source is the cached view-camera data (the main camera
        /// in FPP); each field is copied only on change, so a settled state
        /// performs no writes. Gated by PlayerPrefs
        /// RadioNav.ContactShadowMirror (default 1) for A/B without
        /// redeploying.
        /// </summary>
        private void SyncContactShadows()
        {
            if (PlayerPrefs.GetInt(ContactShadowMirrorPrefsKey, 1) == 0)
                return;
            if (cachedViewData == null)
                return;
            bool applied = CopyContactShadows(cachedViewData, leftData);
            applied |= CopyContactShadows(cachedViewData, rightData);
            if (applied && !contactShadowMirrorLogged)
            {
                contactShadowMirrorLogged = true;
                Debug.Log(LogPrefix + " CONTACTSHADOWS mirrored onto PIP (first apply)");
            }
        }

        private static bool CopyContactShadows(
            UBOAT.Game.Scene.Rendering.AdditionalCameraData from,
            UBOAT.Game.Scene.Rendering.AdditionalCameraData to)
        {
            if (from == null || to == null)
                return false;
            bool changed = false;
            if (to.ContactShadows != from.ContactShadows)
            {
                to.ContactShadows = from.ContactShadows;
                changed = true;
            }
            if (to.ContactShadowsLengthMultiplier != from.ContactShadowsLengthMultiplier)
            {
                to.ContactShadowsLengthMultiplier = from.ContactShadowsLengthMultiplier;
                changed = true;
            }
            if (to.ContactShadowsMaxDistance != from.ContactShadowsMaxDistance)
            {
                to.ContactShadowsMaxDistance = from.ContactShadowsMaxDistance;
                changed = true;
            }
            // The remaining four are get-only properties (CS0200): compare
            // through the getters, write through the private backing fields
            // (same TypeMemberCache pattern as SyncInteriorAmbient).
            changed |= CopyContactShadowField(from, to, "ContactShadowsLength", "contactShadowsLength");
            changed |= CopyContactShadowField(from, to, "ContactShadowsFadeDistance", "contactShadowsFadeDistance");
            changed |= CopyContactShadowField(from, to, "ContactShadowsDistanceScaleFactor", "contactShadowsDistanceScaleFactor");
            changed |= CopyContactShadowField(from, to, "ContactShadowsSampleCount", "contactShadowsSampleCount");
            if (to.ContactShadowsSampleCountMultiplier != from.ContactShadowsSampleCountMultiplier)
            {
                to.ContactShadowsSampleCountMultiplier = from.ContactShadowsSampleCountMultiplier;
                changed = true;
            }
            return changed;
        }

        private static bool CopyContactShadowField(
            UBOAT.Game.Scene.Rendering.AdditionalCameraData from,
            UBOAT.Game.Scene.Rendering.AdditionalCameraData to,
            string propertyName, string fieldName)
        {
            Type type = from.GetType();
            PropertyInfo property = TypeMemberCache.GetInstanceMember(type, propertyName) as PropertyInfo;
            FieldInfo field = TypeMemberCache.GetInstanceMember(type, fieldName) as FieldInfo;
            if (property == null || field == null)
            {
                if (WarnOnce.ShouldLog("SextantPipFactory.ContactField." + fieldName))
                    Debug.LogWarning(LogPrefix + " contact-shadow member '" + fieldName + "' not found - field skipped");
                return false;
            }
            try
            {
                object fromValue = property.GetValue(from, null);
                if (Equals(fromValue, property.GetValue(to, null)))
                    return false;
                field.SetValue(to, fromValue);
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantPipFactory.ContactFieldCopy." + fieldName))
                    Debug.LogWarning(LogPrefix + " contact-shadow copy '" + fieldName + "' failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Sea gate (R5): inside the boat the game stops rendering the sea
        /// for a camera by passing an empty custom water list; outside the
        /// list is cleared back to the default. Idempotent РІР‚вЂќ the lists are
        /// only touched when the inside state flips.
        /// </summary>
        public void SyncWaterRenderList(bool insideBoat)
        {
            if (waterListSynced && waterListInside == insideBoat)
                return;
            waterListSynced = true;
            waterListInside = insideBoat;
            ApplyWaterRenderList(leftWaterCamera, insideBoat);
            ApplyWaterRenderList(rightWaterCamera, insideBoat);
        }

        private void ApplyWaterRenderList(WaterCamera cam, bool insideBoat)
        {
            if (cam == null)
                return;
            try
            {
                cam.SetCustomWaterRenderList(insideBoat ? emptyWaterList : null);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantPipFactory.WaterList"))
                    Debug.LogWarning(LogPrefix + " water list sync failed: " + e.Message);
            }
        }

        private Camera ConfigurePipCamera(string name, RenderTexture target,
            Camera viewCamera, float fovDivisor, bool rightSide,
            SextantBloomCut bloomCut, SextantController owner)
        {
            GameObject go = new GameObject(name);
            if (viewCamera == null)
            {
                if (WarnOnce.ShouldLog("SextantController.ConfigurePipCamera"))
                    Debug.LogWarning(LogPrefix + " viewCamera is null, cannot configure " + name);
                UnityEngine.Object.Destroy(go);
                return null;
            }
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(viewCamera);
            // Camera.CopyFrom copies the source name too ("Main Camera"), which
            // renames the PIP GameObject and makes every PIP-derived lookup
            // (logs, clones, reflection probes) ambiguous РІР‚вЂќ restore ours.
            cam.name = name;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = target;
            cam.fieldOfView = viewCamera.fieldOfView / fovDivisor;
            cam.enabled = false;
            // Sun disc layer (7): rendered only by the PIP cameras; the bit
            // is absent from every stock camera mask, so the main view
            // never sees the disc.
            cam.cullingMask |= SunDiscRenderer.SunDiscLayerMask;

            // The game's render pipeline renders water only for cameras
            // registered on the main camera's AdditionalCameraData
            // (periscope pattern, Periscope.cs:581).
            UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd =
                go.AddComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            pipAdd.RenderScene = true;
            SextantRenderPasses.Setup(pipAdd,
                viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>(),
                cam.cullingMask);
            try
            {
                var mainAdd = viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                if (mainAdd != null)
                    pipAdd.ShadowDistance = mainAdd.ShadowDistance;
                // RenderScene is recomputed per frame by the game's camera
                // controllers (GenericCameraController.PreCull:483 keeps it
                // false while the interior VirtualSceneRenderer provides the
                // scene). Seed it from the live view camera here; a PIP stuck
                // at true renders the exterior scene ON TOP of the interior
                // composite, and the post stack blooms that into the ocular's
                // white blob (live paused-frame A/B, 2026-09-18). Refreshed
                // every frame in SyncRenderScene.
                if (mainAdd != null)
                {
                    pipAdd.RenderScene = mainAdd.RenderScene;
                    // Same seed for the portal scissor: while a door is in
                    // view the game masks the exterior render to the portals'
                    // bbox (ExteriorViewPortalsController.PreCull:109) and
                    // mirrors only RenderScene would let the PIP draw the
                    // exterior full-frame.
                    pipAdd.CullingScissorRect = mainAdd.CullingScissorRect;
                }
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantController.ShadowDistance"))
                    Debug.LogWarning("[RadioNav][Sextant] ShadowDistance mirror failed: " + e.Message);
            }

            if (rightSide)
                rightData = pipAdd;
            else
                leftData = pipAdd;

            // Mirror of the main camera's rendering stack: PPv2
            // PostProcessLayer + SSMSGlobalFog + SSMS, copied in the exact
            // component order of the FPP camera, so the SRP renders the PIP
            // views with the same fog/post look. The legacy PPv1
            // PostProcessingBehaviour is mirrored by live profile reference
            // (per-frame SyncPostProfile, gate RadioNav.Ppv1Mirror): the
            // Р’В§16.10 removal predates the Р’В§16.17 root cause, and the eyes
            // verifiably grade through it (FPP NG profile).
            ResolveMirrorSources(viewCamera);
            effectMirror.AddMirroredEffectComponents(viewCamera, go);

            // PlayWay renders the sea only for cameras carrying a WaterCamera
            // (waterCamerasCache registry; HullEffectsRenderer.GetWaterCamera
            // null-check skips water otherwise). Pattern: EntitySelectionCamera
            // (disabled camera, manual Render(), WaterCamera in prefab).
            try
            {
                WaterCamera waterCam = go.AddComponent<WaterCamera>();
                Debug.Log(LogPrefix + " WATERCAM added to " + name + ": " + (waterCam != null));
                if (rightSide)
                    rightWaterCamera = waterCam;
                else
                    leftWaterCamera = waterCam;

                // A raw WaterCamera is empty: PlayWay needs the config the
                // main camera's component carries (Water reference, custom
                // water render list, enable flags). Mirror it 1:1 so the sea
                // is drawn for the PIP camera as well.
                if (waterCam != null)
                {
                    WaterCamera mainWater = viewCamera.GetComponent<WaterCamera>();
                    if (mainWater != null)
                    {
                        effectMirror.MirrorComponent(mainWater, waterCam);
                        Debug.Log(LogPrefix + " WATERCAM copied to " + name + " from main camera");
                    }
                    else
                    {
                        Debug.LogWarning(LogPrefix + " WATERCAM mirror source missing, raw component on " + name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " WATERCAM add failed: " + e.Message);
            }

            cam.depthTextureMode = viewCamera.depthTextureMode;

            // Bloom-cut: PPv2 volume on a PIP-only layer. While a filter is
            // installed the sun's glow is disabled inside that half's lens;
            // the main camera's PostProcessLayer never consumes this layer,
            // so the FPP view keeps its normal bloom.
            bloomCut.EnsureVolume(go, rightSide);

            SextantCameraProbe probe = go.AddComponent<SextantCameraProbe>();
            probe.tracked = cam;
            probe.owner = owner;

            // Single-writer pose follower (Р’В§16.22): after the VSR positioned
            // the interior clone, puts the PIP root at the clone's world
            // position so the root renders the interior from interior space.
            // Rotation is untouched (Update set yaw/alidade; the VSR never
            // writes the root transform).
            SextantPipInteriorPose poseFollower = go.AddComponent<SextantPipInteriorPose>();
            poseFollower.Pip = cam;
            poseFollower.Mirror = interiorMirror;

            // Must run before the first SRP render: DeepCameraData caches the
            // VirtualSceneRenderer in its constructor.
            interiorMirror.Attach(cam);

            return cam;
        }

        private void ResolveMirrorSources(Camera viewCamera)
        {
            if (sourceSsmsFog != null || viewCamera == null)
                return;
            sourceSsmsFog = FindComponent(viewCamera, "SSMSGlobalFog");
            sourceSsms = FindComponent(viewCamera, "SSMS");
            sourcePpv1 = FindComponent(viewCamera, "PostProcessingBehaviour");
        }

        private static Component FindComponent(Camera cam, string typeName)
        {
            MonoBehaviour[] comps = cam.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().Name == typeName)
                    return comps[i];
            }
            return null;
        }
    }
}
