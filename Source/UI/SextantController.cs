using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using PlayWay.Water;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Environment;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
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

        // §13.3 overlay layout constants live in SextantOverlay (extracted);
        // only the RT size stays here (PIP camera creation).
        private const int RtWidth = 512;
        private const int RtHeight = 512;

        private const float RateDegPerSec = 2f;
        private const float ShiftMultiplier = 5f;
        // Fine adjustment (Ctrl+arrows): one micrometer-drum tick per step,
        // 0.2' = 12" = exactly one display quantum (SextantArc.StepsPerDeg).
        private const int FineStepQuant = 1;
        private const float FineRepeatDelay = 0.3f;
        private const float FineRepeatInterval = 0.15f;
        // Vector telemetry lives in SextantVectorLog (extracted).
        private readonly SextantVectorLog vectorLog = new SextantVectorLog();
        // Zoom steps (divisor on the main camera's FOV): index 2 = default 3x.
        // Smaller divisor = closer view. Buttons/keys shift the index.
        private static readonly float[] ZoomSteps = { 6f, 4.5f, 3f, 2f, 1.5f, 1f };
        private int zoomIndex = 2;
        private float PipFovDivisor { get { return ZoomSteps[zoomIndex]; } }
        private const float PainOnGlare = 0.6f;
        private const float PainOffGlare = 0.5f;


        /// <summary>True while the overlay is up; ConstellationMesh gates Z on it (§7.4).</summary>
        public static bool IsOpen { get; private set; }

        internal double arcAngle;
        private int leftFilterCount;
        private int rightFilterCount;
        private float fineRepeatTimer;
        private bool pain;
        private double lastFormattedArc = double.MinValue;
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

        // Water + post stack wiring for the PIP cameras (SEXTANT_PLAN v1.6):
        // the game's render pipeline renders water only for cameras listed on
        // the main camera's UBOAT.Game.Scene.Rendering.AdditionalCameraData
        // (periscope pattern, Periscope.cs:581), and without a
        // PostProcessLayer the HDR sky/sea clip to pure white.
        private Camera pipLeftCamera;
        private Camera pipRightCamera;
        private RenderTexture leftRt;
        private RenderTexture rightRt;
        private RawImage leftViewImage;
        private RawImage rightViewImage;
        private UBOAT.Game.Scene.Rendering.AdditionalCameraData registeredWaterData;
        private bool waterAttachWarned;

        // 1:1 mirror of the main camera's rendering stack lives in
        // SextantEffectMirror (extracted); the controller keeps the live
        // source references it feeds to the mirror each frame.
        private readonly SextantEffectMirror effectMirror = new SextantEffectMirror();
        private Component sourceSsmsFog;
        private Component sourceSsms;

        private GameObject canvasRoot;
        private RectTransform pipRect;
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

        // PPv2 bloom-cut volumes live in SextantBloomCut (extracted).
        private readonly SextantBloomCut bloomCut = new SextantBloomCut();
        private bool sunDiscSourceLogged;

        private readonly IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();

        // Running-fix worksheet state (v1.9) lives in SextantWorksheet
        // (extracted): sights, card text, course/speed track sampler.
        private SextantWorksheet worksheet;

        // SUNDRIFT calibration (v1.10): solved against the game sun on each
        // open, persisted in PlayerPrefs, applied to all worksheet ephemeris.
        private const string SunCalibrationPrefsKey = "RadioNav.SunCalibrationMinutes";

        private void Awake()
        {
            worksheet = new SextantWorksheet(positionProvider);
            bodiesReadout = new SextantBodiesReadout(positionProvider);
            float saved = PlayerPrefs.GetFloat(SunCalibrationPrefsKey, 0f);
            if (Math.Abs(saved) > 1e-4)
            {
                SunEphemerisCalibration.SetDelta(saved);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration loaded: {1:F2} min",
                    LogPrefix, saved));
            }
        }

        /// <summary>
        /// Solves the ephemeris time shift against the game sun direction
        /// (cheap: ~70 ephemeris evaluations once per open) and persists it.
        /// Keeps the previous calibration when the game sun is unavailable
        /// or the residual is too large to trust.
        /// </summary>
        private void SolveSunCalibration()
        {
            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
                return;
            Vector3 gameDir;
            float gameAlt;
            if (!TryGetGameSun(out gameDir, out gameAlt))
                return;

            // v1.13: never re-solve mid-series — observation 1 was recorded
            // under the stored Δt, a new Δt would put the two sights on
            // different ephemerides and skew the LOPs. Re-solving happens
            // only when the next open starts with an empty series.
            if (worksheet.SeriesActive)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration kept: dt={1:F2} min (series active)",
                    LogPrefix, SunEphemerisCalibration.DeltaMinutes));
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double delta, residual;
            if (SunEphemerisCalibration.SolveDelta(utc, latF, lonF,
                    gameDir.x, gameDir.y, gameDir.z, out delta, out residual))
            {
                SunEphemerisCalibration.SetDelta(delta);
                PlayerPrefs.SetFloat(SunCalibrationPrefsKey, (float)delta);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration solved: dt={1:F2} min residual={2:F3} deg",
                    LogPrefix, delta, residual));
            }
            else
            {
                Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration rejected: residual={1:F3} deg - keeping previous",
                    LogPrefix, residual));
            }
            worksheet.InvalidateCard(); // card shows the calibration line
        }

        [Inject] private static ResourceManager resourceManager = null;
        // Opaque view material from the bundle; kept referenced so Unity
        // does not unload it while the overlay lives (built by SextantOverlay).
        private Material opaqueViewMaterial;

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
                HandleArcInput(Time.unscaledDeltaTime);
                HandleFilterKeys();
                HandleZoomKeys();
                UpdatePipPose();
                if (vectorLog.OnViewRotate(viewCamera))
                    LogVectorState("rotate");
                renderCheck.Update(Time.unscaledDeltaTime, arcAngle, leftRt, rightRt);
                UpdateGlare(Time.unscaledDeltaTime);
                UpdateStarId(Time.unscaledDeltaTime);
                UpdateReadout(false);
                UpdateBodiesReadout(Time.unscaledDeltaTime);
                HandleFixKeys();
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
            if (IsOpen && gameInput != null)
                gameInput.RemoveBlockade(this);
            DetachWaterCameras();
            IsOpen = false;

            if (canvasRoot != null) Destroy(canvasRoot);
            if (pipLeftCamera != null) Destroy(pipLeftCamera.gameObject);
            if (pipRightCamera != null) Destroy(pipRightCamera.gameObject);
            bloomCut.Destroy();
            if (leftRt != null)
            {
                leftRt.Release();
                Destroy(leftRt);
            }
            if (rightRt != null)
            {
                rightRt.Release();
                Destroy(rightRt);
            }
            if (ringTexture != null) Destroy(ringTexture);
            if (circleMaskTexture != null) Destroy(circleMaskTexture);
            if (eyecupTexture != null) Destroy(eyecupTexture);
            renderCheck.Destroy();
        }

        // ---- open / close -------------------------------------------------

        private void TryOpen()
        {
            if (!CanOpen())
                return;

            EnsureOverlay();
            EnsurePipCameras();
            SolveSunCalibration();

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
            pain = false;
            lastFormattedArc = double.MinValue;
            worksheet.InvalidateCard();
            bodiesReadout.Invalidate();
            gameSun.ResetDriftTimer(); // force the first SUNDRIFT line on open
            renderCheck.ResetForOpen();
            SunDiscRenderer.RandomizeDisc(); // fresh sunspot pattern per open
            SunDiscRenderer.Active = false;
        }

        private void AttachAndConfigureCameras()
        {
            gameInput.AddBlockade(this);
            canvasRoot.SetActive(true);
            AttachWaterCameras();
            // Attached cameras are rendered via MainCamera's pass list;
            // keep enabled=false like periscope's small view. Standalone
            // cameras (not attached) need enabled=true.
            if (pipLeftCamera != null)
                pipLeftCamera.enabled = registeredWaterData == null;
            // The right camera must re-render with the updated pose: the
            // attached pass list does not refresh it, so keep it enabled.
            if (pipRightCamera != null)
                pipRightCamera.enabled = true;
            int attachedCount = 0;
            if (registeredWaterData != null)
            {
                try { attachedCount = registeredWaterData.AttachedCameras.Count; } catch { }
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} ATTACH water={1} attached={2} leftEn={3} rightEn={4} fov={5:F1} pipFov={6:F1}",
                LogPrefix, registeredWaterData != null, attachedCount,
                pipLeftCamera != null && pipLeftCamera.enabled,
                pipRightCamera != null && pipRightCamera.enabled,
                viewCamera != null ? viewCamera.fieldOfView : 0f,
                viewCamera != null ? viewCamera.fieldOfView / PipFovDivisor : 0f));
            UpdatePipPose();
            LogVectorState("open");
            IsOpen = true;
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
            vectorLog.Log(trigger, pipLeftCamera, pipRightCamera, viewCamera, arcAngle,
                ownOk, ownDir, ownAlt, ownAz, gameOk, gameDir, gameAlt);
        }

        private void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            if (gameInput != null)
                gameInput.RemoveBlockade(this);
            DetachWaterCameras();
            SunDiscRenderer.Active = false;
            SunDiscRenderer.ResetDirection();
            bloomCut.Hide();
            if (pipLeftCamera != null)
                pipLeftCamera.enabled = false;
            if (pipRightCamera != null)
                pipRightCamera.enabled = false;
            if (canvasRoot != null)
                canvasRoot.SetActive(false);
            Debug.Log(LogPrefix + " closed");
        }

        // Water renders itself only for cameras listed on the main camera's
        // AdditionalCameraData (periscope pattern, Periscope.cs:581).
        // Must be MainCamera, not the transient viewCamera (device probe).
        private void AttachWaterCameras()
        {
            if (pipLeftCamera == null && pipRightCamera == null)
                return;
            MainCamera main = MainCamera.Instance;
            if (main == null)
                main = FindObjectOfType<MainCamera>();
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
            if (pipLeftCamera != null)
            {
                data.AttachedCameras.Remove(pipLeftCamera);
                data.AttachedCameras.Add(pipLeftCamera);
            }
            if (pipRightCamera != null)
            {
                data.AttachedCameras.Remove(pipRightCamera);
                data.AttachedCameras.Add(pipRightCamera);
            }
        }

        private void DetachWaterCameras()
        {
            if (registeredWaterData == null)
                return;
            if (pipLeftCamera != null)
                registeredWaterData.AttachedCameras.Remove(pipLeftCamera);
            if (pipRightCamera != null)
                registeredWaterData.AttachedCameras.Remove(pipRightCamera);
            registeredWaterData = null;
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

        // ---- input ---------------------------------------------------------

        private void HandleArcInput(float dt)
        {
            float dir = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) dir += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) dir -= 1f;
            if (dir == 0f)
            {
                fineRepeatTimer = 0f;
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl)
            {
                // Fine adjustment: one drum tick (0.2' = 1 quantum) per press,
                // hold repeats after a short delay - like turning the drum.
                bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
                if (pressed)
                {
                    fineRepeatTimer = FineRepeatDelay;
                    StepArcFine(dir);
                    return;
                }
                fineRepeatTimer -= dt;
                if (fineRepeatTimer <= 0f)
                {
                    fineRepeatTimer = FineRepeatInterval;
                    StepArcFine(dir);
                }
                return;
            }

            LogArcInputVector(dt);

            bool fast = ShiftHeld();
            float rate = RateDegPerSec * (fast ? ShiftMultiplier : 1f);
            arcAngle = SextantArc.Quantize(arcAngle + dir * rate * dt);
        }

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

        /// <summary>One fine-adjustment step (0.2' drum tick), always on-grid.</summary>
        private void StepArcFine(float dir)
        {
            arcAngle = SextantArc.Quantize(arcAngle + dir * FineStepQuant / SextantArc.StepsPerDeg);
        }

        // Rotate telemetry lives in SextantVectorLog.OnViewRotate (Update
        // calls it directly).

        private void HandleFilterKeys()
        {
            // Ctrl toggles the LEFT (horizon) filter; plain keys the RIGHT
            // (sun) filter - like the separate shade glasses of a sextant.
            bool left = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetFilters(1, left);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetFilters(2, left);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetFilters(3, left);
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) SetFilters(0, left);
        }

        private void HandleZoomKeys()
        {
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) ZoomIn();
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) ZoomOut();
            if (Input.GetKeyDown(KeyCode.R)) ResetArc();
        }

        private void ResetArc()
        {
            arcAngle = 0.0;
            Debug.Log(LogPrefix + " ARC reset to 0");
        }

        // ---- running fix worksheet (v1.9): state lives in SextantWorksheet ----

        private void HandleFixKeys()
        {
            // I records a sight (only with the sun identified and held),
            // O starts a new worksheet series.
            if (Input.GetKeyDown(KeyCode.I))
                worksheet.CaptureObservation(starId.IdentifiedName, arcAngle);
            if (Input.GetKeyDown(KeyCode.O))
                worksheet.ResetSeries();
        }

        // D°M.m′ formatting lives in SextantDmFormat; the worksheet card
        // building lives in SextantWorksheet (both extracted).


        // DВ°M.mвЂІ formatting lives in SextantDmFormat (extracted).

        private void ZoomIn()
        {
            if (zoomIndex <= 0)
                return;
            zoomIndex--;
            LogZoom();
        }

        private void ZoomOut()
        {
            if (zoomIndex >= ZoomSteps.Length - 1)
                return;
            zoomIndex++;
            LogZoom();
        }

        private void LogZoom()
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} ZOOM div={1:F1} fov={2:F1}",
                LogPrefix, PipFovDivisor,
                viewCamera != null ? viewCamera.fieldOfView / PipFovDivisor : 0f));
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
            bloomCut.LogAppliedOnce(pipLeftCamera, pipRightCamera, viewCamera, leftFilterCount, rightFilterCount);
        }

        private void UpdateFilterDim()
        {
            if (!GlareEnabled)
                return;
            if (filterDimLeftImage != null)
            {
                Color c = filterDimLeftImage.color;
                c.a = 0.30f * leftFilterCount;
                filterDimLeftImage.color = c;
            }
            if (filterDimRightImage != null)
            {
                Color c = filterDimRightImage.color;
                c.a = 0.30f * rightFilterCount;
                filterDimRightImage.color = c;
            }
        }

        // ---- PIP cameras ------------------------------------------------------

        private void EnsurePipCameras()
        {
            if (pipLeftCamera != null && pipRightCamera != null)
                return;

            leftRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            leftRt.name = "RadioNavSextantLeftRT";
            rightRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rightRt.name = "RadioNavSextantRightRT";

            pipLeftCamera = ConfigurePipCamera("RadioNavSextantLeftCamera", leftRt, false);
            pipRightCamera = ConfigurePipCamera("RadioNavSextantRightCamera", rightRt, true);

            if (leftViewImage != null) leftViewImage.texture = leftRt;
            if (rightViewImage != null) rightViewImage.texture = rightRt;

            Debug.Log(LogPrefix + " PIP cameras created, RT=" + RtWidth + "x" + RtHeight);
        }

        private Camera ConfigurePipCamera(string name, RenderTexture target, bool rightSide)
        {
            GameObject go = new GameObject(name);
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(viewCamera);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = target;
            cam.fieldOfView = viewCamera.fieldOfView / PipFovDivisor;
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
            }
            catch { }

            // 1:1 mirror of the main camera's rendering stack: PPv2
            // PostProcessLayer + legacy PostProcessingBehaviour + SSMSGlobalFog
            // + SSMS, copied in the exact component order of the FPP camera,
            // so the SRP renders the PIP views with the same fog/post look.
            ResolveMirrorSources();
            effectMirror.AddMirroredEffectComponents(viewCamera, go);

            // PlayWay renders the sea only for cameras carrying a WaterCamera
            // (waterCamerasCache registry; HullEffectsRenderer.GetWaterCamera
            // null-check skips water otherwise). Pattern: EntitySelectionCamera
            // (disabled camera, manual Render(), WaterCamera in prefab).
            try
            {
                WaterCamera waterCam = go.AddComponent<WaterCamera>();
                Debug.Log(LogPrefix + " WATERCAM added to " + name + ": " + (waterCam != null));

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
            probe.owner = this;

            return cam;
        }

        private void ResolveMirrorSources()
        {
            if (sourceSsmsFog != null || viewCamera == null)
                return;
            sourceSsmsFog = FindComponent(viewCamera, "SSMSGlobalFog");
            sourceSsms = FindComponent(viewCamera, "SSMS");
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


        private void LogPipStack(GameObject go)
        {
            UnityEngine.Object[] comps = go.GetComponents(typeof(UnityEngine.Component));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0)
                    sb.Append(",");
                sb.Append(comps[i] != null ? comps[i].GetType().Name : "null");
            }
            Debug.Log(LogPrefix + " PIPSTACK [" + go.name + "] " + sb.ToString());
        }


        private void RefreshMirroredState()
        {
            effectMirror.RefreshSsms(sourceSsmsFog, sourceSsms);
        }

        private void UpdatePipPose()
        {
            if (pipLeftCamera == null || pipRightCamera == null)
                return;
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
                return;

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;
            pipLeftCamera.transform.position = mt.position;
            pipLeftCamera.transform.rotation = Quaternion.Euler(0f, mainEuler.y, 0f);
            pipRightCamera.transform.position = mt.position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            pipRightCamera.transform.rotation = Quaternion.Euler(-(float)arcAngle, mainEuler.y, 0f);

            SyncCameraState(pipLeftCamera, viewCamera);
            SyncCameraState(pipRightCamera, viewCamera);
            RefreshMirroredState();

            // The game's SRP renders attached cameras once and never
            // refreshes them with the updated pose; force the render so the
            // right RT follows the alidade.
            if (SextantVectorLog.Enabled)
                vectorLog.OnManualRender(arcAngle);
            try
            {
                pipLeftCamera.Render();
                pipRightCamera.Render();
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " MANUALRENDER failed: " + e.Message);
            }
        }

        private void SyncCameraState(Camera cam, Camera source)
        {
            cam.fieldOfView = source.fieldOfView / PipFovDivisor;
            cam.cullingMask = source.cullingMask | SunDiscRenderer.SunDiscLayerMask;
            // Keep the SRP pass mask in sync with the live cullingMask
            try
            {
                var pipAdd = cam.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                if (pipAdd != null)
                {
                    Array passes = pipAdd.Passes as Array;
                    if (passes != null)
                    {
                        foreach (object p in passes)
                        {
                            var prop = p.GetType().GetProperty("CullingMask");
                            if (prop != null && prop.CanWrite)
                                prop.SetValue(p, cam.cullingMask);
                            else
                            {
                                FieldInfo f = p.GetType().GetField("cullingMask", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                                if (f != null) f.SetValue(p, cam.cullingMask);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private Vector3 GetPipForward()
        {
            if (pipRightCamera != null)
                return pipRightCamera.transform.forward;
            float yaw = 0f;
            if (viewCamera != null)
                yaw = viewCamera.transform.eulerAngles.y;
            else if (gameMainCamera != null)
                yaw = gameMainCamera.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(-(float)arcAngle, yaw, 0f);
            return rot * Vector3.forward;
        }

        // ---- glare -----------------------------------------------------------
        // v1.7 the glare mechanic was disabled for the white-ocular diagnosis;
        // the diagnosis is closed (PIP cameras mirror the FPP post stack), so
        // the full mechanic is back: shade filters, white flash, pain.
        private const bool GlareEnabled = true;

        private void UpdateGlare(float dt)
        {
            if (!GlareEnabled)
            {
                ApplyGlare(0f);
                if (filterDimLeftImage != null)
                {
                    Color c = filterDimLeftImage.color;
                    c.a = 0f;
                    filterDimLeftImage.color = c;
                }
                if (filterDimRightImage != null)
                {
                    Color c = filterDimRightImage.color;
                    c.a = 0f;
                    filterDimRightImage.color = c;
                }
                LogSunDrift(dt);
                return;
            }

            float g = 0f;
            Vector3 sunDir;
            float sunAlt;
            float sunAz;
            if (pipRightCamera != null && TryGetOwnSun(out sunDir, out sunAlt, out sunAz))
            {
                float angle = Vector3.Angle(pipRightCamera.transform.forward, sunDir);
                g = (float)SextantGlare.Glare(angle, sunAlt, rightFilterCount, pipRightCamera.fieldOfView);
                // Disc must cover the GAME sun sprite (SUNDRIFT sep ~1.6°),
                // so position it on the game direction when available.
                Vector3 gameDir;
                float gameAlt;
                bool gameOk = TryGetGameSun(out gameDir, out gameAlt);
                SunDiscRenderer.SunDirection = gameOk ? gameDir : sunDir;
                UpdateSunReadout(true, sunAlt);
                if (!sunDiscSourceLogged && SunDiscRenderer.Active)
                {
                    sunDiscSourceLogged = true;
                    float sep = gameOk ? Vector3.Angle(gameDir, sunDir) : -1f;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "{0} SUNDISC src={1} sep={2:F3} own(alt={3:F2} az={4:F1}) game={5}",
                        LogPrefix, gameOk ? "game" : "own", sep, sunAlt, sunAz,
                        gameOk ? SextantVectorLog.V3(gameDir) : " n/a"));
                }
            }
            else
            {
                UpdateSunReadout(false, 0f);
            }
            ApplyGlare(g);
            LogSunDrift(dt);
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
            bodiesReadout.UpdateBodies(dt, eyeHeightM);
        }

        private void ApplyGlare(float g)        {
            if (glareImage != null)
            {
                Color c = glareImage.color;
                c.a = g * 0.9f;
                glareImage.color = c;
            }
            if (!pain && g > PainOnGlare)
            {
                pain = true;
                starId.Reset();
                if (painText != null) painText.enabled = true;
            }
            else if (pain && g < PainOffGlare)
            {
                pain = false;
                if (painText != null) painText.enabled = false;
            }
        }

        /// <summary>
        /// Own Meeus ephemeris: sun RA/Dec > the same ENU>world mapping the
        /// star sky uses, so axis conventions are already game-proven.
        /// Needs a valid sandbox position (lat/lon), like star identification.
        /// </summary>
        private bool TryGetOwnSun(out Vector3 dirWorld, out float altDeg, out float azDeg)
        {
            dirWorld = Vector3.zero;
            altDeg = 0f;
            azDeg = 0f;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return false;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = SunEphemerisCalibration.CalibratedJd(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            double raRad, decRad;
            AstroMath.SunRaDec(jd, out raRad, out decRad);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat, out altRad, out azRad, out e, out n, out u);

            double len = Math.Sqrt(e * e + n * n + u * u);
            if (len < 1e-9)
                return false;

            dirWorld = new Vector3((float)(e / len), (float)(u / len), (float)(n / len));
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
            starId.Update(dt, pain, GetPipForward(),
                ownOk, ownDir, ownAlt, gameOk, gameDir, posOk, lat, lon,
                nameText, arcAngle);
        }


        // ---- readout -----------------------------------------------------------

        private void UpdateReadout(bool force)
        {
            if (angleText == null)
                return;
            double q = SextantArc.Quantize(arcAngle);
            if (force || Math.Abs(q - lastFormattedArc) > 1e-9)
            {
                angleText.text = SextantArc.Format(q);
                lastFormattedArc = q;
            }
        }

        private void UpdateFilterLabel()
        {
            if (filterText == null)
                return;
            if (leftFilterCount <= 0 && rightFilterCount <= 0)
            {
                filterText.text = "";
                return;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder("\u0424\u0418\u041B\u042C\u0422\u0420 ");
            if (leftFilterCount > 0)
                sb.Append("\u041B").Append(leftFilterCount).Append(" ");
            if (rightFilterCount > 0)
                sb.Append("\u041F").Append(rightFilterCount);
            filterText.text = sb.ToString().TrimEnd();
        }

        // ---- overlay construction (§13.3) lives in SextantOverlay (extracted) ----

        private void EnsureOverlay()
        {
            if (canvasRoot != null)
                return;

            var ui = SextantOverlay.Build(leftRt, rightRt, resourceManager, this);
            canvasRoot = ui.CanvasRoot;
            pipRect = ui.PipRect;
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
            opaqueViewMaterial = ui.OpaqueViewMaterial;
            bodiesReadout.BindLabels(ui.SunAltText, ui.BodiesAltText);
            worksheet.BindCard(ui.FixCardText);
        }

        // Procedural sprites live in SextantSprites; SRP render-pass
        // cloning lives in SextantRenderPasses (both extracted).
    }

    /// <summary>
    /// Render probe: OnPreRender fires only when the camera actually renders,
    /// so these logs prove whether the SRP re-renders the PIP cameras every
    /// frame and with which pose (frozen forward = cached matrices).
    /// </summary>
    internal sealed class SextantCameraProbe : MonoBehaviour
    {
        public Camera tracked;
        public SextantController owner;

        private void OnPreRender()
        {
            if (tracked == null || owner == null)
                return;
            if (Time.frameCount < 10 || Time.frameCount % 60 == 0)
            {
                Vector3 f = tracked.transform.forward;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[RadioNav][Sextant] PRERENDER {0} frame={1} fwd=({2:F2},{3:F2},{4:F2}) arc={5:F2}",
                    tracked.name, Time.frameCount, f.x, f.y, f.z, owner.arcAngle));
            }
        }
    }
}
