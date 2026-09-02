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

        // §13.3 layout constants (1080-space units).
        private const float OcularSizeUnits = 600f;
        private const int RtWidth = 512;
        private const int RtHeight = 512;
        private const float VeilAlpha = 0.85f;
        private const float ColumnGapUnits = 40f;
        private const float ColumnWidthUnits = 320f;
        private const float HintYUnits = 60f;
        private const float SunColumnWidthUnits = 440f;

        // Running-fix worksheet column (F-Tafel Beobachtungsblatt, two sun
        // sights with Versetzung). SEXTANT_PLAN v1.9.
        private const float FixColumnWidthUnits = 380f;
        private const float FixCardHeightUnits = 560f;
        private const int FixCardFontSize = 11;
        private const float FixPanelIntervalSeconds = 1f;
        private const float FixStatusSeconds = 3f;
        private const float CourseSampleIntervalSeconds = 2f;
        private const float CourseSampleWindowSeconds = 10f;
        private const float MinTransferSpeedKn = 0.5f;

        // Tube falloff / eyecup sprite constants live in SextantSprites
        // (extracted); the eyecup LAYOUT size stays here.
        private const float EyecupSizeUnits = 1000f;

        private const float RateDegPerSec = 2f;
        private const float ShiftMultiplier = 5f;
        // Fine adjustment (Ctrl+arrows): one micrometer-drum tick per step,
        // 0.2' = 12" = exactly one display quantum (SextantArc.StepsPerDeg).
        private const int FineStepQuant = 1;
        private const float FineRepeatDelay = 0.3f;
        private const float FineRepeatInterval = 0.15f;
        private const bool VectorLogEnabled = true;
        private const float VectorLogHoldIntervalSeconds = 0.5f;
        private const float VectorRotateMinDeltaDeg = 0.5f;
        // Zoom steps (divisor on the main camera's FOV): index 2 = default 3x.
        // Smaller divisor = closer view. Buttons/keys shift the index.
        private static readonly float[] ZoomSteps = { 6f, 4.5f, 3f, 2f, 1.5f, 1f };
        private int zoomIndex = 2;
        private float PipFovDivisor { get { return ZoomSteps[zoomIndex]; } }
        private const float IdentifyConeDeg = 0.5f;
        // v1.12: 2.5 s (was 5 s) — author request, faster sight workflow.
        private const float IdentifyHoldSeconds = 2.5f;
        private const float StarIdIntervalSeconds = 0.2f;
        private const float PainOnGlare = 0.6f;
        private const float PainOffGlare = 0.5f;
        private const float SunDriftLogIntervalSeconds = 10f;
        private const string SunName = "\u0421\u043E\u043B\u043D\u0446\u0435";
        private const string PainMessage =
            "\u041D\u0415 \u041C\u041E\u0413\u0423, \u0413\u041B\u0410\u0417\u0410\u041C \u0411\u041E\u041B\u042C\u041D\u041E \u2014 " +
            "\u0423\u0421\u0422\u0410\u041D\u041E\u0412\u0418\u0422\u0415 \u0424\u0418\u041B\u042C\u0422\u0420";
        private const string HintText =
            "\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0443\u0433\u043E\u043B \u00B7 Shift: \u0431\u044B\u0441\u0442\u0440\u0435\u0435 \u00B7 " +
            "Ctrl+\u0421\u0442\u0440\u0435\u043B\u043A\u0438: \u0442\u043E\u0447\u043D\u043E (0,2\u2032) \u00B7 " +
            "1\u20133: \u0444\u0438\u043B\u044C\u0442\u0440 \u043F\u0440\u0430\u0432\u044B\u0439 \u00B7 Ctrl+1\u20133: \u043B\u0435\u0432\u044B\u0439 \u00B7 " +
            "0: \u0441\u043D\u044F\u0442\u044C \u00B7 R: 0\u00B0 \u00B7 +/\u2212: \u0437\u0443\u043C \u00B7 " +
            "I: \u0437\u0430\u043C\u0435\u0440 \u00B7 O: \u0441\u0435\u0440\u0438\u044F \u00B7 Z: \u0432\u044B\u0445\u043E\u0434";
        private const string SunAltPrefix =
            "\u0412\u044B\u0441\u043E\u0442\u0430 \u0441\u043E\u043B\u043D\u0446\u0430 \u043D\u0430\u0434 \u0443\u0440\u043E\u0432\u043D\u0435\u043C \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u0430: ";
        private const string MoonLabel = "\u041B\u0443\u043D\u0430";
        private const string EyeHeightLabel = "\u0413\u043B\u0430\u0437";
        private const float BodiesReadoutIntervalSeconds = 1f;
        private const int BodiesMaxStars = 10;

        // F-Tafel worksheet card strings (v1.9).
        private const string FixCardTitle =
            "\u0410\u0421\u0422\u0420\u041E\u041D\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042F";
        private const string FixCardSight =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415";
        private const string FixCardNoSight =
            "\u041D\u0415\u0422 \u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u042F";
        private const string FixCardNoPosition =
            "\u041D\u0415\u0422 \u041F\u041E\u0417\u0418\u0426\u0418\u0418";
        private const string FixCardRecorded1 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21161 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardRecorded2 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21162 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardNewSeries =
            "\u041D\u041E\u0412\u0410\u042F \u0421\u0415\u0420\u0418\u042F";
        private const string FixCardAwaitSecond =
            "\u0417\u0430\u043F\u0438\u0448\u0438\u0442\u0435 \u0432\u0442\u043E\u0440\u043E\u0435 " +
            "\u043D\u0430\u0431\u043B\u044E\u0434\u0435\u043D\u0438\u0435 (I)";
        private const string FixCardFirstHint =
            "\u0421\u043E\u043B\u043D\u0446\u0435 \u0432 \u0446\u0435\u043D\u0442\u0440, 2.5 \u0441, " +
            "\u0437\u0430\u0442\u0435\u043C I";
        private const string FixCardVersetzung =
            "\u0412\u0415\u0420\u0421\u0415\u0422\u0426\u0423\u041D\u0413";
        private const string FixCardCourse = "\u041A\u0443\u0440\u0441";
        private const string FixCardSpeed = "\u0421\u043A\u043E\u0440";
        private const string FixCardMinutes = "\u043C\u0438\u043D";
        private const string FixCardTransfer = "\u041F\u0435\u0440\u0435\u043D\u043E\u0441";
        private const string FixCardError =
            "\u041E\u0448\u0438\u0431\u043A\u0430 \u043E\u0442 \u0441\u0447\u0438\u0441\u043B\u0435\u043D\u0438\u044F";
        private const string FixCardQuadrant = "\u043A\u0432.";
        private const string FixCardCalibration =
            "\u041A\u0430\u043B\u0438\u0431\u0440\u043E\u0432\u043A\u0430";
        private const string FixCardGeometry =
            "\u0413\u0435\u043E\u043C\u0435\u0442\u0440\u0438\u044F";
        private const string FixCardGeomGood =
            "\u0445\u043E\u0440\u043E\u0448\u043E";
        private const string FixCardGeomFair =
            "\u043F\u043E\u0441\u0440\u0435\u0434\u0441\u0442\u0432\u0435\u043D\u043D\u043E";
        private const string FixCardGeomPoor =
            "\u0441\u043B\u0430\u0431\u043E";
        private const string FixCardGeomAdvice =
            "<15\u00B0: \u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 " +
            "\u0437\u0430\u043C\u0435\u0440\u044B \u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438";

        /// <summary>True while the overlay is up; ConstellationMesh gates Z on it (§7.4).</summary>
        public static bool IsOpen { get; private set; }

        internal double arcAngle;
        private int leftFilterCount;
        private int rightFilterCount;
        private float holdTimer;
        private float fineRepeatTimer;
        private float starIdTimer;
        private string lastFoundName;
        private string identifiedName = "";
        private bool pain;
        private double lastFormattedArc = double.MinValue;
        private float sunDriftLogTimer;
        private float vectorLogHoldTimer;
        private float vectorRotateLogTimer;
        private float lastVectorLogYaw;
        private float renderCheckTimer;
        private bool renderCheckDone;
        private bool renderCheckSecondPending;
        private bool renderCheckSecondDone;
        private Texture2D renderCheckTex;
        private float manualRenderLogTimer;

        private Camera viewCamera;
        private MainCamera gameMainCamera;
        private GameInput gameInput;

        // Game sun access for SUNDRIFT diagnostics only (SEXTANT_PLAN v1.3):
        // TOD_Sky is not referenceable by the in-game mod compiler, so the
        // game values are read via cached reflection and never named.
        private Weather weather;
        private PropertyInfo gameSkyProperty;
        private MemberInfo gameSunDirMember;
        private MemberInfo gameSunAltMember;
        private bool gameSunMembersResolved;
        private bool gameSunUnavailableWarned;

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
        private Text sunAltText;
        private string lastSunAltLine;
        private Text bodiesAltText;
        private string lastBodiesLine;
        private float bodiesReadoutTimer;
        private Texture2D ringTexture;
        private Texture2D circleMaskTexture;
        private static Font sextantOsFont;

        // PPv2 bloom-cut volumes live in SextantBloomCut (extracted).
        private readonly SextantBloomCut bloomCut = new SextantBloomCut();
        private bool sunDiscSourceLogged;

        private readonly IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();

        // Running-fix worksheet state (v1.9): two recorded sights, the
        // calculator, the card text and a course/speed track sampler.
        private readonly RunningFixCalculator runningFix = new RunningFixCalculator();
        private Text fixCardText;
        private string lastFixCardLine;
        private string fixStatusLine = "";
        private float fixStatusTimer;
        private float fixPanelTimer;
        private float courseSampleTimer;
        private float smoothedCourseDeg;
        private float smoothedSpeedKn;
        private struct TrackSample
        {
            public DateTime Utc;
            public double Lat;
            public double Lon;
        }
        private readonly List<TrackSample> trackSamples = new List<TrackSample>(16);

        // v1.13 frozen fix: the Versetzung/geometry/FIX card section is built
        // once at observation-2 record time and never recomputed by the 1 Hz
        // refresh or by reopening the sextant.
        private string fixSectionText;

        // SUNDRIFT calibration (v1.10): solved against the game sun on each
        // open, persisted in PlayerPrefs, applied to all worksheet ephemeris.
        private const string SunCalibrationPrefsKey = "RadioNav.SunCalibrationMinutes";

        private void Awake()
        {
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
            // under the stored ?t, a new ?t would put the two sights on
            // different ephemerides and skew the LOPs. Re-solving happens
            // only when the next open starts with an empty series.
            if (runningFix.Observation1 != null)
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
            lastFixCardLine = null; // card shows the calibration line
        }

        [Inject] private static ResourceManager resourceManager = null;
        private Material opaqueViewMaterial;
        private bool opaqueViewWarned;

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
                UpdateVectorRotateLog();
                RenderCheck(Time.unscaledDeltaTime);
                UpdateGlare(Time.unscaledDeltaTime);
                UpdateStarId(Time.unscaledDeltaTime);
                UpdateReadout(false);
                UpdateBodiesReadout(Time.unscaledDeltaTime);
                HandleFixKeys();
                UpdateCourseSampler(Time.unscaledDeltaTime);
                UpdateFixPanel(Time.unscaledDeltaTime);
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
            if (renderCheckTex != null) Destroy(renderCheckTex);
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
            holdTimer = 0f;
            starIdTimer = 0f;
            lastFoundName = null;
            identifiedName = "";
            pain = false;
            lastFormattedArc = double.MinValue;
            lastSunAltLine = null;
            lastBodiesLine = null;
            lastFixCardLine = null;
            fixPanelTimer = 0f;
            bodiesReadoutTimer = 0f;
            sunDriftLogTimer = 0f; // force the first SUNDRIFT line on open
            renderCheckDone = false;
            renderCheckTimer = 1.0f;
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

            if (VectorLogEnabled)
            {
                vectorLogHoldTimer -= dt;
                bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
                if (pressed || vectorLogHoldTimer <= 0f)
                {
                    vectorLogHoldTimer = VectorLogHoldIntervalSeconds;
                    LogVectorState(pressed ? "press" : "hold");
                }
            }

            bool fast = ShiftHeld();
            float rate = RateDegPerSec * (fast ? ShiftMultiplier : 1f);
            arcAngle = SextantArc.Quantize(arcAngle + dir * rate * dt);
        }

        /// <summary>One fine-adjustment step (0.2' drum tick), always on-grid.</summary>
        private void StepArcFine(float dir)
        {
            arcAngle = SextantArc.Quantize(arcAngle + dir * FineStepQuant / SextantArc.StepsPerDeg);
        }

        /// <summary>
        /// Logs the player's eye vector when the view camera rotates
        /// (mouse look / boat turn) while the sextant is open.
        /// </summary>
        private void UpdateVectorRotateLog()
        {
            if (!VectorLogEnabled || viewCamera == null)
                return;
            vectorRotateLogTimer -= Time.unscaledDeltaTime;
            if (vectorRotateLogTimer > 0f)
                return;
            float yaw = viewCamera.transform.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(lastVectorLogYaw, yaw)) >= VectorRotateMinDeltaDeg)
            {
                vectorRotateLogTimer = VectorLogHoldIntervalSeconds;
                LogVectorState("rotate");
            }
        }

        /// <summary>
        /// RT pixel probe: once ~1 s after open, and once more ~1 s after the
        /// alidade first passes 20 deg - proves the PIP cameras render and
        /// that the right RT content follows the pose (frozen = same colors).
        /// </summary>
        private void RenderCheck(float dt)
        {
            if (renderCheckDone && !renderCheckSecondPending)
                return;

            if (!renderCheckDone)
            {
                renderCheckTimer -= dt;
                if (renderCheckTimer > 0f)
                    return;
                renderCheckDone = true;
                DoRenderCheck("open");
                return;
            }

            if (arcAngle > 20.0 && !renderCheckSecondPending && !renderCheckSecondDone)
            {
                renderCheckSecondPending = true;
                renderCheckTimer = 1.0f;
            }
            if (renderCheckSecondPending)
            {
                renderCheckTimer -= dt;
                if (renderCheckTimer > 0f)
                    return;
                renderCheckSecondPending = false;
                renderCheckSecondDone = true;
                DoRenderCheck("arc20");
            }
        }

        private void DoRenderCheck(string tag)
        {
            if (leftRt == null || rightRt == null || !leftRt.IsCreated() || !rightRt.IsCreated())
            {
                Debug.LogWarning(LogPrefix + " RENDERCHECK rt missing");
                return;
            }
            if (renderCheckTex == null)
                renderCheckTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture prev = RenderTexture.active;
                Color lt = SampleRt(leftRt, 0.9f);
                Color lm = SampleRt(leftRt, 0.5f);
                Color lb = SampleRt(leftRt, 0.1f);
                Color rtTop = SampleRt(rightRt, 0.9f);
                Color rm = SampleRt(rightRt, 0.5f);
                Color rb = SampleRt(rightRt, 0.1f);
                RenderTexture.active = prev;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} RENDERCHECK {1} left(t/m/b)=#{2}({3:F2})/#{4}({5:F2})/#{6}({7:F2}) right(t/m/b)=#{8}({9:F2})/#{10}({11:F2})/#{12}({13:F2})",
                    LogPrefix, tag,
                    ColorUtility.ToHtmlStringRGB(lt), lt.a,
                    ColorUtility.ToHtmlStringRGB(lm), lm.a,
                    ColorUtility.ToHtmlStringRGB(lb), lb.a,
                    ColorUtility.ToHtmlStringRGB(rtTop), rtTop.a,
                    ColorUtility.ToHtmlStringRGB(rm), rm.a,
                    ColorUtility.ToHtmlStringRGB(rb), rb.a));
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " RENDERCHECK failed: " + e.Message);
            }
        }

        private Color SampleRt(RenderTexture target, float v)
        {
            RenderTexture.active = target;
            renderCheckTex.ReadPixels(new Rect(target.width / 2, (int)(target.height * v), 1, 1), 0, 0);
            return renderCheckTex.GetPixel(0, 0);
        }

        /// <summary>
        /// Vector telemetry on alidade input: left camera (player eyes, pitch
        /// 0), right camera (eyes + alidade angle), the player view camera,
        /// derived angles and the sun vectors (own Meeus vs game TOD_Sky).
        /// </summary>
        private void LogVectorState(string trigger)
        {
            ResolveViewCamera();
            var l = ReadCameraVectors(pipLeftCamera);
            var r = ReadCameraVectors(pipRightCamera);
            var v = ReadCameraVectors(viewCamera);
            var sun = ReadSunVectors();
            double q = SextantArc.Quantize(arcAngle);
            float angLR = pipLeftCamera != null && pipRightCamera != null
                ? Vector3.Angle(l.Forward, r.Forward) : 0f;
            float dotLR = pipLeftCamera != null && pipRightCamera != null
                ? Vector3.Dot(l.Forward, r.Forward) : 0f;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECL {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(l.Position), V3(l.Euler), V3(l.Forward), V3(l.Up), V3(l.Right)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECR {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(r.Position), V3(r.Euler), V3(r.Forward), V3(r.Up), V3(r.Right)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECV {1} pos={2} eul={3} fwd={4}",
                LogPrefix, trigger, V3(v.Position), V3(v.Euler), V3(v.Forward)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECD {1} arc={2:F6} quant={3:F6} fmt={4} angLR={5:F3} dotLR={6:F6}",
                LogPrefix, trigger, arcAngle, q, SextantArc.Format(q), angLR, dotLR));
            LogSunLines(trigger, r.Forward, sun);
        }

        private void ResolveViewCamera()
        {
            if (viewCamera == null && !CameraResolver.TryResolve(out viewCamera))
                viewCamera = FindObjectOfType<Camera>();
            if (viewCamera != null)
                lastVectorLogYaw = viewCamera.transform.eulerAngles.y;
        }

        private struct CameraVectors
        {
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
            public Vector3 Position;
            public Vector3 Euler;
        }

        private static CameraVectors ReadCameraVectors(Camera cam)
        {
            if (cam == null)
                return default;
            return new CameraVectors
            {
                Forward = cam.transform.forward,
                Up = cam.transform.up,
                Right = cam.transform.right,
                Position = cam.transform.position,
                Euler = cam.transform.eulerAngles,
            };
        }

        private struct SunVectors
        {
            public bool Own;
            public Vector3 OwnDir;
            public float OwnAlt;
            public float OwnAz;
            public bool Game;
            public Vector3 GameDir;
            public float GameAlt;
        }

        private SunVectors ReadSunVectors()
        {
            var sun = new SunVectors();
            sun.Own = TryGetOwnSun(out sun.OwnDir, out sun.OwnAlt, out sun.OwnAz);
            sun.Game = TryGetGameSun(out sun.GameDir, out sun.GameAlt);
            return sun;
        }

        private void LogSunLines(string trigger, Vector3 rightFwd, SunVectors sun)
        {
            if (sun.Own || sun.Game)
            {
                float angSun = sun.Own ? Vector3.Angle(rightFwd, sun.OwnDir) : -1f;
                float sep = sun.Own && sun.Game ? Vector3.Angle(sun.OwnDir, sun.GameDir) : -1f;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} VECSUN {1} own{2} alt={3:F2} az={4:F1} game{5} galt={6:F2} angSunR={7:F3} sep={8:F3}",
                    LogPrefix, trigger, sun.Own ? V3(sun.OwnDir) : " n/a", sun.OwnAlt, sun.OwnAz,
                    sun.Game ? V3(sun.GameDir) : " n/a", sun.GameAlt, angSun, sep));
            }
            if (sun.Own && pipRightCamera != null)
            {
                // Sun projected into the right camera's viewport: proves the
                // rendered picture matches the astro math (center = 0.5,0.5).
                Vector3 sunNdc = pipRightCamera.WorldToViewportPoint(pipRightCamera.transform.position + sun.OwnDir * 1000f);
                float nx = (sunNdc.x - 0.5f) * 2f;
                float ny = (sunNdc.y - 0.5f) * 2f;
                float tanHalf = Mathf.Tan(pipRightCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                if (tanHalf < 0.001f) tanHalf = 0.176f;
                float dxDeg = Mathf.Atan2(nx * tanHalf, 1f) * Mathf.Rad2Deg;
                float dyDeg = Mathf.Atan2(ny * tanHalf, 1f) * Mathf.Rad2Deg;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} VECSP {1} ndc=({2:F3},{3:F3}) offDeg=({4:F2},{5:F2}) fov={6:F1}",
                    LogPrefix, trigger, sunNdc.x, sunNdc.y, dxDeg, dyDeg,
                    pipRightCamera.fieldOfView));
            }
        }

        private static string V3(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z);
        }

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

        // ---- running fix worksheet (v1.9) ---------------------------------------

        private void HandleFixKeys()
        {
            // I records a sight (only with the sun identified and held),
            // O starts a new worksheet series.
            if (Input.GetKeyDown(KeyCode.I))
                CaptureObservation();
            if (Input.GetKeyDown(KeyCode.O))
                ResetSeries();
        }

        private void CaptureObservation()
        {
            if (identifiedName != SunName)
            {
                fixStatusLine = FixCardNoSight;
                fixStatusTimer = FixStatusSeconds;
                fixPanelTimer = 0f;
                Debug.Log(LogPrefix + " FIXKEY I ignored - sun not identified");
                return;
            }
            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
            {
                fixStatusLine = FixCardNoPosition;
                fixStatusTimer = FixStatusSeconds;
                fixPanelTimer = 0f;
                Debug.Log(LogPrefix + " FIXKEY I ignored - no sandbox position");
                return;
            }

            double ho = SextantArc.Quantize(arcAngle);
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                GameTimeResolver.GetUtcNow(), ho, latF, lonF);
            runningFix.Record(obs);
            FixMapMarker.OnNewObservation();
            // v1.13: the fix is a snapshot computed at record time only.
            if (runningFix.CanSolve)
                RecomputeFixSnapshot();
            else
                fixSectionText = null;
            bool isSecond = runningFix.Observation2 == obs;
            fixStatusLine = isSecond ? FixCardRecorded2 : FixCardRecorded1;
            fixStatusTimer = FixStatusSeconds;
            fixPanelTimer = 0f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} OBS n={1} ho={2:F2} dr=({3:F4},{4:F4}) hc={5:F2} az={6:F2} a={7:F1}NM",
                LogPrefix, isSecond ? 2 : 1, ho, latF, lonF,
                obs.HcDeg, obs.AzDeg, obs.InterceptNm));
        }

        private void ResetSeries()
        {
            runningFix.Reset();
            FixMapMarker.Clear();
            fixSectionText = null;
            fixStatusLine = FixCardNewSeries;
            fixStatusTimer = FixStatusSeconds;
            fixPanelTimer = 0f;
            Debug.Log(LogPrefix + " FIX series reset");
        }

        /// <summary>
        /// Course/speed over ground from the recent position track (10 s
        /// window, 2 s sampling). Below the transfer speed threshold the boat
        /// counts as anchored: speed 0, course keeps its last value.
        /// </summary>
        private void UpdateCourseSampler(float dt)
        {
            courseSampleTimer -= dt;
            if (courseSampleTimer > 0f)
                return;
            courseSampleTimer = CourseSampleIntervalSeconds;

            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
                return;
            DateTime utc = GameTimeResolver.GetUtcNow();
            trackSamples.Add(new TrackSample { Utc = utc, Lat = latF, Lon = lonF });
            while (trackSamples.Count > 0 &&
                   (utc - trackSamples[0].Utc).TotalSeconds > CourseSampleWindowSeconds + 2.0)
                trackSamples.RemoveAt(0);
            if (trackSamples.Count < 2)
                return;

            TrackSample first = trackSamples[0];
            TrackSample last = trackSamples[trackSamples.Count - 1];
            double spanHours = (last.Utc - first.Utc).TotalHours;
            if (spanHours < CourseSampleWindowSeconds / 3600.0 * 0.6)
                return; // not enough baseline yet right after opening

            double distKm = GeoMath.HaversineKm(
                (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
            double kn = distKm / 1.852 / spanHours;
            if (kn >= MinTransferSpeedKn)
            {
                smoothedCourseDeg = GeoMath.InitialBearingDeg(
                    (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
                smoothedSpeedKn = (float)kn;
            }
            else
            {
                smoothedSpeedKn = 0f;
            }
        }

        private void UpdateFixPanel(float dt)
        {
            if (fixCardText == null)
                return;
            if (fixStatusTimer > 0f)
                fixStatusTimer -= dt;
            fixPanelTimer -= dt;
            if (fixPanelTimer > 0f)
                return;
            fixPanelTimer = FixPanelIntervalSeconds;

            string card = BuildFixCard();
            if (card != lastFixCardLine)
            {
                fixCardText.text = card;
                lastFixCardLine = card;
            }
        }

        private void AppendObservation(StringBuilder sb, SextantObservation obs, int index)
        {
            // F-Tafel block order: UT -> gamma/SHA/GHA -> lambda_g/LHA ->
            // delta -> h_b/h1 -> Az/intercept.
            sb.Append(FixCardSight).Append(" \u2116").Append(index).Append('\n');
            sb.Append("UT ").Append(obs.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("\u03B3 ").Append(SextantDmFormat.FormatDm(obs.GhaAriesDeg));
            // Same D°M.m′ format as gamma/GHA: a 0.1° decimal SHA read as
            // 198°18.0′ made gamma+SHA look 2′ off GHA on the card (in-game
            // verification 2026-08-30) — the chain is exact, only the old
            // display rounded SHA to ±3′.
            sb.Append("  SHA ").Append(SextantDmFormat.FormatDm(obs.ShaSunDeg)).Append('\n');
            sb.Append("GHA ").Append(SextantDmFormat.FormatDm(obs.GhaSunDeg)).Append('\n');
            sb.Append("\u03BBg ").Append(SextantDmFormat.FormatLatLon(obs.DrLonDeg, false));
            sb.Append("  LHA ").Append(SextantDmFormat.FormatDm(obs.LhaDeg));
            sb.Append(' ').Append(FixCardQuadrant).Append(obs.Quadrant)
              .Append(obs.QuadrantWest ? " W" : " O").Append('\n');
            sb.Append("\u03B4 ").Append(SextantDmFormat.FormatSignedDm(obs.DecDeg)).Append('\n');
            sb.Append("hb ").Append(SextantDmFormat.FormatDm(obs.HoDeg));
            sb.Append("  h1 ").Append(SextantDmFormat.FormatDm(obs.HcDeg)).Append('\n');
            sb.Append("Az ").Append(obs.AzDeg.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append("  a ").Append(obs.InterceptNm >= 0 ? "+" : "\u2212")
              .Append(Math.Abs(obs.InterceptNm).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" NM");
        }

        private string BuildFixCard()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append(FixCardTitle).Append('\n');
            if (fixStatusTimer > 0f && !string.IsNullOrEmpty(fixStatusLine))
                sb.Append(fixStatusLine).Append('\n');
            double calMinutes = SunEphemerisCalibration.DeltaMinutes;
            if (Math.Abs(calMinutes) > 0.01)
            {
                sb.Append(FixCardCalibration).Append(" \u0394t ")
                  .Append(calMinutes >= 0 ? "+" : "\u2212")
                  .Append(Math.Abs(calMinutes).ToString("F1", CultureInfo.InvariantCulture))
                  .Append(' ').Append(FixCardMinutes).Append('\n');
            }

            if (runningFix.Observation1 == null)
            {
                sb.Append(FixCardFirstHint);
                return sb.ToString();
            }
            AppendObservation(sb, runningFix.Observation1, 1);
            sb.Append('\n');
            if (runningFix.Observation2 == null)
            {
                sb.Append('\n').Append(FixCardAwaitSecond);
                return sb.ToString();
            }
            sb.Append('\n');
            AppendObservation(sb, runningFix.Observation2, 2);
            sb.Append('\n').Append('\n');

            // v1.13: the Versetzung/geometry/FIX tail is a SNAPSHOT computed
            // once when observation 2 was recorded (I key) — reopening the
            // sextant or the passage of time never recomputes it. The math
            // belongs to the two recorded sights, not to the live view.
            if (fixSectionText != null)
                sb.Append(fixSectionText);
            return sb.ToString();
        }

        /// <summary>
        /// Builds the frozen Versetzung/geometry/FIX section from the two
        /// recorded observations with the course/speed current at record
        /// time. Called only from CaptureObservation (and cleared by
        /// ResetSeries) — never on the 1 Hz card refresh.
        /// </summary>
        private void RecomputeFixSnapshot()
        {
            StringBuilder sb = new StringBuilder(256);

            double dtHours = (runningFix.Observation2.Utc - runningFix.Observation1.Utc).TotalHours;
            if (dtHours < 0.0)
                dtHours = 0.0;
            double transferNm = smoothedSpeedKn * dtHours;
            sb.Append(FixCardVersetzung).Append('\n');
            sb.Append(FixCardCourse).Append(' ')
              .Append(smoothedCourseDeg.ToString("F0", CultureInfo.InvariantCulture)).Append('\u00B0')
              .Append(' ').Append(FixCardSpeed).Append(' ')
              .Append(smoothedSpeedKn.ToString("F1", CultureInfo.InvariantCulture)).Append(" kn\n");
            sb.Append("\u0394t ").Append((dtHours * 60.0).ToString("F0", CultureInfo.InvariantCulture))
              .Append(' ').Append(FixCardMinutes).Append(' ')
              .Append(FixCardTransfer).Append(' ')
              .Append(transferNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM\n");

            // Crossing-angle quality: shallow geometry amplifies small
            // intercept errors (~1/sin θ) — warn the navigator.
            double crossDeg = RunningFixCalculator.CrossingAngleDeg(
                runningFix.Observation1, runningFix.Observation2);
            sb.Append(FixCardGeometry).Append(' ')
              .Append(crossDeg.ToString("F0", CultureInfo.InvariantCulture))
              .Append('\u00B0').Append(" \u2014 ");
            if (crossDeg > 30.0)
                sb.Append(FixCardGeomGood).Append('\n');
            else if (crossDeg >= 15.0)
                sb.Append(FixCardGeomFair).Append('\n');
            else
                sb.Append(FixCardGeomPoor).Append('\n')
                  .Append(FixCardGeomAdvice).Append('\n');

            double fixLat, fixLon;
            if (!runningFix.CalculateFix(smoothedCourseDeg, smoothedSpeedKn, out fixLat, out fixLon))
            {
                FixMapMarker.Clear();
                sb.Append("FIX \u2014");
                fixSectionText = sb.ToString();
                return;
            }
            sb.Append("FIX\n");
            sb.Append("\u03C6 ").Append(SextantDmFormat.FormatLatLon(fixLat, true));
            sb.Append("  \u03BB ").Append(SextantDmFormat.FormatLatLon(fixLon, false)).Append('\n');
            FixMapMarker.Set(fixLat, fixLon,
                "FIX " + runningFix.Observation2.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                "\nlat " + SextantDmFormat.FormatLatLonAscii(fixLat, true) +
                " lon " + SextantDmFormat.FormatLatLonAscii(fixLon, false));
            float curLat, curLon;
            if (positionProvider.TryGetPosition(out curLat, out curLon))
            {
                double errNm = RunningFixCalculator.FixErrorNm(fixLat, fixLon, curLat, curLon);
                sb.Append(FixCardError).Append(' ')
                  .Append(errNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM");
            }
            fixSectionText = sb.ToString();
        }

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
            if (VectorLogEnabled)
            {
                manualRenderLogTimer -= Time.unscaledDeltaTime;
                if (manualRenderLogTimer <= 0f)
                {
                    manualRenderLogTimer = 1f;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "{0} MANUALRENDER frame={1} arc={2:F2}",
                        LogPrefix, Time.frameCount, arcAngle));
                }
            }
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
                        gameOk ? V3(gameDir) : " n/a"));
                }
            }
            else
            {
                UpdateSunReadout(false, 0f);
            }
            ApplyGlare(g);
            LogSunDrift(dt);
        }

        /// <summary>
        /// Right-side verification readout: own Meeus sun altitude as the
        /// reference the arc should match at alignment.
        /// </summary>
        private void UpdateSunReadout(bool available, float sunAltDeg)
        {
            if (sunAltText == null)
                return;
            string line = available
                ? SunAltPrefix + sunAltDeg.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0"
                : SunAltPrefix + "\u2014";
            if (line != lastSunAltLine)
            {
                sunAltText.text = line;
                lastSunAltLine = line;
            }
        }

        /// <summary>
        /// Small-font readout below the sun line: Moon altitude plus the nav
        /// stars currently above the horizon (top N by altitude). Throttled
        /// to 1 s - bodies move far too slowly to justify per-frame strings.
        /// </summary>
        private void UpdateBodiesReadout(float dt)
        {
            if (bodiesAltText == null)
                return;
            bodiesReadoutTimer -= dt;
            if (bodiesReadoutTimer > 0f)
                return;
            bodiesReadoutTimer = BodiesReadoutIntervalSeconds;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
            {
                SetBodiesLine(null);
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            var entries = new List<KeyValuePair<float, string>>();
            float moonAlt;
            bool moonUp = TryGetMoonAlt(jd, lstDeg, lat, out moonAlt);

            foreach (StarEntry star in StarCatalog.Stars)
            {
                if (!star.IsNav)
                    continue;
                double altRad, azRad, e, n, u;
                AstroMath.RaDecToEnu(star.RaDeg * Mathf.Deg2Rad, star.DecDeg * Mathf.Deg2Rad,
                    lstDeg, lat, out altRad, out azRad, out e, out n, out u);
                float alt = (float)(altRad * Mathf.Rad2Deg);
                if (alt <= 0f)
                    continue;
                string name = NavStarNames.TryGetName(star.Hip, out string starName)
                    ? starName
                    : "HIP " + star.Hip.ToString(CultureInfo.InvariantCulture);
                entries.Add(new KeyValuePair<float, string>(alt, name + " " + FormatAlt(alt)));
            }

            entries.Sort((a, b) => b.Key.CompareTo(a.Key));

            // Eye height above sea level first (HE, meters - the value a
            // navigator needs for the dip correction), then the Moon, then
            // stars: top N by altitude.
            var sb = new StringBuilder();
            if (viewCamera != null)
                sb.Append(EyeHeightLabel + " " +
                    viewCamera.transform.position.y.ToString("F1", CultureInfo.InvariantCulture) + " \u043C");
            if (moonUp)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(MoonLabel + " " + FormatAlt(moonAlt));
            }
            int shown = 0;
            for (int i = 0; i < entries.Count && shown < BodiesMaxStars; i++)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(entries[i].Value);
                shown++;
            }
            SetBodiesLine(sb.Length > 0 ? sb.ToString() : null);
        }

        private void SetBodiesLine(string line)
        {
            if (line == lastBodiesLine)
                return;
            bodiesAltText.text = line ?? "";
            lastBodiesLine = line;
        }

        private static string FormatAlt(float altDeg)
        {
            return altDeg.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0";
        }

        /// <summary>Own Meeus moon altitude (negative below the horizon).</summary>
        private static bool TryGetMoonAlt(double jd, double lstDeg, double lat, out float altDeg)
        {
            altDeg = 0f;
            if (!AstroMath.MoonRaDec(jd, out double raRad, out double decRad))
                return false;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out _, out _, out _, out _);
            altDeg = (float)(altRad * Mathf.Rad2Deg);
            return true;
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
                holdTimer = 0f;
                identifiedName = "";
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
        /// SUNDRIFT telemetry (SEXTANT_PLAN v1.3): every 10 s while the sextant
        /// is open, log the angular separation between our Meeus sun vector and
        /// the game's TOD_Sky sun vector (read via cached reflection). A steady
        /// sep above ~0.5° means an ephemeris or axis-convention drift.
        /// </summary>
        private void LogSunDrift(float dt)
        {
            sunDriftLogTimer -= dt;
            if (sunDriftLogTimer > 0f)
                return;
            sunDriftLogTimer = SunDriftLogIntervalSeconds;

            Vector3 ownDir;
            float ownAlt;
            float ownAz;
            if (!TryGetOwnSun(out ownDir, out ownAlt, out ownAz))
                return; // no sandbox position - nothing to compare against

            Vector3 gameDir;
            float gameAlt;
            if (!TryGetGameSun(out gameDir, out gameAlt))
            {
                if (!gameSunUnavailableWarned)
                {
                    gameSunUnavailableWarned = true;
                    Debug.LogWarning(LogPrefix + " SUNDRIFT game sun unavailable via reflection - drift unmonitored");
                }
                return;
            }

            float sepDeg = Vector3.Angle(ownDir, gameDir);
            float dAlt = ownAlt - gameAlt;
            // Azimuths 0=N +east; game azimuth derived from the world vector
            // with the same (e>x, n>z) mapping the star sky proved in-game.
            float gameAz = Mathf.Atan2(gameDir.x, gameDir.z) * Mathf.Rad2Deg;
            if (gameAz < 0f) gameAz += 360f;
            float dAz = ownAz - gameAz;
            if (dAz > 180f) dAz -= 360f;
            if (dAz < -180f) dAz += 360f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} SUNDRIFT sep={1:F3} dalt={2:F3} daz={3:F3} meeus(alt={4:F2} az={5:F1}) game(alt={6:F2} az={7:F1})",
                LogPrefix, sepDeg, dAlt, dAz, ownAlt, ownAz, gameAlt, gameAz));
        }

        private bool TryGetGameSun(out Vector3 dir, out float alt)
        {
            dir = Vector3.zero;
            alt = 0f;
            if (weather == null && InjectionFramework.Instance.IsNotNull())
                weather = InjectionFramework.Instance.GetInstance<Weather>();
            if (weather == null)
                return false;

            try
            {
                if (gameSkyProperty == null)
                    gameSkyProperty = weather.GetType().GetProperty("Sky");
                if (gameSkyProperty == null)
                    return false;

                object sky = gameSkyProperty.GetValue(weather, null);
                if (sky == null)
                    return false;

                if (!gameSunMembersResolved)
                {
                    gameSunMembersResolved = true;
                    gameSunDirMember = FindSunMember(sky.GetType(), "SunDirection");
                    gameSunAltMember = FindSunMember(sky.GetType(), "SunAltitude");
                }
                if (gameSunDirMember == null || gameSunAltMember == null)
                    return false;

                object dirBoxed = ReadMember(sky, gameSunDirMember);
                if (!(dirBoxed is Vector3))
                    return false;
                dir = (Vector3)dirBoxed;
                alt = Convert.ToSingle(ReadMember(sky, gameSunAltMember));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " SUNDRIFT reflection failed: " + e.Message);
                return false;
            }
        }

        private static MemberInfo FindSunMember(Type type, string name)
        {
            PropertyInfo prop = type.GetProperty(name);
            if (prop != null)
                return prop;
            return type.GetField(name);
        }

        private static object ReadMember(object target, MemberInfo member)
        {
            FieldInfo field = member as FieldInfo;
            if (field != null)
                return field.GetValue(target);
            return ((PropertyInfo)member).GetValue(target, null);
        }

        // ---- star / sun identification ---------------------------------------

        private void UpdateStarId(float dt)
        {
            starIdTimer -= dt;
            bool tick = starIdTimer <= 0f;
            if (tick)
                starIdTimer = StarIdIntervalSeconds;

            if (pain)
            {
                holdTimer = 0f;
                identifiedName = "";
                nameText.text = "";
                return;
            }

            if (tick)
            {
                float bodyAlt;
                lastFoundName = FindNearestBodyName(out bodyAlt);
                if (lastFoundName != null)
                {
                    holdTimer += StarIdIntervalSeconds;
                    if (holdTimer >= IdentifyHoldSeconds)
                    {
                        bool justIdentified = identifiedName != lastFoundName;
                        identifiedName = lastFoundName;
                        if (justIdentified)
                            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                                "{0} IDHIT name={1} alt={2:F2} arc={3:F2} |d|={4:F2}",
                                LogPrefix, lastFoundName, bodyAlt, arcAngle,
                                Math.Abs(bodyAlt - (float)arcAngle)));
                    }
                }
                else
                {
                    holdTimer = 0f;
                    identifiedName = "";
                }
            }

            if (nameText.text != identifiedName)
                nameText.text = identifiedName;
        }

        private string FindNearestBodyName(out float bodyAltDeg)
        {
            // Sun ID aims at the direction the player actually sees: the disc
            // is drawn on the GAME sun (SUNDRIFT sep ~1.6° would never fit
            // the 0.5° cone against our own ephemeris). Own ephemeris stays
            // the fallback when the game sun is unavailable via reflection.
            Vector3 sunDir;
            float sunAlt;
            float sunAz;
            Vector3 pipFwd = GetPipForward();
            if (pipFwd.sqrMagnitude < 0.01f)
            {
                bodyAltDeg = -1f;
                return null;
            }
            if (TryGetOwnSun(out sunDir, out sunAlt, out sunAz))
            {
                Vector3 idTarget = sunDir;
                Vector3 gameDir;
                float gameAlt;
                if (TryGetGameSun(out gameDir, out gameAlt))
                    idTarget = gameDir;
                if (Vector3.Angle(pipFwd, idTarget) <= IdentifyConeDeg)
                {
                    bodyAltDeg = sunAlt;
                    return SunName;
                }
            }

            bodyAltDeg = -1f;
            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return null;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            Vector3 fwd = pipFwd;
            double bestCos = Math.Cos(IdentifyConeDeg * Mathf.Deg2Rad);
            int bestHip = -1;

            var stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].DecDeg < -89.5f && stars[i].Vmag > 6f)
                    continue; // cheap skip near-pole dim entries costs nothing
                AstroMath.RaDecToEnu(stars[i].RaDeg * Mathf.Deg2Rad, stars[i].DecDeg * Mathf.Deg2Rad,
                    lstDeg, lat, out double altRad, out _, out double e, out double n, out double u);
                if (altRad <= 0)
                    continue;

                double len = Math.Sqrt(e * e + n * n + u * u);
                if (len < 1e-9)
                    continue;
                double cos = (e * fwd.x + u * fwd.y + n * fwd.z) / len;
                if (cos > bestCos)
                {
                    bestCos = cos;
                    bestHip = stars[i].Hip;
                    bodyAltDeg = (float)(altRad * Mathf.Rad2Deg);
                }
            }

            if (bestHip < 0)
                return null;

            string name;
            return NavStarNames.TryGetName(bestHip, out name) ? name : ("HIP " + bestHip);
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

        // ---- overlay construction (§13.3) --------------------------------------

        private void EnsureOverlay()
        {
            if (canvasRoot != null)
                return;

            canvasRoot = new GameObject("RadioNavSextantOverlay", typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // Veil: solid dark layer, no cut-out (§13.3 - aspect-safe).
            GameObject veilGo = new GameObject("Veil", typeof(Image));
            veilGo.transform.SetParent(canvasRoot.transform, false);
            Image veil = veilGo.GetComponent<Image>();
            veil.color = new Color(0f, 0f, 0f, VeilAlpha);
            Stretch(veil.rectTransform);

            // Eyecup (наглазник): black shade larger than the ocular, drawn
            // between the veil and the lens so the world around the field of
            // view sinks into near-black while instrument texts (later
            // siblings) stay readable. Solid over the ocular rim, long soft
            // falloff outward - out-of-focus rubber close to the eye.
            GameObject eyecupGo = new GameObject("Eyecup", typeof(Image));
            eyecupGo.transform.SetParent(canvasRoot.transform, false);
            Image eyecup = eyecupGo.GetComponent<Image>();
            eyecup.sprite = SextantSprites.CreateEyecupSprite(SextantSprites.CreateEyecupTexture());
            eyecup.raycastTarget = false;
            AnchorCenter(eyecup.rectTransform, EyecupSizeUnits, EyecupSizeUnits);

            // Ocular: square container clipped to a circle (in-game finding
            // 2026-08-26: unclipped corners painted opaque black over the live
            // horizon). Mask clips both RT views, glare and ring to the circle;
            // outside it the veil shows through.
            GameObject pipGo = new GameObject("Ocular", typeof(Image), typeof(Mask));
            pipGo.transform.SetParent(canvasRoot.transform, false);
            pipRect = pipGo.GetComponent<RectTransform>();
            AnchorCenter(pipRect, OcularSizeUnits, OcularSizeUnits);
            Image maskGraphic = pipGo.GetComponent<Image>();
            maskGraphic.sprite = SextantSprites.CreateCircleMaskSprite();
            maskGraphic.raycastTarget = false;
            Mask ocularMask = pipGo.GetComponent<Mask>();
            ocularMask.showMaskGraphic = false;

            // Opaque black plate under the PIP views: hides the world behind
            // the lenses regardless of the RT alpha left by the game's post
            // stack. Clipped to the circle by the Mask; the veil outside
            // stays unchanged (vignette).
            GameObject backingGo = new GameObject("LensBacking", typeof(Image));
            backingGo.transform.SetParent(pipGo.transform, false);
            Image backing = backingGo.GetComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 1f);
            backing.raycastTarget = false;
            Stretch(backing.rectTransform);

            // Left half: fixed-horizon camera view (pitch 0). Shows only the
            // left half of its RT so the optical axis sits at the split line
            // (screen center); the right half of the frame is not drawn.
            GameObject leftGo = new GameObject("LeftView", typeof(RawImage));
            leftGo.transform.SetParent(pipGo.transform, false);
            leftViewImage = leftGo.GetComponent<RawImage>();
            leftViewImage.raycastTarget = false;
            leftViewImage.texture = leftRt;
            leftViewImage.rectTransform.anchorMin = new Vector2(0f, 0f);
            leftViewImage.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            leftViewImage.rectTransform.offsetMin = Vector2.zero;
            leftViewImage.rectTransform.offsetMax = Vector2.zero;
            leftViewImage.uvRect = new Rect(0f, 0f, 0.5f, 1f);

            // Right half: mirror camera pitched to the arc angle (0-130).
            // Right half of its RT only; at arc=0 the halves form one
            // continuous circling picture.
            GameObject rightGo = new GameObject("RightView", typeof(RawImage));
            rightGo.transform.SetParent(pipGo.transform, false);
            rightViewImage = rightGo.GetComponent<RawImage>();
            rightViewImage.raycastTarget = false;
            rightViewImage.texture = rightRt;
            rightViewImage.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rightViewImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            rightViewImage.rectTransform.offsetMin = Vector2.zero;
            rightViewImage.rectTransform.offsetMax = Vector2.zero;
            rightViewImage.uvRect = new Rect(0.5f, 0f, 0.5f, 1f);

            // Opaque view material (bundle shader RadioNav/UIOpaque, Blend
            // One Zero): the game's post stack writes alpha < 1 into the
            // RTs (RENDERCHECK 2026-08-28: 0.29-0.47) and the stock UI
            // shader blends the black lens backing through, darkening the
            // picture. With the opaque shader the views render at full
            // brightness; the backing stays only as a safety layer.
            opaqueViewMaterial = ResolveOpaqueViewMaterial();
            if (opaqueViewMaterial != null)
            {
                leftViewImage.material = opaqueViewMaterial;
                rightViewImage.material = opaqueViewMaterial;
            }

            // Vertical sight line (1 physical pixel — drum alignment hairline
            // between the direct/mirror halves).
            GameObject splitGo = new GameObject("VerticalSplit", typeof(Image));
            splitGo.transform.SetParent(pipGo.transform, false);
            Image splitImage = splitGo.GetComponent<Image>();
            splitImage.color = new Color(1f, 1f, 1f, 0.5f);
            splitImage.raycastTarget = false;
            AnchorCenter(splitImage.rectTransform, 1f / canvas.scaleFactor, OcularSizeUnits);

            // Horizontal sight line (1 physical pixel, crosshair) — line of
            // sight, drawn above the views/below filter dims so it stays
            // subtle and never interferes with the lenses.
            GameObject horizGo = new GameObject("HorizontalSight", typeof(Image));
            horizGo.transform.SetParent(pipGo.transform, false);
            Image horizImage = horizGo.GetComponent<Image>();
            horizImage.color = new Color(1f, 1f, 1f, 0.5f);
            horizImage.raycastTarget = false;
            AnchorCenter(horizImage.rectTransform, OcularSizeUnits, 1f / canvas.scaleFactor);

            // Shade filters dim the scope view (v1.5): separate left (horizon)
            // and right (sun) half dimmers - like the separate shade glasses
            // of a real sextant.
            GameObject dimLeftGo = new GameObject("FilterDimLeft", typeof(Image));
            dimLeftGo.transform.SetParent(pipGo.transform, false);
            filterDimLeftImage = dimLeftGo.GetComponent<Image>();
            filterDimLeftImage.color = new Color(0f, 0f, 0f, 0f);
            filterDimLeftImage.raycastTarget = false;
            RectTransform dimLeftRt = filterDimLeftImage.rectTransform;
            dimLeftRt.anchorMin = new Vector2(0f, 0f);
            dimLeftRt.anchorMax = new Vector2(0.5f, 1f);
            dimLeftRt.offsetMin = Vector2.zero;
            dimLeftRt.offsetMax = Vector2.zero;

            GameObject dimRightGo = new GameObject("FilterDimRight", typeof(Image));
            dimRightGo.transform.SetParent(pipGo.transform, false);
            filterDimRightImage = dimRightGo.GetComponent<Image>();
            filterDimRightImage.color = new Color(0f, 0f, 0f, 0f);
            filterDimRightImage.raycastTarget = false;
            RectTransform dimRightRt = filterDimRightImage.rectTransform;
            dimRightRt.anchorMin = new Vector2(0.5f, 0f);
            dimRightRt.anchorMax = new Vector2(1f, 1f);
            dimRightRt.offsetMin = Vector2.zero;
            dimRightRt.offsetMax = Vector2.zero;

            // One-shot placement proof: screen-space corners of both dim
            // halves (matches the FILTERSIDE logs when a side is pressed).
            try
            {
                Vector3[] corners = new Vector3[4];
                filterDimLeftImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tl = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tl[i] = corners[i];
                filterDimRightImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tr = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tr[i] = corners[i];
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} DIMRECT left=({1:F0},{2:F0})-({3:F0},{4:F0}) right=({5:F0},{6:F0})-({7:F0},{8:F0}) screen={9}x{10}",
                    LogPrefix, tl[0].x, tl[0].y, tl[2].x, tl[2].y,
                    tr[0].x, tr[0].y, tr[2].x, tr[2].y, Screen.width, Screen.height));
            }
            catch { }

            // Glare flash inside the ocular only.
            GameObject glareGo = new GameObject("Glare", typeof(Image));
            glareGo.transform.SetParent(pipGo.transform, false);
            glareImage = glareGo.GetComponent<Image>();
            glareImage.color = new Color(1f, 1f, 1f, 0f);
            glareImage.raycastTarget = false;
            Stretch(glareImage.rectTransform);

            // Soft radial edge blending the ocular rim into the veil.
            GameObject ringGo = new GameObject("EdgeRing", typeof(Image));
            ringGo.transform.SetParent(pipGo.transform, false);
            Image ring = ringGo.GetComponent<Image>();
            ringTexture = SextantSprites.CreateRingTexture();
            ring.sprite = SextantSprites.CreateRingSprite(ringTexture);
            ring.raycastTarget = false;
            Stretch(ring.rectTransform);

            // Reading column hugging the circle's left edge (§13.3).
            GameObject columnGo = new GameObject("ReadingColumn");
            columnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform column = columnGo.AddComponent<RectTransform>();
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(1f, 0.5f);
            column.sizeDelta = new Vector2(ColumnWidthUnits, 180f);
            column.anchoredPosition = new Vector2(-OcularSizeUnits * 0.5f - ColumnGapUnits, 0f);

            angleText = MakeLabel("AngleText", 22, TextAnchor.MiddleRight, Color.white);
            SetChild(column, angleText, 160f);
            nameText = MakeLabel("NameText", 16, TextAnchor.MiddleRight, new Color(0.75f, 0.9f, 1f));
            SetChild(column, nameText, 116f);
            painText = MakeLabel("PainText", 14, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.35f));
            painText.text = PainMessage;
            painText.enabled = false;
            // Pain warning is long; a wide centered line above the hint
            // fits it fully (the narrow left column used to clip the left
            // edge off-screen with Overflow). Must sit ABOVE the button row
            // (button tops end at y=193 from the bottom edge), so y=215.
            {
                RectTransform pr = painText.rectTransform;
                pr.anchorMin = new Vector2(0.5f, 0f);
                pr.anchorMax = new Vector2(0.5f, 0f);
                pr.pivot = new Vector2(0.5f, 0f);
                pr.sizeDelta = new Vector2(1500f, 40f);
                pr.anchoredPosition = new Vector2(0f, HintYUnits + 155f);
            }
            filterText = MakeLabel("FilterText", 14, TextAnchor.MiddleRight, new Color(0.85f, 0.85f, 0.85f));
            SetChild(column, filterText, 36f);

            // Sun verification readout hugging the circle's right edge:
            // own-ephemeris altitude (reference for the arc at alignment).
            GameObject sunColumnGo = new GameObject("SunInfoColumn");
            sunColumnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform sunColumn = sunColumnGo.AddComponent<RectTransform>();
            sunColumn.anchorMin = new Vector2(0.5f, 0.5f);
            sunColumn.anchorMax = new Vector2(0.5f, 0.5f);
            sunColumn.pivot = new Vector2(0f, 0.5f);
            sunColumn.sizeDelta = new Vector2(SunColumnWidthUnits, 180f);
            sunColumn.anchoredPosition = new Vector2(OcularSizeUnits * 0.5f + ColumnGapUnits, 0f);

            sunAltText = MakeLabel("SunAltText", 14, TextAnchor.MiddleLeft, new Color(0.75f, 0.9f, 1f));
            SetChild(sunColumn, sunAltText, 160f);

            // Small-font body altitudes: Moon + nav stars above the horizon
            // (updated on a 1 s cadence, see UpdateBodiesReadout).
            bodiesAltText = MakeLabel("BodiesAltText", 11, TextAnchor.UpperLeft, new Color(0.62f, 0.70f, 0.78f));
            SetChild(sunColumn, bodiesAltText, 130f);

            // Running-fix worksheet column (F-Tafel card): second column left
            // of the ocular; on narrow aspects (4:3, side zone < ~740 units)
            // it stacks below the reading column instead.
            GameObject fixColumnGo = new GameObject("FixColumn");
            fixColumnGo.transform.SetParent(canvasRoot.transform, false);
            RectTransform fixColumn = fixColumnGo.AddComponent<RectTransform>();
            float refWidthUnits = 1080f * Screen.width / Mathf.Max(1, Screen.height);
            float sideUnits = (refWidthUnits - OcularSizeUnits) * 0.5f;
            bool fixStacked = sideUnits <
                ColumnWidthUnits + FixColumnWidthUnits + 3f * ColumnGapUnits;
            fixColumn.anchorMin = new Vector2(0.5f, 0.5f);
            fixColumn.anchorMax = new Vector2(0.5f, 0.5f);
            fixColumn.sizeDelta = new Vector2(FixColumnWidthUnits, FixCardHeightUnits);
            if (fixStacked)
            {
                fixColumn.pivot = new Vector2(1f, 1f);
                fixColumn.anchoredPosition = new Vector2(
                    -OcularSizeUnits * 0.5f - ColumnGapUnits, -110f);
            }
            else
            {
                fixColumn.pivot = new Vector2(1f, 0.5f);
                fixColumn.anchoredPosition = new Vector2(
                    -OcularSizeUnits * 0.5f - ColumnGapUnits - ColumnWidthUnits - ColumnGapUnits, 0f);
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} FIXLAYOUT refW={1:F0} side={2:F0} stacked={3}",
                LogPrefix, refWidthUnits, sideUnits, fixStacked));

            fixCardText = MakeLabel("FixCardText", FixCardFontSize,
                TextAnchor.UpperLeft, new Color(0.82f, 0.88f, 0.82f));
            fixCardText.horizontalOverflow = HorizontalWrapMode.Wrap;
            fixCardText.text = "";
            fixCardText.rectTransform.SetParent(fixColumn, false);
            RectTransform fixCardRect = fixCardText.rectTransform;
            fixCardRect.anchorMin = Vector2.zero;
            fixCardRect.anchorMax = Vector2.one;
            fixCardRect.offsetMin = Vector2.zero;
            fixCardRect.offsetMax = Vector2.zero;

            // Bottom hint line.
            Text hint = MakeLabel("HintText", 13, TextAnchor.MiddleCenter,
                new Color(0.75f, 0.75f, 0.75f));
            hint.text = HintText;
            hint.transform.SetParent(canvasRoot.transform, false);
            RectTransform hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0.5f, 0f);
            hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1100f, 40f);
            hr.anchoredPosition = new Vector2(0f, HintYUnits);

            // No on-screen control buttons: zoom is +/- keys, arc reset is R
            // (see HintText). Mouse stays free for aiming.
            canvasRoot.SetActive(false);
            Debug.Log(LogPrefix + " overlay built");
        }

        private Text MakeLabel(string labelName, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(labelName, typeof(Text));
            go.transform.SetParent(canvasRoot.transform, false);
            Text t = go.GetComponent<Text>();
            t.font = GetSextantOsFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        /// <summary>
        /// OS Arial for legacy UI.Text: TMP dynamic font assets did not pull
        /// Cyrillic glyphs from the OS font (in-game 2026-08-26, labels stayed
        /// boxes), while legacy Text renders them reliably. Static cache.
        /// </summary>
        private static Font GetSextantOsFont()
        {
            if (sextantOsFont == null)
            {
                sextantOsFont = Font.CreateDynamicFontFromOSFont("Arial", 48);
                if (sextantOsFont == null)
                    Debug.LogWarning(LogPrefix + " OS Arial unavailable - labels will be blank");
            }
            return sextantOsFont;
        }

        private static void SetChild(RectTransform column, Text label, float y)
        {
            RectTransform r = label.rectTransform;
            r.SetParent(column, false);
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.offsetMin = new Vector2(0f, r.offsetMin.y);
            r.offsetMax = new Vector2(0f, r.offsetMax.y);
            r.sizeDelta = new Vector2(0f, label.fontSize * 1.8f);
            r.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>
        /// Loads the opaque view material from the mod's AssetBundle
        /// (RadioNavOpaqueView, shader RadioNav/UIOpaque). Returns null when
        /// unavailable — the views then keep the stock UI shader.
        /// </summary>
        private Material ResolveOpaqueViewMaterial()
        {
            if (opaqueViewWarned)
                return opaqueViewMaterial;
            if (resourceManager == null)
            {
                opaqueViewWarned = true;
                Debug.LogWarning(LogPrefix +
                    " resourceManager unavailable - ocular views keep the stock UI shader (alpha darkening possible)");
                return null;
            }

            Resource<Material> material =
                resourceManager.RetrieveAsset<Material>("RadioNavOpaqueView", this, false);
            if (material.Valid && material.Asset != null)
                return material.Asset;

            opaqueViewWarned = true;
            Debug.LogWarning(LogPrefix +
                " 'RadioNavOpaqueView' not found in bundle - ocular views keep the stock UI shader (alpha darkening possible)");
            return null;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void AnchorCenter(RectTransform r, float w, float h)
        {
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
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
