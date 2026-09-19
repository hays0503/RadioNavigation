using System;
using System.Collections.Generic;
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
    /// SextantController — the controller keeps pose, input and readout.
    /// Creation order and log strings are unchanged from the move.
    /// </summary>
    public sealed class SextantPipFactory
    {
        // RT size stays with creation (was SextantController.RtWidth/Height).
        public const int RtWidth = 512;
        public const int RtHeight = 512;

        // NOTE (2026-09-18): the PIP scene render intermittently loses its
        // lighting (one whole RT goes dark, ~3-7% of frames, either eye at
        // random) inside the game's SRP — a game-side transient that this mod
        // could not eliminate. Tried and measured ineffective: render-order
        // overrides (depth), a dummy absorber camera, distinct RT sizes, the
        // SSMS mirror toggle. See SEXTANT_PLAN.md §16.15.
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

        internal void Ensure(Camera viewCamera, float fovDivisor,
            RawImage leftViewImage, RawImage rightViewImage,
            SextantEffectMirror mirror, SextantBloomCut bloomCut,
            SextantController owner)
        {
            if (LeftCamera != null && RightCamera != null)
                return;

            effectMirror = mirror;

            LeftRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            LeftRt.name = "RadioNavSextantLeftRT";
            RightRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RightRt.name = "RadioNavSextantRightRT";

            LeftCamera = ConfigurePipCamera("RadioNavSextantLeftCamera", LeftRt,
                viewCamera, fovDivisor, false, bloomCut, owner);
            RightCamera = ConfigurePipCamera("RadioNavSextantRightCamera", RightRt,
                viewCamera, fovDivisor, true, bloomCut, owner);

            if (leftViewImage != null) leftViewImage.texture = LeftRt;
            if (rightViewImage != null) rightViewImage.texture = RightRt;

            Debug.Log(LogPrefix + " PIP cameras created, RT=" + RtWidth + "x" + RtHeight);
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
            // The game's list itself can be null after an update — guard the
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
        /// Mirrors the live <c>AdditionalCameraData.RenderScene</c> of the
        /// view camera onto both PIP cameras. The game recomputes that flag
        /// every frame (<c>GenericCameraController.PreCull:483</c>: false
        /// while the interior <c>VirtualSceneRenderer</c> renders the scene),
        /// so a PIP stuck at the creation-time <c>true</c> draws the exterior
        /// scene on top of the interior composite — the over-bright input the
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
            // PIP drew the exterior scene over the WHOLE frame — the blocky
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
        /// true live) — the ocular missed the contact darkening on pipes and
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
        /// list is cleared back to the default. Idempotent — the lists are
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
            // (logs, clones, reflection probes) ambiguous — restore ours.
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
            // §16.10 removal predates the §16.17 root cause, and the eyes
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

            // Single-writer pose follower (§16.22): after the VSR positioned
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
