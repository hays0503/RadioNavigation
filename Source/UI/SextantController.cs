using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
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

        // §2 behaviour constants.
        private const float RateDegPerSec = 2f;
        private const float ShiftMultiplier = 5f;
        private const bool VectorLogEnabled = true;
        private const float VectorLogHoldIntervalSeconds = 0.5f;
        private const float VectorRotateMinDeltaDeg = 0.5f;
        private const float FovDivisor = 3f;
        private const float IdentifyConeDeg = 0.5f;
        private const float IdentifyHoldSeconds = 5f;
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
            "1\u20133: \u0444\u0438\u043B\u044C\u0442\u0440\u044B \u00B7 Z: \u0432\u044B\u0445\u043E\u0434";

        /// <summary>True while the overlay is up; ConstellationMesh gates Z on it (§7.4).</summary>
        public static bool IsOpen { get; private set; }

        internal double arcAngle;
        private int filterCount;
        private float holdTimer;
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

        // 1:1 mirror of the main camera's rendering stack (periscope camera
        // pattern): effect components are copied from the FPP camera onto the
        // PIP cameras so the SRP renders them with the same fog/post look.
        private Component pipSsmsFog;
        private Component pipSsms;
        private Component pipPpBehaviour;
        private Component sourceSsmsFog;
        private Component sourceSsms;
        private Component sourcePpBehaviour;
        private readonly System.Collections.Generic.Dictionary<Type, MemberInfo[]> mirrorMemberCache =
            new System.Collections.Generic.Dictionary<Type, MemberInfo[]>();

        private GameObject canvasRoot;
        private RectTransform pipRect;
        private Image filterDimImage;
        private Image glareImage;
        private Text angleText;
        private Text nameText;
        private Text painText;
        private Text filterText;
        private Texture2D ringTexture;
        private Texture2D circleMaskTexture;
        private static Font sextantOsFont;

        private readonly IPlayerPositionProvider positionProvider = new GamePlayerPositionProvider();

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
                UpdatePipPose();
                UpdateVectorRotateLog();
                RenderCheck(Time.unscaledDeltaTime);
                UpdateGlare(Time.unscaledDeltaTime);
                UpdateStarId(Time.unscaledDeltaTime);
                UpdateReadout(false);
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
            if (!IsLiveViewMode())
            {
                Debug.Log(LogPrefix + " Z ignored outside live view (mode=" + CurrentModeName() + ")");
                return;
            }
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
            {
                Debug.Log(LogPrefix + " Z ignored - no rendering camera");
                return;
            }
            if (gameInput == null && InjectionFramework.Instance.IsNotNull())
                gameInput = InjectionFramework.Instance.GetInstance<GameInput>();
            if (gameInput == null)
            {
                Debug.LogWarning(LogPrefix + " GameInput not available - cannot open");
                return;
            }

            EnsureOverlay();
            EnsurePipCameras();

            arcAngle = 0.0;
            filterCount = 0;
            holdTimer = 0f;
            starIdTimer = 0f;
            lastFoundName = null;
            identifiedName = "";
            pain = false;
            lastFormattedArc = double.MinValue;
            sunDriftLogTimer = 0f; // force the first SUNDRIFT line on open
            renderCheckDone = false;
            renderCheckTimer = 1.0f;

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
                viewCamera != null ? viewCamera.fieldOfView / FovDivisor : 0f));
            UpdatePipPose();
            LogVectorState("open");
            IsOpen = true;

            UpdateFilterLabel();
            UpdateFilterDim();
            if (painText != null) painText.enabled = false;
            UpdateReadout(true);
            Debug.Log(LogPrefix + " opened (two-camera split ocular)");
        }

        private void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            if (gameInput != null)
                gameInput.RemoveBlockade(this);
            DetachWaterCameras();
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
                return;

            bool pressed = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
            if (VectorLogEnabled)
            {
                vectorLogHoldTimer -= dt;
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
                renderCheckTex = new Texture2D(1, 1, TextureFormat.RGB24, false);
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
                    "{0} RENDERCHECK {1} left(t/m/b)=#{2}/#{3}/#{4} right(t/m/b)=#{5}/#{6}/#{7}",
                    LogPrefix, tag,
                    ColorUtility.ToHtmlStringRGB(lt), ColorUtility.ToHtmlStringRGB(lm),
                    ColorUtility.ToHtmlStringRGB(lb), ColorUtility.ToHtmlStringRGB(rtTop),
                    ColorUtility.ToHtmlStringRGB(rm), ColorUtility.ToHtmlStringRGB(rb)));
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
            if (viewCamera == null && !CameraResolver.TryResolve(out viewCamera))
                viewCamera = FindObjectOfType<Camera>();
            if (viewCamera != null)
                lastVectorLogYaw = viewCamera.transform.eulerAngles.y;

            Vector3 lfwd = pipLeftCamera != null ? pipLeftCamera.transform.forward : Vector3.zero;
            Vector3 lup = pipLeftCamera != null ? pipLeftCamera.transform.up : Vector3.zero;
            Vector3 lrgt = pipLeftCamera != null ? pipLeftCamera.transform.right : Vector3.zero;
            Vector3 lpos = pipLeftCamera != null ? pipLeftCamera.transform.position : Vector3.zero;
            Vector3 leul = pipLeftCamera != null ? pipLeftCamera.transform.eulerAngles : Vector3.zero;
            Vector3 rfwd = pipRightCamera != null ? pipRightCamera.transform.forward : Vector3.zero;
            Vector3 rup = pipRightCamera != null ? pipRightCamera.transform.up : Vector3.zero;
            Vector3 rrgt = pipRightCamera != null ? pipRightCamera.transform.right : Vector3.zero;
            Vector3 rpos = pipRightCamera != null ? pipRightCamera.transform.position : Vector3.zero;
            Vector3 reul = pipRightCamera != null ? pipRightCamera.transform.eulerAngles : Vector3.zero;
            Vector3 vfwd = viewCamera != null ? viewCamera.transform.forward : Vector3.zero;
            Vector3 veul = viewCamera != null ? viewCamera.transform.eulerAngles : Vector3.zero;
            Vector3 vpos = viewCamera != null ? viewCamera.transform.position : Vector3.zero;
            Vector3 sunDir = Vector3.zero;
            float sunAlt = 0f, sunAz = 0f;
            bool own = TryGetOwnSun(out sunDir, out sunAlt, out sunAz);
            Vector3 gameDir = Vector3.zero;
            float gameAlt = 0f;
            bool game = TryGetGameSun(out gameDir, out gameAlt);
            double q = SextantArc.Quantize(arcAngle);
            float angLR = pipLeftCamera != null && pipRightCamera != null
                ? Vector3.Angle(lfwd, rfwd) : 0f;
            float dotLR = pipLeftCamera != null && pipRightCamera != null
                ? Vector3.Dot(lfwd, rfwd) : 0f;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECL {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(lpos), V3(leul), V3(lfwd), V3(lup), V3(lrgt)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECR {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(rpos), V3(reul), V3(rfwd), V3(rup), V3(rrgt)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECV {1} pos={2} eul={3} fwd={4}",
                LogPrefix, trigger, V3(vpos), V3(veul), V3(vfwd)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECD {1} arc={2:F6} quant={3:F6} fmt={4} angLR={5:F3} dotLR={6:F6}",
                LogPrefix, trigger, arcAngle, q, SextantArc.Format(q), angLR, dotLR));
            if (own || game)
            {
                float angSun = own ? Vector3.Angle(rfwd, sunDir) : -1f;
                float sep = own && game ? Vector3.Angle(sunDir, gameDir) : -1f;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} VECSUN {1} own{2} alt={3:F2} az={4:F1} game{5} galt={6:F2} angSunR={7:F3} sep={8:F3}",
                    LogPrefix, trigger, own ? V3(sunDir) : " n/a", sunAlt, sunAz,
                    game ? V3(gameDir) : " n/a", gameAlt, angSun, sep));
            }
            if (own && pipRightCamera != null)
            {
                // Sun projected into the right camera's viewport: proves the
                // rendered picture matches the astro math (center = 0.5,0.5).
                Vector3 sunNdc = pipRightCamera.WorldToViewportPoint(pipRightCamera.transform.position + sunDir * 1000f);
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
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetFilters(1);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetFilters(2);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetFilters(3);
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) SetFilters(0);
        }

        private void SetFilters(int count)
        {
            filterCount = count;
            UpdateFilterLabel();
            UpdateFilterDim();
        }

        private void UpdateFilterDim()
        {
            if (!GlareEnabled)
                return;
            if (filterDimImage == null)
                return;
            Color c = filterDimImage.color;
            c.a = 0.15f * filterCount;
            filterDimImage.color = c;
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

            pipLeftCamera = ConfigurePipCamera("RadioNavSextantLeftCamera", leftRt);
            pipRightCamera = ConfigurePipCamera("RadioNavSextantRightCamera", rightRt);

            if (leftViewImage != null) leftViewImage.texture = leftRt;
            if (rightViewImage != null) rightViewImage.texture = rightRt;

            Debug.Log(LogPrefix + " PIP cameras created, RT=" + RtWidth + "x" + RtHeight);
        }

        private Camera ConfigurePipCamera(string name, RenderTexture target)
        {
            GameObject go = new GameObject(name);
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(viewCamera);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = target;
            cam.fieldOfView = viewCamera.fieldOfView / FovDivisor;
            cam.enabled = false;

            // The game's render pipeline renders water only for cameras
            // registered on the main camera's AdditionalCameraData
            // (periscope pattern, Periscope.cs:581).
            UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd =
                go.AddComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            pipAdd.RenderScene = true;
            SetupRenderPasses(pipAdd,
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
            AddMirroredEffectComponents(go);

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
                        CopyMembers(mainWater, waterCam, GetMirrorMembers(mainWater.GetType()));
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
            sourcePpBehaviour = FindComponent(viewCamera, "PostProcessingBehaviour");
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

        private void AddMirroredEffectComponents(GameObject go)
        {
            ResolveMirrorSources();
            if (viewCamera == null)
                return;

            // Preserve the exact component order of the source camera so the
            // SRP/effect chain matches the reference render 1:1.
            MonoBehaviour[] comps = viewCamera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null)
                    continue;
                Type t = comps[i].GetType();
                string name = t.Name;
                if (name != "PostProcessLayer" && name != "PostProcessingBehaviour" &&
                    name != "SSMSGlobalFog" && name != "SSMS")
                    continue;

                Component source = comps[i];
                Component dst = go.AddComponent(t);
                MemberInfo[] members = GetMirrorMembers(t);
                CopyMembers(source, dst, members);
                if (name == "PostProcessLayer")
                    CopyPrivatePostLayerFields(source, dst);
                else if (name == "SSMSGlobalFog")
                    pipSsmsFog = dst;
                else if (name == "SSMS")
                    pipSsms = dst;
                else if (name == "PostProcessingBehaviour")
                    pipPpBehaviour = dst;
            }

            LogPipStack(go);
        }

        private static MemberInfo[] GetMirrorMembers(Type t)
        {
            var list = new System.Collections.Generic.List<MemberInfo>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsStatic)
                    continue;
                list.Add(f);
            }
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0)
                    continue;
                list.Add(p);
            }
            return list.ToArray();
        }

        private MemberInfo[] GetMirrorMembersCached(Type t)
        {
            MemberInfo[] cached;
            if (!mirrorMemberCache.TryGetValue(t, out cached))
            {
                cached = GetMirrorMembers(t);
                mirrorMemberCache[t] = cached;
            }
            return cached;
        }

        private static void CopyMembers(Component from, Component to, MemberInfo[] members)
        {
            if (members == null)
                return;
            for (int i = 0; i < members.Length; i++)
            {
                try
                {
                    FieldInfo f = members[i] as FieldInfo;
                    if (f != null)
                        f.SetValue(to, f.GetValue(from));
                    else
                        ((PropertyInfo)members[i]).SetValue(to,
                            ((PropertyInfo)members[i]).GetValue(from, null), null);
                }
                catch { }
            }
        }

        private static void CopyPrivatePostLayerFields(Component from, Component to)
        {
            Type t = from.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.Name == "volumeLayer" || f.Name == "m_Resources")
                {
                    try { f.SetValue(to, f.GetValue(from)); } catch { }
                }
            }
            try
            {
                PostProcessResources[] allResources = Resources.FindObjectsOfTypeAll<PostProcessResources>();
                PostProcessLayer layer = to as PostProcessLayer;
                if (allResources != null && allResources.Length > 0 && layer != null)
                    layer.Init(allResources[0]);
            }
            catch { }
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
            if (pipSsmsFog != null && sourceSsmsFog != null)
                CopyMembers(sourceSsmsFog, pipSsmsFog, GetMirrorMembersCached(sourceSsmsFog.GetType()));
            if (pipSsms != null && sourceSsms != null)
                CopyMembers(sourceSsms, pipSsms, GetMirrorMembersCached(sourceSsms.GetType()));
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
            cam.fieldOfView = source.fieldOfView / FovDivisor;
            cam.cullingMask = source.cullingMask;
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
        // v1.7: glare mechanic temporarily disabled for the white-ocular
        // diagnosis. Flip to true to restore filters + pain + white flash.
        private const bool GlareEnabled = false;

        private void UpdateGlare(float dt)
        {
            if (!GlareEnabled)
            {
                ApplyGlare(0f);
                if (filterDimImage != null)
                {
                    Color c = filterDimImage.color;
                    c.a = 0f;
                    filterDimImage.color = c;
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
                g = (float)SextantGlare.Glare(angle, sunAlt, filterCount, pipRightCamera.fieldOfView);
            }
            ApplyGlare(g);
            LogSunDrift(dt);
        }

        private void ApplyGlare(float g)
        {
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
        /// Own Meeus ephemeris: sun RA/Dec → the same ENU→world mapping the
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
            double jd = AstroMath.ToJulianDay(utc);
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
            // with the same (e→x, n→z) mapping the star sky proved in-game.
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
            Vector3 sunDir;
            float sunAlt;
            float sunAz;
            Vector3 pipFwd = GetPipForward();
            if (pipFwd.sqrMagnitude < 0.01f)
            {
                bodyAltDeg = -1f;
                return null;
            }
            if (TryGetOwnSun(out sunDir, out sunAlt, out sunAz)
                && Vector3.Angle(pipFwd, sunDir) <= IdentifyConeDeg)
            {
                bodyAltDeg = sunAlt;
                return SunName;
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
            filterText.text = filterCount > 0
                ? "\u0424\u0418\u041B\u042C\u0422\u0420 " + filterCount
                : "";
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

            // Ocular: square container clipped to a circle (in-game finding
            // 2026-08-26: unclipped corners painted opaque black over the live
            // horizon). Mask clips both RT views, glare and ring to the circle;
            // outside it the veil shows through.
            GameObject pipGo = new GameObject("Ocular", typeof(Image), typeof(Mask));
            pipGo.transform.SetParent(canvasRoot.transform, false);
            pipRect = pipGo.GetComponent<RectTransform>();
            AnchorCenter(pipRect, OcularSizeUnits, OcularSizeUnits);
            Image maskGraphic = pipGo.GetComponent<Image>();
            maskGraphic.sprite = CreateCircleMaskSprite();
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

            // Vertical split line between the halves.
            GameObject splitGo = new GameObject("VerticalSplit", typeof(Image));
            splitGo.transform.SetParent(pipGo.transform, false);
            Image splitImage = splitGo.GetComponent<Image>();
            splitImage.color = new Color(1f, 1f, 1f, 0.35f);
            splitImage.raycastTarget = false;
            AnchorCenter(splitImage.rectTransform, 2f, OcularSizeUnits);

            // Shade filters dim the whole scope view (v1.5): the PIP camera
            // renders without the game's post stack, so the daytime sky clips
            // to white; filters are the authentic way to cut that down.
            GameObject dimGo = new GameObject("FilterDim", typeof(Image));
            dimGo.transform.SetParent(pipGo.transform, false);
            filterDimImage = dimGo.GetComponent<Image>();
            filterDimImage.color = new Color(0f, 0f, 0f, 0f);
            filterDimImage.raycastTarget = false;
            Stretch(filterDimImage.rectTransform);

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
            ring.sprite = CreateRingSprite();
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

            angleText = MakeLabel("AngleText", 44, TextAnchor.MiddleRight, Color.white);
            SetChild(column, angleText, 160f);
            nameText = MakeLabel("NameText", 32, TextAnchor.MiddleRight, new Color(0.75f, 0.9f, 1f));
            SetChild(column, nameText, 116f);
            painText = MakeLabel("PainText", 32, TextAnchor.MiddleRight, new Color(1f, 0.45f, 0.35f));
            painText.text = PainMessage;
            painText.enabled = false;
            SetChild(column, painText, 72f);
            filterText = MakeLabel("FilterText", 28, TextAnchor.MiddleRight, new Color(0.85f, 0.85f, 0.85f));
            SetChild(column, filterText, 36f);

            // Bottom hint line.
            Text hint = MakeLabel("HintText", 26, TextAnchor.MiddleCenter,
                new Color(0.75f, 0.75f, 0.75f));
            hint.text = HintText;
            hint.transform.SetParent(canvasRoot.transform, false);
            RectTransform hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0.5f, 0f);
            hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1100f, 40f);
            hr.anchoredPosition = new Vector2(0f, HintYUnits);

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

        private Sprite CreateRingSprite()
        {
            const int size = 256;
            ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            ringTexture.name = "RadioNavSextantRing";
            ringTexture.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r <= 0.78f) a = 0f;
                    else if (r >= 1f) a = 1f;
                    else
                    {
                        float t = (r - 0.78f) / 0.22f;
                        a = t * t * (3f - 2f * t); // smoothstep
                    }
                    ringTexture.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
            }
            ringTexture.Apply();
            return Sprite.Create(ringTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// The game's SRP renders a camera through the passes configured on
        /// its AdditionalCameraData; a freshly added component has none, so
        /// the RT stays blank (in-game finding 2026-08-26). Deep-copy the
        /// main camera's pass list when available - those are the exact
        /// culling groups the game renders the scene with; otherwise build
        /// a single enabled pass with the camera's culling mask.
        /// Backing fields are private: "passes" (CameraPass[]) and
        /// "useCustomPasses" (bool) - the Passes property is get-only.
        /// </summary>
        private static void SetupRenderPasses(UBOAT.Game.Scene.Rendering.AdditionalCameraData data,
            UBOAT.Game.Scene.Rendering.AdditionalCameraData mainData, int cullingMask)
        {
            Type type = data.GetType();
            Type passType = type.GetNestedType("CameraPass",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (passType == null)
            {
                Debug.LogWarning(LogPrefix + " CameraPass type not found - PIP may stay blank");
                return;
            }

            try
            {
                // Passes is get-only (CanWrite=false) - find backing field by type
                FieldInfo passesField = null;
                FieldInfo useCustomField = null;
                foreach (FieldInfo f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType.IsArray && f.FieldType.GetElementType() == passType)
                        passesField = f;
                    else if (f.FieldType == typeof(bool) && f.Name.IndexOf("useCustom", StringComparison.OrdinalIgnoreCase) >= 0)
                        useCustomField = f;
                }
                if (passesField == null)
                    passesField = type.GetField("passes", BindingFlags.NonPublic | BindingFlags.Instance);
                if (useCustomField == null)
                    useCustomField = type.GetField("useCustomPasses", BindingFlags.NonPublic | BindingFlags.Instance);

                Array source = null;
                if (mainData != null)
                {
                    PropertyInfo passesProp = type.GetProperty("Passes");
                    if (passesProp != null)
                        source = passesProp.GetValue(mainData) as Array;
                    // fallback: read backing field directly if property missing
                    if (source == null && passesField != null)
                        source = passesField.GetValue(mainData) as Array;
                }

                int count = source != null && source.Length > 0 ? source.Length : 1;
                Array passes = Array.CreateInstance(passType, count);
                if (source != null && source.Length > 0)
                {
                    // CameraPass(int cullingMask) is the only public ctor;
                    // Enabled is read-write, ScreenSpaceReflections is
                    // get-only and stays at its default.
                    PropertyInfo cullingProp = passType.GetProperty("CullingMask");
                    PropertyInfo enabledProp = passType.GetProperty("Enabled");
                    for (int i = 0; i < source.Length; i++)
                    {
                        object src = source.GetValue(i);
                        int mask = cullingMask;
                        if (cullingProp != null)
                        {
                            object maskBoxed = cullingProp.GetValue(src);
                            if (maskBoxed is int)
                                mask = (int)maskBoxed;
                        }
                        object copy = Activator.CreateInstance(passType, mask);
                        if (enabledProp != null)
                        {
                            object enabled = enabledProp.GetValue(src);
                            if (enabled is bool)
                                enabledProp.SetValue(copy, (bool)enabled);
                        }
                        passes.SetValue(copy, i);
                    }
                }
                else
                {
                    object pass = Activator.CreateInstance(passType, cullingMask);
                    passType.GetProperty("Enabled").SetValue(pass, true);
                    passes.SetValue(pass, 0);
                }

                if (passesField != null)
                    passesField.SetValue(data, passes);
                else
                    type.GetProperty("Passes").SetValue(data, passes);
                if (useCustomField != null)
                    useCustomField.SetValue(data, true);
                else
                {
                    PropertyInfo useCustomProp = type.GetProperty("UseCustomPasses");
                    if (useCustomProp != null && useCustomProp.CanWrite)
                        useCustomProp.SetValue(data, true);
                }
                data.RenderScene = true;
                Debug.Log(LogPrefix + " PIP render passes configured: " + count + " pass(es), mask=" + cullingMask);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " render pass setup failed: " + e.Message + " " + e.StackTrace);
            }
        }

        /// <summary>Hard circle sprite for the ocular Mask (smooth AA at the rim).</summary>
        private Sprite CreateCircleMaskSprite()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantCircleMask";
            tex.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r <= 0.96f) a = 1f;
                    else if (r >= 1f) a = 0f;
                    else a = 1f - (r - 0.96f) / 0.04f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            circleMaskTexture = tex;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
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
