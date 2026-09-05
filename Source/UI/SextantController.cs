using System;
using System.Globalization;
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
        private const float PainOnGlare = 0.6f;
        private const float PainOffGlare = 0.5f;


        /// <summary>True while the overlay is up; ConstellationMesh gates Z on it (§7.4).</summary>
        public static bool IsOpen { get; private set; }

        internal double arcAngle;
        private int leftFilterCount;
        private int rightFilterCount;
        private bool pain;
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

        // SUNDRIFT calibration lifecycle lives in SunCalibrator
        // (extracted, P5.4 slice 4c): solved on open, persisted, applied
        // to all worksheet ephemeris.
        private readonly SunCalibrator calibrator = new SunCalibrator();

        private void Awake()
        {
            worksheet = new SextantWorksheet(positionProvider);
            bodiesReadout = new SextantBodiesReadout(positionProvider);
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
                HandleArcInput(Time.unscaledDeltaTime);
                HandleFilterKeys();
                HandleZoomKeys();
                UpdatePipPose();
                if (vectorLog.OnViewRotate(viewCamera))
                    LogVectorState("rotate");
                renderCheck.Update(Time.unscaledDeltaTime, arcAngle, pipFactory.LeftRt, pipFactory.RightRt);
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
            pipFactory.Destroy();
            IsOpen = false;

            if (canvasRoot != null) Destroy(canvasRoot);
            bloomCut.Destroy();
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
            pain = false;
            readout.Reset();
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
            pipFactory.AttachToMain();
            // Attached cameras are rendered via MainCamera's pass list;
            // keep enabled=false like periscope's small view. Standalone
            // cameras (not attached) need enabled=true.
            if (pipFactory.LeftCamera != null)
                pipFactory.LeftCamera.enabled = !pipFactory.WaterAttached;
            // The right camera must re-render with the updated pose: the
            // attached pass list does not refresh it, so keep it enabled.
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
            if (gameInput != null)
                gameInput.RemoveBlockade(this);
            pipFactory.Detach();
            SunDiscRenderer.Active = false;
            SunDiscRenderer.ResetDirection();
            bloomCut.Hide();
            if (pipFactory.LeftCamera != null)
                pipFactory.LeftCamera.enabled = false;
            if (pipFactory.RightCamera != null)
                pipFactory.RightCamera.enabled = false;
            if (canvasRoot != null)
                canvasRoot.SetActive(false);
            Debug.Log(LogPrefix + " closed");
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
                arcInput.ResetFineHold();
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl)
            {
                // Fine adjustment: one drum tick (0.2' = 1 quantum) per press,
                // hold repeats after a short delay - like turning the drum.
                bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
                if (arcInput.AdvanceFineHold(dt, pressed))
                    arcAngle = SextantArcInput.StepFine(arcAngle, dir);
                return;
            }

            LogArcInputVector(dt);

            arcAngle = SextantArcInput.StepCoarse(arcAngle, dir, dt, ShiftHeld());
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
            if (!GlareEnabled)
                return;
            if (filterDimLeftImage != null)
            {
                Color c = filterDimLeftImage.color;
                c.a = SextantReadout.FilterDimAlpha(leftFilterCount);
                filterDimLeftImage.color = c;
            }
            if (filterDimRightImage != null)
            {
                Color c = filterDimRightImage.color;
                c.a = SextantReadout.FilterDimAlpha(rightFilterCount);
                filterDimRightImage.color = c;
            }
        }

        // Per-frame pose lives in SextantPipPose (extracted, P5.4 slice
        // 4b); the manual-render telemetry closure stays here.
        private void UpdatePipPose()
        {
            SextantPipPose.Update(pipFactory, arcAngle, zoom.FovDivisor,
                ref viewCamera, () => vectorLog.OnManualRender(arcAngle));
        }

        // ---- glare -----------------------------------------------------------
        // v1.7 the glare mechanic was disabled for the white-ocular diagnosis;
        // the diagnosis is closed (PIP cameras mirror the FPP post stack), so
        // the full mechanic is back: shade filters, white flash, pain.
        // static readonly (not const): const folds away under the C# 7.0
        // game compiler and produced CS0162 unreachable-code warnings.
        private static readonly bool GlareEnabled = true;

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
            if (pipFactory.RightCamera != null && TryGetOwnSun(out sunDir, out sunAlt, out sunAz))
            {
                float angle = Vector3.Angle(pipFactory.RightCamera.transform.forward, sunDir);
                g = (float)SextantGlare.Glare(angle, sunAlt, rightFilterCount, pipFactory.RightCamera.fieldOfView);
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
        /// Own sun direction via the SunEphemeris domain (extracted);
        /// adapts the position provider and game clock to explicit inputs.
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

            SunEphemeris.SunFix fix = SunEphemeris.Compute(lat, lon, GameTimeResolver.GetUtcNow());
            if (!fix.Ok)
                return false;

            dirWorld = new Vector3((float)fix.DirX, (float)fix.DirY, (float)fix.DirZ);
            altDeg = fix.AltDeg;
            azDeg = fix.AzDeg;
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
            starId.Update(dt, pain, SextantPipPose.Forward(pipFactory, viewCamera, gameMainCamera, arcAngle),
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
            // NOTE: ui.OpaqueViewMaterial intentionally not cached here — the
            // ocular images already hold it (SextantOcularBuilder assigns
            // Left/RightViewImage.material), so no extra reference is needed
            // to keep Unity from unloading it.
            bodiesReadout.BindLabels(ui.SunAltText, ui.BodiesAltText);
            worksheet.BindCard(ui.FixCardText);
        }

        // Procedural sprites live in SextantSprites; SRP render-pass
        // cloning lives in SextantRenderPasses (both extracted).
    }
}
