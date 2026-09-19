using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Environment;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sextant device (SEXTANT_PLAN v1.2). Z toggles a modal overlay (Shift+Z
    /// belongs to the constellation toggle): dark veil over the live view,
    /// circular ocular split in two halves. The left half is a fixed-horizon
    /// PIP camera (pitch 0), the right half is a second PIP camera pitched to
    /// the arc angle (0-130). Both cameras use the game's own periscope render
    /// pattern (AttachedCameras + SRP passes), so sky/sea/stars are drawn by
    /// the game's shaders and the mod only changes the angle.
    /// Shade filters 1/2/3, gaussian glare with pain hysteresis, star/sun
    /// identification after a 5 s hold.
    /// Layout is resolution-independent: own overlay canvas + CanvasScaler
    /// (1920x1080 reference, match height), SEXTANT_PLAN §13.
    /// </summary>
    public sealed class SextantController : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        // Arc input step math + fine-drum repeat live in SextantArcInput
        // (extracted, P5.4 slice 4b); the controller keeps key polling.
        private readonly SextantArcInput arcInput = new SextantArcInput();
        // Vector telemetry lives in SextantVectorLog (extracted).
        private readonly SextantVectorLog vectorLog = new SextantVectorLog();
        // Ocular zoom steps live in SextantZoom (extracted, P5.4 slice 4a).
        private readonly SextantZoom zoom = new SextantZoom();
        // Key polling lives in SextantInputPoller, glare/pain in
        // SextantGlareCoordinator (extracted); the controller keeps host
        // services (arc field, filters, logs, telemetry).


        /// <summary>True while the overlay is up; ConstellationMesh gates Z on it (§7.4).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Live device instance for the MCP probe (set in Awake).</summary>
        internal static SextantController Instance { get; private set; }

        /// <summary>
        /// True when the camera is one of the two sextant PIP halves. Used by
        /// PipCloneCameraPatch to keep the PIP render pass at a single GBuffer
        /// writer (§16.21).
        /// </summary>
        public static bool IsPipCamera(Camera camera)
        {
            if (camera == null || Instance == null)
                return false;
            return camera == Instance.pipFactory.LeftCamera
                || camera == Instance.pipFactory.RightCamera;
        }

        internal double arcAngle;
        private int leftFilterCount;
        private int rightFilterCount;
        private SextantInputPoller inputPoller;
        private readonly SextantGlareCoordinator glare = new SextantGlareCoordinator();
        // Reading-column text lives in SextantReadout (extracted, P5.4
        // slice 4d); the controller applies Text/color.
        private readonly SextantReadout readout = new SextantReadout();
        // RT pixel probe lives in SextantRenderCheck (extracted).
        private readonly SextantRenderCheck renderCheck = new SextantRenderCheck();
        // Star/sun identification cycle lives in SextantStarId (extracted).
        private readonly SextantStarId starId = new SextantStarId();

        private Camera viewCamera;
        private MainCamera gameMainCamera;
        private GameInput gameInput;

        // Game sun access for SUNDRIFT diagnostics lives in SextantGameSun
        // (extracted): TOD_Sky is read via cached reflection there.
        private readonly SextantGameSun gameSun = new SextantGameSun();

        // PIP camera lifecycle (creation/attach/teardown) lives in
        // SextantPipFactory (extracted, P5.4 slice 4a); the controller keeps
        // pose, input and readout. Water renders only for cameras listed on
        // the main camera's AdditionalCameraData (periscope pattern).
        private readonly SextantPipFactory pipFactory = new SextantPipFactory();
        private RawImage leftViewImage;
        private RawImage rightViewImage;

        // 1:1 mirror of the main camera's rendering stack lives in
        // SextantEffectMirror (extracted); mirror sources live in the PIP
        // factory, refreshed from the pose each frame.
        private readonly SextantEffectMirror effectMirror = new SextantEffectMirror();

        private GameObject canvasRoot;
        private GameObject helpPanel;
        private Image filterDimLeftImage;
        private Image filterDimRightImage;
        private Image glareImage;
        private Text angleText;
        private Text nameText;
        private Text painText;
        private Text filterText;
        // Sun/bodies readout lives in SextantBodiesReadout (extracted).
        private SextantBodiesReadout bodiesReadout;
        private Texture2D ringTexture;
        private Texture2D circleMaskTexture;
        private Texture2D eyecupTexture;
        private System.Collections.Generic.List<Sprite> ownedSprites;

        // Cached delegates for the per-frame input poller: method-group
        // conversions would allocate a new delegate each frame.
        private Action<float> logArcInputDelegate;
        private Action<int, bool> setFiltersDelegate;
        private Action zoomInDelegate;
        private Action zoomOutDelegate;
        private Action resetArcDelegate;
        private Action manualRenderDelegate;
        private Action<bool, float> sunReadoutDelegate;
        private Action<float> sunDriftLogDelegate;
        private Action painStarIdResetDelegate;

        // PPv2 bloom-cut volumes live in SextantBloomCut (extracted).
        private readonly SextantBloomCut bloomCut = new SextantBloomCut();

        private readonly IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();

        // Running-fix worksheet state (v1.9) lives in SextantWorksheet
        // (extracted): sights, card text, course/speed track sampler.
        private SextantWorksheet worksheet;

        // SUNDRIFT calibration lifecycle lives in SunCalibrator
        // (extracted, P5.4 slice 4c): solved on open, persisted, applied
        // to all worksheet ephemeris.
        private readonly SunCalibrator calibrator = new SunCalibrator();

        private void Awake()
        {
            Instance = this;
            worksheet = new SextantWorksheet(positionProvider);
            bodiesReadout = new SextantBodiesReadout(positionProvider);
            inputPoller = new SextantInputPoller(arcInput, worksheet, starId);
            logArcInputDelegate = LogArcInputVector;
            setFiltersDelegate = SetFilters;
            zoomInDelegate = ZoomIn;
            zoomOutDelegate = ZoomOut;
            resetArcDelegate = ResetArc;
            manualRenderDelegate = () => vectorLog.OnManualRender(arcAngle);
            sunReadoutDelegate = UpdateSunReadout;
            sunDriftLogDelegate = LogSunDrift;
            painStarIdResetDelegate = starId.Reset;
            calibrator.Load();
        }

        [Inject] private static ResourceManager resourceManager = null;

        private void Update()
        {
            if (IsOpen)
            {
                // Shift+Z while open does nothing: constellations are gated by
                // IsOpen, and the device must not close on their combo.
                if (Input.GetKeyDown(KeyCode.Z) && !ShiftHeld())
                {
                    Close();
                    return;
                }
                inputPoller.PollArc(Time.unscaledDeltaTime, ref arcAngle, ShiftHeld(), logArcInputDelegate);
                inputPoller.PollFilters(setFiltersDelegate);
                inputPoller.PollZoom(zoomInDelegate, zoomOutDelegate, resetArcDelegate);
                UpdatePipPose();
                if (vectorLog.OnViewRotate(viewCamera))
                    LogVectorState("rotate");
                renderCheck.Update(Time.unscaledDeltaTime, arcAngle, pipFactory.LeftRt, pipFactory.RightRt);
                UpdateGlare(Time.unscaledDeltaTime);
                UpdateStarId(Time.unscaledDeltaTime);
                UpdateReadout(false);
                UpdateBodiesReadout(Time.unscaledDeltaTime);
                UpdateMoonParity(Time.unscaledDeltaTime);
                inputPoller.PollFix(arcAngle);
                if (Input.GetKeyDown(KeyCode.P))
                    ToggleHelp();
                worksheet.UpdateCourseSampler(Time.unscaledDeltaTime);
                worksheet.UpdatePanel(Time.unscaledDeltaTime);
            }
            else if (Input.GetKeyDown(KeyCode.Z) && !ShiftHeld())
            {
                TryOpen();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (IsOpen && gameInput != null)
                gameInput.RemoveBlockade(this);
            pipFactory.Destroy();
            SextantPipPose.Reset();
            SextantPipPose.ClearTestAim();
            IsOpen = false;

            if (canvasRoot != null) Destroy(canvasRoot);
            bloomCut.Destroy();
            if (ringTexture != null) Destroy(ringTexture);
            if (circleMaskTexture != null) Destroy(circleMaskTexture);
            if (eyecupTexture != null) Destroy(eyecupTexture);
            if (ownedSprites != null)
            {
                for (int i = 0; i < ownedSprites.Count; i++)
                {
                    if (ownedSprites[i] != null)
                        Destroy(ownedSprites[i]);
                }
                ownedSprites.Clear();
            }
            renderCheck.Destroy();
        }

        // ---- open / close -------------------------------------------------

        private void TryOpen()
        {
            if (!CanOpen())
                return;

            EnsureOverlay();
            pipFactory.Ensure(viewCamera, zoom.FovDivisor, leftViewImage, rightViewImage,
                effectMirror, bloomCut, this);
            calibrator.Solve(positionProvider, gameSun, worksheet);

            ResetReadingState();

            AttachAndConfigureCameras();

            UpdateFilterLabel();
            UpdateFilterDim();
            if (painText != null) painText.enabled = false;
            UpdateReadout(true);
            Debug.Log(LogPrefix + " opened (two-camera split ocular)");
        }

        private bool CanOpen()
        {
            if (!IsLiveViewMode())
            {
                Debug.Log(LogPrefix + " Z ignored outside live view (mode=" + CurrentModeName() + ")");
                return false;
            }
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
            {
                Debug.Log(LogPrefix + " Z ignored - no rendering camera");
                return false;
            }
            if (gameInput == null && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput == null)
            {
                Debug.LogWarning(LogPrefix + " GameInput not available - cannot open");
                return false;
            }
            return true;
        }

        private void ResetReadingState()
        {
            // arcAngle is intentionally NOT reset here: the reading must
            // survive close/open cycles (explicit reset = R key / 0° button).
            leftFilterCount = 0;
            rightFilterCount = 0;
            starId.Reset();
            glare.ResetPain();
            readout.Reset();
            worksheet.InvalidateCard();
            bodiesReadout.Invalidate();
            gameSun.ResetDriftTimer(); // force the first SUNDRIFT line on open
            renderCheck.ResetForOpen();
            SunDiscRenderer.RandomizeDisc(); // fresh sunspot pattern per open
            SunDiscRenderer.Active = false;
            gameSun.ResetMoonDiagTimer();
            if (helpPanel != null)
                helpPanel.SetActive(false);
        }

        /// <summary>P key: show/hide the on-screen F-Tafel legend.</summary>
        private void ToggleHelp()
        {
            if (helpPanel == null)
                return;
            bool show = !helpPanel.activeSelf;
            helpPanel.SetActive(show);
            Debug.Log(LogPrefix + " help " + (show ? "shown" : "hidden"));
        }

        private void AttachAndConfigureCameras()
        {
            gameInput.AddBlockade(this);
            canvasRoot.SetActive(true);
            pipFactory.AttachToMain();
            // Both PIP cameras take the SAME render path: enabled=true, so the
            // game's SRP camera loop renders each one once per frame with the
            // pose set in Update (i.e. after LateUpdate positioned the sun disc
            // and the interior clone). The old split (left disabled + manual
            // Render() in Update) left the left RT effectively black —
            // GPU-verified 2026-09-17 (RenderDoc): left RT RGB <= 0.025 vs
            // right RT RGB <= 0.917, the disabled camera's manual render being
            // a no-op. Water still needs the AttachedCameras registration.
            if (pipFactory.LeftCamera != null)
                pipFactory.LeftCamera.enabled = true;
            if (pipFactory.RightCamera != null)
                pipFactory.RightCamera.enabled = true;
            int attachedCount = pipFactory.AttachedCount();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} ATTACH water={1} attached={2} leftEn={3} rightEn={4} fov={5:F1} pipFov={6:F1}",
                LogPrefix, pipFactory.WaterAttached, attachedCount,
                pipFactory.LeftCamera != null && pipFactory.LeftCamera.enabled,
                pipFactory.RightCamera != null && pipFactory.RightCamera.enabled,
                viewCamera != null ? viewCamera.fieldOfView : 0f,
                viewCamera != null ? viewCamera.fieldOfView / zoom.FovDivisor : 0f));
            UpdatePipPose();
            LogVectorState("open");
            IsOpen = true;
            // Bloom-cut is always-on while open (512x512 PIP: halos + NaN
            // black blocks from the 1080p-tuned bloom) - arm it on the open
            // edge, otherwise it would wait for the first filter keypress.
            bloomCut.Update(IsOpen, leftFilterCount, rightFilterCount);
        }

        /// <summary>
        /// Vector telemetry (extracted to SextantVectorLog): gathers the
        /// sun reads and delegates the full state dump.
        /// </summary>
        private void LogVectorState(string trigger)
        {
            Vector3 ownDir, gameDir;
            float ownAlt, ownAz, gameAlt;
            bool ownOk = TryGetOwnSun(out ownDir, out ownAlt, out ownAz);
            bool gameOk = TryGetGameSun(out gameDir, out gameAlt);
            vectorLog.Log(trigger, pipFactory.LeftCamera, pipFactory.RightCamera, viewCamera, arcAngle,
                ownOk, ownDir, ownAlt, ownAz, gameOk, gameDir, gameAlt);
        }

        private void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            SextantPipPose.ClearTestAim();
            if (gameInput != null)
                gameInput.RemoveBlockade(this);
            pipFactory.Detach();
            pipFactory.InteriorMirror.SetEnabled(false);
            SunDiscRenderer.Active = false;
            SunDiscRenderer.ResetDirection();
            bloomCut.Hide();
            if (pipFactory.LeftCamera != null)
                pipFactory.LeftCamera.enabled = false;
            if (pipFactory.RightCamera != null)
                pipFactory.RightCamera.enabled = false;
            if (canvasRoot != null)
                canvasRoot.SetActive(false);
            if (helpPanel != null)
                helpPanel.SetActive(false);
            Debug.Log(LogPrefix + " closed");
        }

        // ---- MCP probe surface (SextantProbe, external agents) -------------

        internal SextantWorksheet WorksheetForProbe
        {
            get { return worksheet; }
        }

        internal SextantStarId StarIdForProbe
        {
            get { return starId; }
        }

        internal int LeftFilterCount
        {
            get { return leftFilterCount; }
        }

        internal int RightFilterCount
        {
            get { return rightFilterCount; }
        }

        internal double ArcAngle
        {
            get { return arcAngle; }
        }

        /// <summary>Opens the device for the probe; false when CanOpen() is
        /// false (outside the live view) or the open did not stick.</summary>
        internal bool TryOpenForProbe()
        {
            if (IsOpen)
                return true;
            TryOpen();
            return IsOpen;
        }

        internal void CloseForProbe()
        {
            Close();
        }

        internal void SetArcForProbe(double deg)
        {
            arcAngle = deg;
            UpdateReadout(true);
        }

        internal void SetFiltersForProbe(int left, int right)
        {
            leftFilterCount = left;
            rightFilterCount = right;
            UpdateFilterLabel();
            UpdateFilterDim();
            SunDiscRenderer.Active = IsOpen && rightFilterCount > 0;
            bloomCut.Update(IsOpen, leftFilterCount, rightFilterCount);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} FILTERSIDE side=PROBE left={1} right={2}",
                LogPrefix, leftFilterCount, rightFilterCount));
        }

        internal bool TryGetPositionForProbe(out float lat, out float lon)
        {
            return positionProvider.TryGetPosition(out lat, out lon);
        }

        internal void ResetMoonDiagTimerForProbe()
        {
            gameSun.ResetMoonDiagTimer();
        }

        /// <summary>Test-only: toggles the mirrored SSMS/SSMSGlobalFog image
        /// effects on both PIP cameras. Components are matched by type NAME, so
        /// the mod keeps no compile-time dependency on the SSMS assembly. Live
        /// bisection aid for the ocular glow / black-square artifacts
        /// (SSEXTANT_PLAN §16.13). Returns the number of components touched.</summary>
        internal int SetSsmsEnabledForProbe(bool enabled)
        {
            int count = 0;
            Camera[] cameras = { pipFactory.LeftCamera, pipFactory.RightCamera };
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null)
                    continue;
                MonoBehaviour[] behaviours = cameras[i].GetComponents<MonoBehaviour>();
                for (int j = 0; j < behaviours.Length; j++)
                {
                    MonoBehaviour behaviour = behaviours[j];
                    if (behaviour == null)
                        continue;
                    string typeName = behaviour.GetType().Name;
                    if (typeName != "SSMS" && typeName != "SSMSGlobalFog")
                        continue;
                    behaviour.enabled = enabled;
                    count++;
                }
            }
            Debug.Log(LogPrefix + " SSMS probe " + (enabled ? "on" : "off")
                + " components=" + count);
            return count;
        }

        /// <summary>Test-only: dumps the SRP pass lists (CullingMask /
        /// Enabled / ScreenSpaceReflections per pass) of the main, interior
        /// and both PIP cameras as JSON. The Interior Camera carries 2 custom
        /// passes vs the PIP's main-cloned 1 (or default structure) — this
        /// shows what the writer-level pass difference actually is
        /// (residual-light track). Read-only; nested pass type resolved by
        /// name so no compile-time dependency beyond AdditionalCameraData.</summary>
        internal string PassDumpForProbe()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"cameras\":{");
            DumpPasses(sb, "Main Camera");
            sb.Append(',');
            DumpPasses(sb, "Interior Camera");
            Camera[] pips = { pipFactory.LeftCamera, pipFactory.RightCamera };
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null)
                    continue;
                sb.Append(",\"pip");
                sb.Append(i);
                sb.Append("\":");
                AppendPassList(sb, pips[i].GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>());
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static void DumpPasses(StringBuilder sb, string cameraName)
        {
            sb.Append('\"');
            sb.Append(cameraName);
            sb.Append("\":");
            GameObject go = GameObject.Find(cameraName);
            UBOAT.Game.Scene.Rendering.AdditionalCameraData data = null;
            if (go != null)
                data = go.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            AppendPassList(sb, data);
        }

        private static void AppendPassList(StringBuilder sb, UBOAT.Game.Scene.Rendering.AdditionalCameraData data)
        {
            if (data == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('[');
            try
            {
                MemberInfo passesMember = TypeMemberCache.GetInstanceMember(
                    data.GetType(), "Passes");
                PropertyInfo passesProp = passesMember as PropertyInfo;
                Array passes = passesProp != null ? passesProp.GetValue(data, null) as Array : null;
                if (passes == null)
                {
                    sb.Append(']');
                    return;
                }
                for (int i = 0; i < passes.Length; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    object pass = passes.GetValue(i);
                    if (pass == null)
                    {
                        sb.Append("null");
                        continue;
                    }
                    Type passType = pass.GetType();
                    sb.Append("{\"mask\":");
                    sb.Append(ReadPassInt(pass, passType, "CullingMask"));
                    sb.Append(",\"enabled\":");
                    sb.Append(ReadPassBool(pass, passType, "Enabled") ? "true" : "false");
                    sb.Append(",\"ssr\":");
                    sb.Append(ReadPassBool(pass, passType, "ScreenSpaceReflections") ? "true" : "false");
                    sb.Append('}');
                }
                sb.Append(']');
            }
            catch (Exception e)
            {
                sb.Append("],\"error\":\"");
                sb.Append(e.GetType().Name);
                sb.Append("\"}");
            }
        }

        private static string ReadPassInt(object pass, Type passType, string name)
        {
            try
            {
                MemberInfo member = TypeMemberCache.GetInstanceMember(passType, name);
                PropertyInfo property = member as PropertyInfo;
                if (property == null)
                    return "null";
                object value = property.GetValue(pass, null);
                if (value is int)
                    return ((int)value).ToString(CultureInfo.InvariantCulture);
                return "null";
            }
            catch
            {
                return "null";
            }
        }

        private static bool ReadPassBool(object pass, Type passType, string name)
        {
            try
            {
                MemberInfo member = TypeMemberCache.GetInstanceMember(passType, name);
                PropertyInfo property = member as PropertyInfo;
                if (property == null)
                    return false;
                object value = property.GetValue(pass, null);
                if (value is bool)
                    return (bool)value;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Test-only: toggles components by type NAME on a named
        /// camera ("Main Camera", "Interior Camera", PIP cameras...).
        /// Name-based, so the mod keeps no compile-time dependency on
        /// external assemblies the MCP bridge cannot resolve (SSMS,
        /// CP_SSSSS_Main, Standard Assets AO/SSR, TOD). Bisection aid for
        /// the residual tint (SEXTANT_PLAN step 6): which main/interior-path
        /// effect carries the eyes' warmth. Returns -1 when the camera is
        /// not found, else the number of components touched.</summary>
        internal int SetComponentEnabledForProbe(string cameraName, string typeName, bool enabled)
        {
            if (string.IsNullOrEmpty(cameraName) || string.IsNullOrEmpty(typeName))
                return -1;
            GameObject target = GameObject.Find(cameraName);
            if (target == null)
                return -1;
            int count = 0;
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                Type type = behaviour.GetType();
                if (type.Name != typeName && type.FullName != typeName)
                    continue;
                behaviour.enabled = enabled;
                count++;
            }
            Debug.Log(LogPrefix + " COMPONENT probe camera='" + cameraName
                + "' type='" + typeName + "' " + (enabled ? "on" : "off")
                + " components=" + count);
            return count;
        }

        /// <summary>Test-only numeric readback of both PIP render targets
        /// (channel means 0-255): distinguishes a camera-side tint (the RT
        /// itself is already blue) from a display-side one (RT neutral, the
        /// screen blue). Uses the calibrated path (§16.18): Blit into an 8x8
        /// RT, then ReadPixels exactly 8x8 (a direct large-region ReadPixels
        /// returns black on this Unity build).</summary>
        internal string RtMeanForProbe()
        {
            int lr, lg, lb, rr, rg, rb;
            bool leftOk = TryReadRtMean(pipFactory.LeftRt, out lr, out lg, out lb);
            bool rightOk = TryReadRtMean(pipFactory.RightRt, out rr, out rg, out rb);
            if (!leftOk && !rightOk)
                return "{\"ok\":false,\"error\":\"no-rt\"}";
            return "{\"ok\":true,\"left\":[" + lr + "," + lg + "," + lb
                + "],\"right\":[" + rr + "," + rg + "," + rb + "]}";
        }

        private static bool TryReadRtMean(RenderTexture source, out int r, out int g, out int b)
        {
            r = 0;
            g = 0;
            b = 0;
            if (source == null)
                return false;
            RenderTexture temp = null;
            Texture2D tex = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                temp = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;
                tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
                Color32[] pixels = tex.GetPixels32();
                if (pixels == null || pixels.Length == 0)
                    return false;
                long sr = 0;
                long sg = 0;
                long sb = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    sr += pixels[i].r;
                    sg += pixels[i].g;
                    sb += pixels[i].b;
                }
                r = (int)(sr / pixels.Length);
                g = (int)(sg / pixels.Length);
                b = (int)(sb / pixels.Length);
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantController.RtMeanRead"))
                    Debug.LogWarning(LogPrefix + " RT mean readback failed: " + e.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp != null)
                    RenderTexture.ReleaseTemporary(temp);
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
            }
        }

        // §13.4 camera gate: sextant exists only in live deck views.
        private bool IsLiveViewMode()
        {
            if (gameMainCamera == null)
                gameMainCamera = FindObjectOfType<MainCamera>();
            if (gameMainCamera == null)
                return false;
            string mode = gameMainCamera.CurrentMode.ToString();
            return mode == "SectionView" || mode == "FPP";
        }

        private string CurrentModeName()
        {
            if (gameMainCamera == null)
                gameMainCamera = FindObjectOfType<MainCamera>();
            return gameMainCamera != null ? gameMainCamera.CurrentMode.ToString() : "no-camera";
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        // ---- input polling lives in SextantInputPoller (extracted);
        // key polling callbacks and host services stay here.

        /// <summary>Hold/press vector telemetry on plain (non-Ctrl) input.</summary>
        private void LogArcInputVector(float dt)
        {
            if (!SextantVectorLog.Enabled)
                return;
            bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
            bool holdDue = vectorLog.OnArcInput(dt);
            if (pressed)
                vectorLog.OnArcPressed();
            if (pressed || holdDue)
                LogVectorState(pressed ? "press" : "hold");
        }

        // Rotate telemetry lives in SextantVectorLog.OnViewRotate (Update
        // calls it directly).

        private void ResetArc()
        {
            arcAngle = 0.0;
            Debug.Log(LogPrefix + " ARC reset to 0");
        }

        // D°M.m′ formatting lives in SextantDmFormat; the worksheet card
        // building lives in SextantWorksheet (both extracted).


        // DВ°M.mвЂІ formatting lives in SextantDmFormat (extracted).

        private void ZoomIn()
        {
            if (zoom.ZoomIn())
                LogZoom();
        }

        private void ZoomOut()
        {
            if (zoom.ZoomOut())
                LogZoom();
        }

        private void LogZoom()
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} ZOOM div={1:F1} fov={2:F1}",
                LogPrefix, zoom.FovDivisor,
                viewCamera != null ? viewCamera.fieldOfView / zoom.FovDivisor : 0f));
        }

        private void SetFilters(int count, bool leftSide)
        {
            if (leftSide)
                leftFilterCount = count;
            else
                rightFilterCount = count;
            UpdateFilterLabel();
            UpdateFilterDim();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} FILTERSIDE side={1} count={2} left={3} right={4} dimA=(L={5:F2},R={6:F2})",
                LogPrefix, leftSide ? "L" : "R", count, leftFilterCount, rightFilterCount,
                filterDimLeftImage != null ? filterDimLeftImage.color.a : -1f,
                filterDimRightImage != null ? filterDimRightImage.color.a : -1f));

            // The sun disc lives in the right (sun) half: gated by the right
            // filter only. The left filter dims the horizon view.
            SunDiscRenderer.Active = IsOpen && rightFilterCount > 0;

            bloomCut.Update(IsOpen, leftFilterCount, rightFilterCount);
            bloomCut.LogAppliedOnce(pipFactory.LeftCamera, pipFactory.RightCamera, viewCamera, leftFilterCount, rightFilterCount);
        }

        private void UpdateFilterDim()
        {
            glare.ApplyDim(filterDimLeftImage, filterDimRightImage,
                leftFilterCount, rightFilterCount, SextantGlareCoordinator.GlareEnabled);
        }

        // Per-frame pose lives in SextantPipPose (extracted, P5.4 slice
        // 4b); the manual-render telemetry closure stays here.
        private void UpdatePipPose()
        {
            SextantPipPose.Update(pipFactory, arcAngle, zoom.FovDivisor,
                ref viewCamera, manualRenderDelegate);
        }

        // ---- glare application lives in SextantGlareCoordinator
        // (extracted); the sun fetches stay here as adapters.

        private void UpdateGlare(float dt)
        {
            Vector3 sunDir = Vector3.zero;
            float sunAlt = 0f;
            float sunAz = 0f;
            bool ownOk = pipFactory.RightCamera != null
                && TryGetOwnSun(out sunDir, out sunAlt, out sunAz);
            Vector3 gameDir = Vector3.zero;
            float gameAlt = 0f;
            bool gameOk = false;
            if (ownOk)
                gameOk = TryGetGameSun(out gameDir, out gameAlt);

            GlareFrame frame = new GlareFrame();
            frame.RightCamera = pipFactory.RightCamera;
            frame.OwnOk = ownOk;
            frame.OwnDir = sunDir;
            frame.OwnAlt = sunAlt;
            frame.OwnAz = sunAz;
            frame.GameOk = gameOk;
            frame.GameDir = gameDir;
            frame.RightFilterCount = rightFilterCount;
            frame.GlareImage = glareImage;
            frame.FilterDimLeft = filterDimLeftImage;
            frame.FilterDimRight = filterDimRightImage;
            frame.PainText = painText;
            frame.SunReadout = sunReadoutDelegate;
            frame.SunDriftLog = sunDriftLogDelegate;
            frame.PainStarIdReset = painStarIdResetDelegate;
            glare.Update(dt, frame);
        }

        // Sun/bodies readout lives in SextantBodiesReadout (extracted);
        // these wrappers keep the call sites.
        private void UpdateSunReadout(bool available, float sunAltDeg)
        {
            bodiesReadout.UpdateSunLine(available, sunAltDeg);
        }

        private void UpdateBodiesReadout(float dt)
        {
            float eyeHeightM = viewCamera != null
                ? viewCamera.transform.position.y : 0f;
            bodiesReadout.UpdateBodies(dt, eyeHeightM,
                worksheet.Observation1AzDeg, worksheet.Observation1Name);
        }

        /// <summary>
        /// Own sun direction via the SunEphemeris domain (extracted);
        /// adapts the position provider and game clock to explicit inputs.
        /// Needs a valid sandbox position (lat/lon), like star identification.
        /// </summary>
        internal bool TryGetOwnSun(out Vector3 dirWorld, out float altDeg, out float azDeg)
        {
            dirWorld = Vector3.zero;
            altDeg = 0f;
            azDeg = 0f;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return false;

            SunEphemeris.SunFix fix = SunEphemeris.Compute(lat, lon, GameTimeResolver.GetUtcNow());
            if (!fix.Ok)
                return false;

            dirWorld = new Vector3((float)fix.DirX, (float)fix.DirY, (float)fix.DirZ);
            altDeg = fix.AltDeg;
            azDeg = fix.AzDeg;
            return true;
        }

        /// <summary>
        /// V1 Moon parity telemetry: own Meeus moon alt/az fed to the
        /// PlayerPrefs-gated MOONDIAG log so the game/mod moon coincidence
        /// can be judged from Player.log.
        /// </summary>
        private void UpdateMoonParity(float dt)
        {
            float ownAlt, ownAz;
            bool ownOk = TryGetOwnMoon(out ownAlt, out ownAz);
            gameSun.LogMoonParity(dt, ownOk, ownAlt, ownAz);
        }

        /// <summary>Own Meeus moon alt/az at the sandbox position.</summary>
        internal bool TryGetOwnMoon(out float altDeg, out float azDeg)
        {
            altDeg = 0f;
            azDeg = 0f;
            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return false;

            DateTime utc = GameTimeResolver.GetUtcNow();
            // The Moon shares the game sky time shift (ADR-033 v2.4): compare
            // (and render) the same calibrated moon the fix uses.
            double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            if (!AstroMath.MoonRaDec(jd, out double raRad, out double decRad))
                return false;
            AstroMath.RaDecToEnu(raRad, decRad, AstroMath.LstDeg(jd, lon), lat,
                out double altRad, out double azRad, out _, out _, out _);
            altDeg = (float)(altRad * Mathf.Rad2Deg);
            azDeg = (float)(azRad * Mathf.Rad2Deg);
            return true;
        }

        /// <summary>
        /// SUNDRIFT telemetry and game-sun reflection live in
        /// SextantGameSun (extracted); these wrappers keep the call sites.
        /// </summary>
        private void LogSunDrift(float dt)
        {
            Vector3 ownDir;
            float ownAlt, ownAz;
            bool ownOk = TryGetOwnSun(out ownDir, out ownAlt, out ownAz);
            gameSun.LogDrift(dt, ownOk, ownDir, ownAlt, ownAz);
        }

        private bool TryGetGameSun(out Vector3 dir, out float alt)
        {
            return gameSun.TryGetSun(out dir, out alt);
        }

        // ---- star / sun identification lives in SextantStarId (extracted) ----

        private void UpdateStarId(float dt)
        {
            Vector3 ownDir, gameDir;
            float ownAlt, ownAz, gameAlt;
            bool ownOk = TryGetOwnSun(out ownDir, out ownAlt, out ownAz);
            bool gameOk = TryGetGameSun(out gameDir, out gameAlt);
            float lat, lon;
            bool posOk = positionProvider.TryGetPosition(out lat, out lon);
            // Hull gate (R5): inside the boat the hull blocks the sky, so no
            // body may be identified there — the ocular shows the interior.
            MainCamera main = MainCamera.Instance;
            bool canSight = main == null || !main.IsInsideBoat;
            starId.Update(dt, glare.IsPained, canSight, SextantPipPose.Forward(pipFactory, viewCamera, gameMainCamera, arcAngle),
                ownOk, ownDir, ownAlt, gameOk, gameDir, posOk, lat, lon,
                nameText, arcAngle);
        }


        // ---- readout -----------------------------------------------------------

        private void UpdateReadout(bool force)
        {
            if (angleText == null)
                return;
            string text;
            if (readout.TryUpdateArc(arcAngle, force, out text))
                angleText.text = text;
        }

        private void UpdateFilterLabel()
        {
            if (filterText == null)
                return;
            filterText.text = SextantReadout.FormatFilters(leftFilterCount, rightFilterCount);
        }

        // ---- overlay construction (§13.3) lives in SextantOverlay (extracted) ----

        private void EnsureOverlay()
        {
            if (canvasRoot != null)
                return;

            var ui = SextantOverlay.Build(pipFactory.LeftRt, pipFactory.RightRt, resourceManager, this);
            canvasRoot = ui.CanvasRoot;
            leftViewImage = ui.LeftViewImage;
            rightViewImage = ui.RightViewImage;
            filterDimLeftImage = ui.FilterDimLeftImage;
            filterDimRightImage = ui.FilterDimRightImage;
            glareImage = ui.GlareImage;
            angleText = ui.AngleText;
            nameText = ui.NameText;
            painText = ui.PainText;
            filterText = ui.FilterText;
            ringTexture = ui.RingTexture;
            circleMaskTexture = ui.CircleMaskTexture;
            eyecupTexture = ui.EyecupTexture;
            ownedSprites = ui.OwnedSprites;
            // NOTE: ui.OpaqueViewMaterial intentionally not cached here — the
            // ocular images already hold it (SextantOcularBuilder assigns
            // Left/RightViewImage.material), so no extra reference is needed
            // to keep Unity from unloading it.
            bodiesReadout.BindLabels(ui.SunAltText, ui.BodiesAltText);
            worksheet.BindCard(ui.FixCardText);
            helpPanel = SextantHelpPanel.Build(canvasRoot);
        }

        // Procedural sprites live in SextantSprites; SRP render-pass
        // cloning lives in SextantRenderPasses (both extracted).
    }
}
