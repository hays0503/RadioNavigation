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

        // Stabilization state (author decisions 2026-11-04): the aim is RAW +
        // washout offset — no yaw low-pass (it made the view slide behind the
        // mouse). The offset freezes the world yaw while the mouse rests and
        // bleeds out slowly afterwards. The POSITION is never touched (bino
        // parity: the game's bridge binocular keeps the camera at the
        // character's head undamped; filtering it showed as height jumps).
        // Synced (snapped) whenever the filter is bypassed — test aim,
        // disabled. Reset on the open edge and on teardown (ResetFilters).
        private static float yawOffset;
        // Keyboard aim trim (author request 2026-11-04): the left/right arrows
        // rotate the view like the bridge binocular's arrow rotation; reset by
        // R (with the arc) and on open.
        private static float yawTrimDeg;
        // Aim freeze: while the mouse is still the world yaw is frozen, so the
        // boat's wobble cannot move the star across the crosshair. The raw
        // mouse axes drive the idle timer — the game's input blockade does not
        // touch them.
        private static float idleSeconds;
        private static bool wasLocked;
        private static float lockedYaw;

        /// <summary>Test override: both PIP cameras yaw to the given azimuth.
        /// The keyboard trim resets with it so an agent's absolute aim is not
        /// offset by a leftover trim (2026-11-04).</summary>
        public static void SetTestAim(float yawDeg)
        {
            ResetYawTrim();
            testAimYawDeg = yawDeg;
            testAimActive = true;
        }

        /// <summary>Drops the test override (sextant close/teardown).</summary>
        public static void ClearTestAim()
        {
            testAimActive = false;
        }

        public static void Update(SextantPipFactory factory, double arcAngle,
            float fovDivisor, ref Camera viewCamera, Action manualRender, float dt)
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

            MainCamera interiorMain = MainCamera.Instance;
            bool insideBoat = interiorMain?.IsInsideBoat == true;

            // Stabilization inputs (2026-11-04): the smoothing gate, the aim
            // freeze gate and the raw mouse motion (read once per frame).
            bool stabilizationOn;
            bool locked = UpdateAimFreeze(insideBoat, dt, out stabilizationOn);
            Locked = locked;

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;
            float targetYaw = (testAimActive ? testAimYawDeg : mainEuler.y) + yawTrimDeg;
            float yaw = StabilizeYaw(targetYaw, dt, stabilizationOn, locked);
            // Position: bino parity (author decision 2026-11-04) — the game's
            // bridge binocular keeps its camera at the character's head with
            // no damping, and any position filtering/freeze showed as height
            // jumps in the ocular. Raw 1:1, always.
            Vector3 position = mt.position;
            factory.LeftCamera.transform.position = position;
            factory.LeftCamera.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            factory.RightCamera.transform.position = position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            factory.RightCamera.transform.rotation = Quaternion.Euler(-(float)arcAngle, yaw, 0f);

            // Inside the boat the hull blocks the sky, so each PIP renders the
            // interior directly: it takes the interior camera's culling mask
            // (the sea gate + RenderScene=true are applied around this call).
            // Single GBuffer writer per pass, whose depth matches the lighting
            // camera — the shared-_GBufferZ mismatch that corrupted one ocular
            // half (SEXTANT_PLAN §16.17) is gone by construction.
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
        /// Drops per-camera caches and the stabilization filters. Called from
        /// the controller's teardown; the open edge resets the filters via
        /// <see cref="ResetFilters"/> (the PIP cameras live for the whole
        /// scene session, so their caches survive an open/close cycle).
        /// </summary>
        public static void Reset()
        {
            addCache.Clear();
            maskCache.Clear();
            lastGoodViewCamera = null;
            ResetFilters();
        }

        /// <summary>
        /// Drops the stabilization state and the aim-freeze state (the
        /// per-camera caches stay). Called on the session-open edge
        /// (AttachAndConfigureCameras) and from <see cref="Reset"/>: the first
        /// pose of a session snaps instead of gliding from the pose frozen at
        /// Close (swarm review 2026-11-04).
        /// </summary>
        public static void ResetFilters()
        {
            yawOffset = 0f;
            yawTrimDeg = 0f;
            idleSeconds = 0f;
            wasLocked = false;
            lockedYaw = 0f;
            Locked = false;
        }

        /// <summary>Keyboard aim trim (author request 2026-11-04): the
        /// left/right arrows rotate the view like the bridge binocular's arrow
        /// rotation. Additive to the camera yaw, wrapped to ±180°. While the
        /// aim is frozen the trim moves the FROZEN pose instead of releasing
        /// the freeze (author feedback: the star must stay steady during the
        /// fine adjustment — the arc drum works the same way); the offset
        /// invariant keeps the boat-induced delta unchanged.</summary>
        public static void AddYawTrim(float deltaDeg)
        {
            if (float.IsNaN(deltaDeg))
                return;
            yawTrimDeg = (float)BodyRowFormatter.Wrap180(yawTrimDeg + deltaDeg);
            if (Locked)
                lockedYaw = (float)BodyRowFormatter.Wrap180(lockedYaw + deltaDeg);
        }

        /// <summary>Drops the keyboard trim (R key, sextant open). While the
        /// aim is frozen the frozen pose shifts back by the trim, so R centres
        /// the view immediately (the offset is deliberately NOT reset — that
        /// would snap the boat's wobble back in).</summary>
        public static void ResetYawTrim()
        {
            if (Locked)
                lockedYaw = (float)BodyRowFormatter.Wrap180(lockedYaw - yawTrimDeg);
            yawTrimDeg = 0f;
        }

        /// <summary>Current keyboard aim trim (degrees) — the readout/probe.</summary>
        public static float YawTrimDeg
        {
            get { return yawTrimDeg; }
        }

        /// <summary>Per-frame stabilization inputs (extracted from Update for
        /// the complexity gate): the smoothing gate, the raw mouse motion, the
        /// idle timer and the resulting aim-freeze flag. The freeze is off
        /// under the test aim, inside the boat and when either gate is off.</summary>
        private static bool UpdateAimFreeze(bool insideBoat, float dt,
            out bool stabilizationOn)
        {
            stabilizationOn = RadioNavDiagnostics.IsEnabledCached(
                SextantStabilization.PrefsKey, true);
            bool canLock = stabilizationOn && !testAimActive && !insideBoat
                && RadioNavDiagnostics.IsEnabledCached(
                    SextantStabilization.LockPrefsKey, true);
            if (!canLock)
            {
                idleSeconds = 0f;
                return false;
            }

            float mouseMotion = Mathf.Abs(Input.GetAxis("Mouse X"))
                + Mathf.Abs(Input.GetAxis("Mouse Y"));
            idleSeconds = SextantStabilization.AdvanceIdle(idleSeconds, mouseMotion, dt);
            return SextantStabilization.ShouldLock(idleSeconds);
        }

        /// <summary>True while the aim freeze is active (verification/MCP).</summary>
        public static bool Locked { get; private set; }

        /// <summary>Mouse-idle time driving the aim freeze (seconds).</summary>
        public static float IdleSeconds
        {
            get { return idleSeconds; }
        }

        /// <summary>Aim-stabilized yaw (2026-11-04, author feedback): RAW plus
        /// a washout offset — no low-pass, so moving the mouse moves the view
        /// exactly (a filtered yaw made it slide behind the hand). While the
        /// aim rests the offset freezes the world yaw at the captured value;
        /// while moving the offset bleeds out with the washout constant (no
        /// fast catch-up). Bypasses (gate off, test aim) snap the offset to
        /// zero.</summary>
        private static float StabilizeYaw(float target, float dt, bool enabled, bool locked)
        {
            if (!enabled || testAimActive)
            {
                yawOffset = 0f;
                wasLocked = false;
                return target;
            }

            if (locked)
            {
                // Freeze the world yaw: the offset tracks the live target so
                // the output stays exactly at the value captured on the edge.
                if (!wasLocked)
                    lockedYaw = target + yawOffset;
                float delta = (float)BodyRowFormatter.Wrap180(lockedYaw - target);
                // A live pose that jumped far away (teleport, camera
                // transition) invalidates the freeze — follow the live view.
                if (!SextantStabilization.OffsetUsable(delta,
                        SextantStabilization.MaxYawOffsetDeg))
                {
                    wasLocked = false;
                    yawOffset = 0f;
                    return target;
                }
                wasLocked = true;
                yawOffset = delta;
                return lockedYaw;
            }

            wasLocked = false;
            yawOffset = SextantStabilization.DecayOffset(yawOffset, dt);
            if (!SextantStabilization.OffsetUsable(yawOffset,
                    SextantStabilization.MaxYawOffsetDeg))
                yawOffset = 0f;
            return target + yawOffset;
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
