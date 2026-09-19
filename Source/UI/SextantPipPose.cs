using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-frame PIP camera pose behind SextantController (P5.4 slice 4b,
    /// SRP): main-camera tracking, SRP state sync and the debug heartbeat
    /// live here with explicit inputs; the controller owns open/close/input
    /// and passes its factory, arc and zoom divisor. The cameras are rendered
    /// by the game's SRP camera loop (both enabled), not by a manual Render()
    /// — see Update(). Creation still lives in SextantPipFactory. Moved
    /// verbatim (pose order, log strings).
    /// </summary>
    public static class SextantPipPose
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        // Per-camera caches for the per-frame pass-mask sync (P5.4 slice 4b):
        // the AdditionalCameraData component and the last applied culling mask.
        // Last good (non-PIP) view camera: fallback for the self-follow
        // guard below. Reset with the other per-camera caches on teardown.
        private static Camera lastGoodViewCamera;
        private static readonly Dictionary<Camera, UBOAT.Game.Scene.Rendering.AdditionalCameraData> addCache =
            new Dictionary<Camera, UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
        private static readonly Dictionary<Camera, int> maskCache =
            new Dictionary<Camera, int>();

        // Test-aim override (MCP bridge /sextant/aim): replaces the camera yaw
        // while active so an agent can point the alidade at a body without
        // mouse input. Gated by PlayerPrefs RadioNav.TestAim; cleared on close.
        private static float testAimYawDeg;
        private static bool testAimActive;

        /// <summary>Test override: both PIP cameras yaw to the given azimuth.</summary>
        public static void SetTestAim(float yawDeg)
        {
            testAimYawDeg = yawDeg;
            testAimActive = true;
        }

        /// <summary>Drops the test override (sextant close/teardown).</summary>
        public static void ClearTestAim()
        {
            testAimActive = false;
        }

        public static void Update(SextantPipFactory factory, double arcAngle,
            float fovDivisor, ref Camera viewCamera, Action manualRender)
        {
            if (factory.LeftCamera == null || factory.RightCamera == null)
                return;
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
                return;

            // Never follow our own PIP cameras (stuck-angle guard). The
            // resolver can transiently return a PIP (Camera.main null window
            // during transitions: the hook cache holds the last pre-culled
            // camera) — copying PIP->PIP freezes rotation while the interior
            // pose follower keeps position live ("stuck angle, can move").
            // Fall back to the last good view instead.
            if (SextantController.IsPipCamera(viewCamera))
            {
                if (WarnOnce.ShouldLog("SextantPipPose.PipSelfFollow"))
                    Debug.LogWarning(LogPrefix + " view camera resolved to a PIP camera - keeping last good view");
                if (lastGoodViewCamera != null)
                    viewCamera = lastGoodViewCamera;
                else
                    return;
            }
            else
            {
                lastGoodViewCamera = viewCamera;
            }

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;
            float yaw = testAimActive ? testAimYawDeg : mainEuler.y;
            factory.LeftCamera.transform.position = mt.position;
            factory.LeftCamera.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            factory.RightCamera.transform.position = mt.position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            factory.RightCamera.transform.rotation = Quaternion.Euler(-(float)arcAngle, yaw, 0f);

            // Inside the boat the hull blocks the sky, so each PIP renders the
            // interior directly: it takes the interior camera's culling mask
            // (the sea gate + RenderScene=true are applied around this call).
            // Single GBuffer writer per pass, whose depth matches the lighting
            // camera — the shared-_GBufferZ mismatch that corrupted one ocular
            // half (SEXTANT_PLAN §16.17) is gone by construction.
            MainCamera interiorMain = MainCamera.Instance;
            bool insideBoat = interiorMain?.IsInsideBoat == true;
            int interiorMask = insideBoat ? factory.InteriorMirror.InteriorMask : 0;

            SyncCameraState(factory.LeftCamera, viewCamera, fovDivisor, interiorMask);
            SyncCameraState(factory.RightCamera, viewCamera, fovDivisor, interiorMask);
            factory.RefreshMirroredState(viewCamera);

            // The sea must stay hidden like in the game's own inside view
            // (R5: the hull blocks the sea, so the ocular must not show it).
            factory.InteriorMirror.Sync(insideBoat);
            factory.SyncWaterRenderList(insideBoat);

            // Both PIP cameras are enabled (SextantController), so the game's
            // SRP camera loop renders each exactly once per frame with the pose
            // set above — after LateUpdate positioned the sun disc and the
            // interior clone. The old manual Render() here ran in Update (before
            // those LateUpdates) and was a no-op for the disabled left camera;
            // GPU-verified 2026-09-17 (RenderDoc capture: left RT <= 0.025 RGB,
            // right RT <= 0.917, both written 2-3x per frame).
            if (SextantVectorLog.Enabled && manualRender != null)
                manualRender();
        }

        public static void SyncCameraState(Camera cam, Camera source, float fovDivisor, int interiorMask)
        {
            cam.fieldOfView = source.fieldOfView / fovDivisor;
            // Camera params the game recomputes while the sextant is open:
            // the FPP far clip follows the inside state and the weather fog
            // (FPPCameraController.cs:942: 40 m inside, fog-dependent
            // outside), the near clip changes with the view (:615-616). The
            // PIP must stay 1:1 with the eyes, so copy them every frame.
            cam.nearClipPlane = source.nearClipPlane;
            cam.farClipPlane = source.farClipPlane;
            cam.depthTextureMode = source.depthTextureMode;
            // Inside the boat (interiorMask != 0) the PIP renders the interior
            // directly instead of the exterior view; the sun disc layer stays
            // visible for the alidade either way.
            int mask = (interiorMask != 0 ? interiorMask : source.cullingMask) | SunDiscRenderer.SunDiscLayerMask;
            cam.cullingMask = mask;

            // Per-frame hot path: cache the AdditionalCameraData per camera and
            // skip the pass walk entirely while the mask is unchanged. Pass
            // member lookups go through TypeMemberCache (no per-frame
            // GetProperty/GetField, no string key allocation).
            if (maskCache.TryGetValue(cam, out int lastMask) && lastMask == mask)
                return;
            maskCache[cam] = mask;

            try
            {
                UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd;
                if (!addCache.TryGetValue(cam, out pipAdd) || pipAdd == null)
                {
                    pipAdd = cam.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                    addCache[cam] = pipAdd;
                }
                ApplyMaskToPasses(pipAdd, mask);
            }
            catch (Exception e)
            {
                // Culling/sun-layer divergence must surface once instead of
                // silently rendering a wrong PIP stack every pose update.
                if (WarnOnce.ShouldLog("SextantController.SyncCameraState"))
                    Debug.LogWarning(LogPrefix + " SyncCameraState failed: " + e.Message);
            }
        }

        /// <summary>Writes a culling mask onto every SRP pass of one camera.</summary>
        internal static void ApplyMaskToPasses(UBOAT.Game.Scene.Rendering.AdditionalCameraData data, int mask)
        {
            if (data == null || !(data.Passes is Array passes))
                return;

            for (int i = 0; i < passes.Length; i++)
            {
                object p = passes.GetValue(i);
                if (p == null)
                    continue;
                ApplyPassMask(p, mask);
            }
        }

        /// <summary>Writes the culling mask onto one SRP pass (property or field).</summary>
        private static void ApplyPassMask(object pass, int mask)
        {
            Type passType = pass.GetType();
            MemberInfo member = TypeMemberCache.GetInstanceMember(passType, "CullingMask");
            if (member is PropertyInfo property)
            {
                if (property.CanWrite)
                {
                    property.SetValue(pass, mask);
                    return;
                }
            }
            else if (member is FieldInfo field)
            {
                field.SetValue(pass, mask);
                return;
            }

            MemberInfo fallback = TypeMemberCache.GetInstanceMember(passType, "cullingMask");
            if (fallback is FieldInfo fallbackField)
                fallbackField.SetValue(pass, mask);
        }

        /// <summary>
        /// Drops per-camera caches (PIP cameras are recreated per open).
        /// Called from the controller's teardown.
        /// </summary>
        public static void Reset()
        {
            addCache.Clear();
            maskCache.Clear();
            lastGoodViewCamera = null;
        }

        public static Vector3 Forward(SextantPipFactory factory, Camera viewCamera,
            MainCamera gameMainCamera, double arcAngle)
        {
            if (factory.RightCamera != null)
                return factory.RightCamera.transform.forward;
            float yaw = 0f;
            if (testAimActive)
                yaw = testAimYawDeg;
            else if (viewCamera != null)
                yaw = viewCamera.transform.eulerAngles.y;
            else if (gameMainCamera != null)
                yaw = gameMainCamera.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(-(float)arcAngle, yaw, 0f);
            return rot * Vector3.forward;
        }
    }
}
