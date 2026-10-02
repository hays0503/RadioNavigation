using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

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
        // The PP layer owns the buffer the smear actually lives in, so the
        // reset has to reach it as well as the camera-data flag. Cleared with
        // addCache on teardown.
        private static readonly Dictionary<Camera, PostProcessLayer> layerCache =
            new Dictionary<Camera, PostProcessLayer>();
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
        // Eye-shadow input: the anchor for the aim's angular rate and the last
        // measured rate. Reset with the other filters on the open edge.
        private static SextantAimRate.State aimRateState = SextantAimRate.State.Fresh;
        private static SextantAimRateSample aimRateSample = new SextantAimRateSample(0d, 0d);
        // Debug aim probe state and the acceleration currently requested. Read
        // from PlayerPrefs at 1 Hz; both are zero unless a probe is armed, so a
        // normal session never enters this path.
        private static SextantAimProbe.State probe = SextantAimProbe.State.Rest;
        // View-camera pose captured on the first frame the probe drives it, and
        // whether it is doing so right now. Without the capture the offset would
        // be read back from the camera every frame and integrate into the
        // player's own aim; without the flag the restore would be guessed at.
        private static Vector3 viewProbeHome;
        private static bool viewProbeDriving;
        private static int probeRun;
        private static double probeSpinRate;
        private static double probeYawAccel;
        private static double probePitchAccel;
        private static float probePollTimer;
        private static float probeLogTimer;
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

            // Optional Sternspreiz diagnostic. This is a no-op unless the
            // dedicated PlayerPrefs gate is explicitly 1, 2, or 3. Prepare
            // before applying masks so the fixture/source scope and callback
            // are ready for the same PIP frame.
            factory.PrepareSternspreizPhase0(viewCamera);

            // Sternspreizglas lens (production feature, PlayerPrefs gate): the
            // streak effect is attached to both PIPs and gated by the sky state
            // inside the effect, so this call is a cheap no-op while off.
            factory.PrepareLens();

            MainCamera interiorMain = MainCamera.Instance;
            bool insideBoat = interiorMain?.IsInsideBoat == true;

            // Stabilization inputs (2026-11-04): the smoothing gate, the aim
            // freeze gate and the raw mouse motion (read once per frame).
            bool stabilizationOn;
            bool locked = UpdateAimFreeze(insideBoat, dt, out stabilizationOn);
            Locked = locked;

            PollAimProbe(dt);

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;

            // Debug aim probe (2026-10-02): a reproducible angular acceleration
            // so a motion-locked defect can be reproduced without a mouse. It is
            // an OFFSET on top of the player's own aim, and while it is running
            // the washout freeze is bypassed - the freeze exists to hold the
            // world still while the mouse is idle, which is precisely what must
            // NOT happen during a probe, or the picture would sit still and only
            // the shadow would move. The probe has to reach the final camera
            // rotation, not just the aim, because the artefact under
            // investigation is in the picture.
            probe = StepProbe(dt);
            if (probe.Moving)
                stabilizationOn = false;
            // The base pose the rest of this method must measure FROM. While the
            // probe drives the view camera the camera's own euler already carries
            // the offset, so reading it would apply the probe twice: the ocular
            // would spin at twice the commanded rate and the aim rate the eye
            // shadow is fed from would double as well. One source, both consumers.
            Vector3 baseEuler = ApplyProbeToViewCamera(mt, mainEuler, probe);
            LogAimProbe(dt);

            float targetYaw = (testAimActive ? testAimYawDeg : baseEuler.y) + yawTrimDeg
                + (float)probe.Yaw;
            // Eye-shadow input (author 2026-10-02): the angular RATE of where the
            // observer is asking to look, measured on the aim BEFORE the washout
            // stabilization. Two reasons it is not the stabilized output: the
            // freeze holds that output still, so a shadow driven from it would
            // vanish exactly while the hand is moving and the washout offset
            // (the model's real eye displacement) is at its largest; and the
            // alidade is absent by construction, since it never enters
            // targetYaw — it moves the instrument, not the view.
            //
            // The test aim pins both axes, so it produces no rate and therefore
            // no shadow, which is also the cleanest proof of the author's rule
            // (a view that cannot turn has no lune): with RadioNav.TestAim=1 the
            // aperture must stay clean however fast the mouse moves.
            double aimPitch = (testAimActive ? 0d : baseEuler.x) + probe.Pitch;
            aimRateState = SextantAimRate.Advance(aimRateState, targetYaw, aimPitch, dt,
                out aimRateSample);
            MaybeResetTemporalEffects(factory, viewCamera, aimRateSample.Magnitude);
            float yaw = StabilizeYaw(targetYaw, dt, stabilizationOn, locked);
            float pitch = (float)aimPitch;
            // Both beams follow the head pitch; the alidade adds the gap between
            // them. The angles come from SextantBeamPoseMath so the shipped
            // orientation and the offline tests cannot drift apart (the pitch
            // used to be pinned to 0 here — see the class remarks).
            SextantBeamPose horizon = SextantBeamPoseMath.HorizonBeam(pitch, yaw);
            SextantBeamPose index = SextantBeamPoseMath.IndexBeam(pitch, yaw, arcAngle);
            // Position: bino parity (author decision 2026-11-04) — the game's
            // bridge binocular keeps its camera at the character's head with
            // no damping, and any position filtering/freeze showed as height
            // jumps in the ocular. Raw 1:1, always.
            Vector3 position = mt.position;
            factory.LeftCamera.transform.position = position;
            factory.LeftCamera.transform.rotation =
                Quaternion.Euler((float)horizon.PitchDeg, (float)horizon.YawDeg, 0f);
            factory.RightCamera.transform.position = position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            factory.RightCamera.transform.rotation =
                Quaternion.Euler((float)index.PitchDeg, (float)index.YawDeg, 0f);

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

        /// <summary>
        /// Far clip for the PIP cameras, in metres. NOT mirrored from the FPP eye
        /// camera any more (author's report, 2026-10-02).
        ///
        /// The eye camera's far clip is tuned for the bridge window - 40 m
        /// inside, fog-dependent outside (FPPCameraController.cs:942) - and the
        /// game rewrites it every frame, so copying it into the PIP sliced the
        /// ocean. Measured: a razor-sharp horizontal boundary crossed the ocular
        /// about 7 degrees below the axis (r = +0.37) with sky above and sea
        /// below, it moved as the boat pitched, and it was present ONLY inside
        /// the ocular - the main camera's far is 4500 and never showed it. It
        /// survived with the eye shadow switched off entirely, which is what
        /// cleared the lune, the lens, the constellation lines and the bezel.
        ///
        /// The value itself lives in the pure contract SextantPipClip, because a
        /// number that caused a visible defect has to be assertable offline.
        /// </summary>
        public const float PipFarClipPlane = (float)SextantPipClip.FarPlane;

        /// <summary>
        /// Near clip floor. The eye camera's own near plane is mirrored so the
        /// interior keeps its depth precision; the floor only stops a zero or a
        /// negative value, which would collapse the projection.
        /// </summary>
        public const float PipMinNearClipPlane = (float)SextantPipClip.MinNearPlane;

        /// <summary>
        /// One step of whichever probe mode is selected: the continuous spin when
        /// a spin rate is set, otherwise the acceleration swing. Split out of
        /// Update because it is a mode choice, not part of the pose pipeline -
        /// and because inlining it pushed Update to the complexity ceiling.
        /// </summary>
        private static SextantAimProbe.State StepProbe(float dt)
        {
            if (Math.Abs(probeSpinRate) > 1e-9)
                return SextantAimProbe.AdvanceSpin(probe, probeSpinRate, dt);
            return SextantAimProbe.Advance(probe, probeYawAccel, probePitchAccel, dt);
        }

        /// <summary>
        /// Reads the probe preferences once a second and arms a fresh probe when
        /// one is requested. Re-arming replaces the state rather than adding to
        /// it, so setting the same vector twice gives the same swing - which is
        /// the whole point of a probe.
        /// </summary>
        private static void PollAimProbe(float dt)
        {
            probePollTimer -= dt;
            if (probePollTimer > 0f)
                return;
            probePollTimer = 1f;

            double yaw = SextantAimProbe.AccelerationFrom(
                PlayerPrefs.GetInt(SextantAimProbe.YawAccelPrefsKey, 0));
            double pitch = SextantAimProbe.AccelerationFrom(
                PlayerPrefs.GetInt(SextantAimProbe.PitchAccelPrefsKey, 0));
            double seconds = SextantAimProbe.SecondsFrom(
                PlayerPrefs.GetInt(SextantAimProbe.SecondsPrefsKey, 0));
            double spin = SextantAimProbe.SpinRateFrom(
                PlayerPrefs.GetInt(SextantAimProbe.SpinRatePrefsKey, 0));
            int run = PlayerPrefs.GetInt(SextantAimProbe.RunPrefsKey, 0);

            // Spin takes precedence: it is the mode the author asked for
            // ("rotate continuously"), and mixing it with an acceleration swing
            // would make the rate depend on two preferences at once.
            probeSpinRate = spin;
            if (Math.Abs(spin) > 1e-9 && seconds > 0d)
            {
                if (probe.SecondsLeft <= 0d && SextantAimProbe.IsFreshRun(run, probeRun))
                {
                    probeRun = run;
                    probe = SextantAimProbe.ArmedSpin(seconds);
                    probeYawAccel = 0d;
                    probePitchAccel = 0d;
                }
                return;
            }

            if (seconds <= 0d || (Math.Abs(yaw) < 1e-9 && Math.Abs(pitch) < 1e-9))
            {
                // Off: drop any running probe, so a cleared preference stops the
                // view immediately instead of coasting to its timer. The run
                // counter is NOT consumed here - fixing a mistyped acceleration
                // and firing again must still work.
                probe = SextantAimProbe.State.Rest;
                probeYawAccel = 0d;
                probePitchAccel = 0d;
                probeSpinRate = 0d;
                return;
            }

            // A fresh run counter, not merely an armed preference: without this an
            // expired probe is re-armed by the very next poll, so a forgotten
            // preference swings the view in bursts forever and the timer stops
            // being a safety property at all. One swing per deliberate bump.
            if (probe.SecondsLeft <= 0d && SextantAimProbe.IsFreshRun(run, probeRun))
            {
                probeRun = run;
                probe = SextantAimProbe.Armed(yaw, pitch, seconds);
                probeYawAccel = yaw;
                probePitchAccel = pitch;
            }
        }

        /// <summary>1 Hz probe telemetry: the state a captured frame belongs to.</summary>
        /// <summary>
        /// Turns the view camera while the probe runs, and puts it back when the
        /// probe stops. Returns the pose the caller must measure from.
        ///
        /// Why this exists (measured 2026-10-02): the probe only ever moved the
        /// instrument's own cameras, so a 220 deg/s spin left the PLAYER's view -
        /// and therefore the rendered frame - bit-identical to rest. The picture
        /// was the thing under test, so a probe that cannot reach it measures
        /// nothing; that is a blind instrument, not a clean result.
        ///
        /// Three properties, each of which is a bug if lost:
        ///   - gated on RadioNav.ProbeViewSpin, so the shipped default does not
        ///     move the player's view;
        ///   - the pose is written from a pose captured once, never read back, so
        ///     the offset cannot integrate and the view returns exactly;
        ///   - drive and restore share SextantAimProbe.ViewAngle, so the two
        ///     cannot drift apart, and the restore runs on the first frame the
        ///     probe is no longer moving - including the frame after the
        ///     instrument is closed, which is what clears it on re-open.
        /// </summary>
        private static Vector3 ApplyProbeToViewCamera(Transform mt, Vector3 mainEuler,
            SextantAimProbe.State probeState)
        {
            bool drive = probeState.Moving
                && SextantAimProbe.ViewSpinFrom(
                    PlayerPrefs.GetInt(SextantAimProbe.ViewSpinPrefsKey, 0));

            if (drive)
            {
                if (!viewProbeDriving)
                {
                    viewProbeHome = mainEuler;
                    viewProbeDriving = true;
                }
                mt.eulerAngles = new Vector3(
                    (float)SextantAimProbe.ViewAngle(viewProbeHome.x, probeState.Pitch),
                    (float)SextantAimProbe.ViewAngle(viewProbeHome.y, probeState.Yaw),
                    viewProbeHome.z);
                return viewProbeHome;
            }

            if (!viewProbeDriving)
                return mainEuler;

            mt.eulerAngles = viewProbeHome;
            viewProbeDriving = false;
            return viewProbeHome;
        }

        private static void LogAimProbe(float dt)
        {
            if (!probe.Moving)
                return;
            probeLogTimer -= dt;
            if (probeLogTimer > 0f)
                return;
            probeLogTimer = 1f;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[RadioNav][Sextant] PROBE run={0} yaw={1:F2} spin={2:F1}deg/s total={3:F0}deg left={4:F1}s accel={5:F1},{6:F1}",
                probeRun, probe.Yaw, probeSpinRate, probe.SpinTotal, probe.SecondsLeft,
                probeYawAccel, probePitchAccel));
        }

        public static void SyncCameraState(Camera cam, Camera source, float fovDivisor,
            int interiorMask)
        {
            cam.fieldOfView = source.fieldOfView / fovDivisor;
            // The near clip and the depth mode still track the eyes: the near
            // clip changes with the view (:615-616) and the interior needs the
            // precision. The far clip deliberately does NOT - see
            // PipFarClipPlane for the measurement that decided it.
            cam.nearClipPlane = Mathf.Max(source.nearClipPlane, PipMinNearClipPlane);
            cam.farClipPlane = PipFarClipPlane;
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
            layerCache.Clear();
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
            // The aim-rate latch must forget its anchor here too: the session
            // open edge would otherwise read as a jump and the eye shadow would
            // flash for one frame.
            aimRateState = SextantAimRate.Reset();
            aimRateSample = new SextantAimRateSample(0d, 0d);
            // ...and a running debug probe must stop here: re-opening the
            // instrument is an explicit act, and a coasting probe would move the
            // view the moment the author looks into the ocular again.
            probe = SextantAimProbe.State.Rest;
            probeYawAccel = 0d;
            probePitchAccel = 0d;
            probePollTimer = 0f;
            // probeRun is deliberately NOT cleared: it records which request was
            // last consumed, and forgetting it would make re-opening the
            // instrument fire a swing the author had already used.
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

        /// <summary>Angular rate of the aim, in deg/s (eye-shadow input).</summary>
        public static SextantAimRateSample AimRate
        {
            get { return aimRateSample; }
        }

        /// <summary>
        /// Drops the pipeline's temporal history while the aim is swinging
        /// fast (see SextantTemporalReset). The smear on fast mouse turns
        /// follows the MAIN camera — a 220 deg/s probe spin (PIP only) renders
        /// sharp — so the reset goes to the view camera first; the PIP pair
        /// gets it too, in case their own history ever matters. Decided by the
        /// pure ShouldReset (rate + kill-switch), never unconditionally.
        /// </summary>
        private static void MaybeResetTemporalEffects(SextantPipFactory factory,
            Camera viewCamera, double rateDegPerSec)
        {
            if (!SextantTemporalReset.ShouldReset(rateDegPerSec, SextantTemporalReset.IsEnabled()))
                return;
            ResetTemporalEffects(viewCamera);
            if (factory == null)
                return;
            ResetTemporalEffects(factory.LeftCamera);
            ResetTemporalEffects(factory.RightCamera);
        }

        private static void ResetTemporalEffects(Camera camera)
        {
            if (camera == null)
                return;
            // The game clears BOTH halves (GameCamera.ResetTemporalEffectsNow,
            // MainCamera.cs:148-163): the PP layer's own history buffer AND the
            // AdditionalCameraData flag. Setting only the flag was MEASURED to
            // leave the smear fully intact at 70 deg/s - the flag is a request
            // the pipeline honours on its own schedule, while the history is
            // the buffer the fast rotation smears into. Note the game's own
            // InterruptTemporalAntiAliasing / InterruptMotionBlur
            // (MainCamera.cs:1083-1089) are empty bodies in this build, so
            // there is no higher-level call to make: this is the whole reset.
            PostProcessLayer layer;
            if (!layerCache.TryGetValue(camera, out layer) || layer == null)
            {
                layer = camera.GetComponent<PostProcessLayer>();
                layerCache[camera] = layer;
            }
            layer?.ResetHistory();
            UBOAT.Game.Scene.Rendering.AdditionalCameraData data;
            if (!addCache.TryGetValue(camera, out data) || data == null)
            {
                data = camera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                addCache[camera] = data;
            }
            if (data != null)
                data.TemporalEffectsResetRequested = true;
        }

        /// <summary>Debug aim probe state, for reflection/telemetry.</summary>
        public static SextantAimProbe.State AimProbe
        {
            get { return probe; }
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
            float pitch = 0f;
            if (testAimActive)
                yaw = testAimYawDeg;
            else if (viewCamera != null)
            {
                Vector3 euler = viewCamera.transform.eulerAngles;
                yaw = euler.y;
                pitch = euler.x;
            }
            else if (gameMainCamera != null)
            {
                Vector3 euler = gameMainCamera.transform.eulerAngles;
                yaw = euler.y;
                pitch = euler.x;
            }
            SextantBeamPose index = SextantBeamPoseMath.IndexBeam(pitch, yaw, arcAngle);
            Quaternion rot = Quaternion.Euler((float)index.PitchDeg, (float)index.YawDeg, 0f);
            return rot * Vector3.forward;
        }
    }
}
