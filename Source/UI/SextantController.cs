using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using DWS.Common.InjectionFramework;
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
    /// central PIP camera pitched to the arc angle (world pitch, 0 = horizon),
    /// shade filters 1/2/3, gaussian glare with pain hysteresis, star/sun
    /// identification after a 5 s hold.
    /// Layout is resolution-independent: own overlay canvas + CanvasScaler
    /// (1920x1080 reference, match height), SEXTANT_PLAN §13.
    /// </summary>
    public sealed class SextantController : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        // §13.3 layout constants (1080-space units).
        private const float OcularSizeUnits = 600f;
        private const float VeilAlpha = 0.85f;
        private const float ColumnGapUnits = 40f;
        private const float ColumnWidthUnits = 320f;
        private const float HintYUnits = 60f;

        // §2 behaviour constants.
        private const float RateDegPerSec = 2f;
        private const float ShiftMultiplier = 5f;
        private const float FovDivisor = 3f;
        private const float IdentifyConeDeg = 0.5f;
        private const float IdentifyHoldSeconds = 5f;
        private const float StarIdIntervalSeconds = 0.2f;
        private const float PainOnGlare = 0.6f;
        private const float PainOffGlare = 0.5f;
        private const int RtSize = 1024;
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

        private double arcAngle;
        private int filterCount;
        private float holdTimer;
        private float starIdTimer;
        private string lastFoundName;
        private string identifiedName = "";
        private bool pain;
        private double lastFormattedArc = double.MinValue;
        private float sunDriftLogTimer;

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

        // Water + post stack wiring for the PIP camera (SEXTANT_PLAN v1.6):
        // the game's render pipeline renders water only for cameras listed on
        // the main camera's UBOAT.Game.Scene.Rendering.AdditionalCameraData
        // (periscope pattern), and without a PostProcessLayer the HDR sky/sea
        // clip to pure white.
        private UBOAT.Game.Scene.Rendering.AdditionalCameraData registeredWaterData;
        private bool waterAttachWarned;

        // RT debug (user request: pixel groups + screenshot of ocular)
        private const bool DebugRtEnabled = true;
        private float debugRtLogTimer;
        private float debugRtPngTimer;
        private Texture2D debugRtTex;

        private RenderTexture rt;
        private Camera pipCamera;
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
                UpdateGlare(Time.unscaledDeltaTime);
                UpdateStarId(Time.unscaledDeltaTime);
                UpdateReadout(false);
                DebugRt(Time.unscaledDeltaTime);
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
            DetachWaterCamera();
            IsOpen = false;

            if (canvasRoot != null) Destroy(canvasRoot);
            if (pipCamera != null) Destroy(pipCamera.gameObject);
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
            if (ringTexture != null) Destroy(ringTexture);
            if (circleMaskTexture != null) Destroy(circleMaskTexture);
            if (debugRtTex != null) Destroy(debugRtTex);
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
            EnsurePipCamera();

            arcAngle = 0.0;
            filterCount = 0;
            holdTimer = 0f;
            starIdTimer = 0f;
            lastFoundName = null;
            identifiedName = "";
            pain = false;
            lastFormattedArc = double.MinValue;
            sunDriftLogTimer = 0f; // force the first SUNDRIFT line on open

            gameInput.AddBlockade(this);
            canvasRoot.SetActive(true);
            AttachWaterCamera();
            // Attached cameras are rendered via MainCamera's pass list;
            // keep enabled=false like periscope's small view. Standalone
            // cameras (not attached) need enabled=true.
            pipCamera.enabled = registeredWaterData == null;
            IsOpen = true;

            UpdatePipPose();
            UpdateFilterLabel();
            UpdateFilterDim();
            if (painText != null) painText.enabled = false;
            UpdateReadout(true);
            Debug.Log(LogPrefix + " opened");
        }

        private void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            if (gameInput != null)
                gameInput.RemoveBlockade(this);
            DetachWaterCamera();
            if (pipCamera != null)
                pipCamera.enabled = false;
            if (canvasRoot != null)
                canvasRoot.SetActive(false);
            Debug.Log(LogPrefix + " closed");
        }

        // Water renders itself only for cameras listed on the main camera's
        // AdditionalCameraData (periscope pattern, Periscope.cs:581).
        // Must be MainCamera, not the transient viewCamera (device probe).
        private void AttachWaterCamera()
        {
            if (pipCamera == null)
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
            data.AttachedCameras.Remove(pipCamera);
            data.AttachedCameras.Add(pipCamera);
        }

        private void DetachWaterCamera()
        {
            if (registeredWaterData != null && pipCamera != null)
                registeredWaterData.AttachedCameras.Remove(pipCamera);
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

            bool fast = ShiftHeld();
            float rate = RateDegPerSec * (fast ? ShiftMultiplier : 1f);
            arcAngle = SextantArc.Quantize(arcAngle + dir * rate * dt);
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

        // ---- PIP camera ------------------------------------------------------

        private void EnsurePipCamera()
        {
            if (pipCamera != null)
                return;

            rt = new RenderTexture(RtSize, RtSize, 24);
            rt.name = "RadioNavSextantRT";

            GameObject go = new GameObject("RadioNavSextantCamera");
            pipCamera = go.AddComponent<Camera>();
            pipCamera.CopyFrom(viewCamera);
            pipCamera.clearFlags = CameraClearFlags.Skybox;
            pipCamera.targetTexture = rt;
            pipCamera.fieldOfView = viewCamera.fieldOfView / FovDivisor;
            pipCamera.enabled = false;

            // The game's render pipeline renders water only for cameras
            // registered on the main camera's AdditionalCameraData
            // (periscope pattern, Periscope.cs:581).
            UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd =
                go.AddComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            pipAdd.RenderScene = true;
            SetupRenderPasses(pipAdd,
                viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>(),
                pipCamera.cullingMask);

            // Post stack v2 (game's custom fork): without it the HDR sky/sea
            // clip to pure white (in-game finding 2026-08-26). The fork hides
            // volumeLayer/m_Resources, so both are copied from the main
            // camera's layer via reflection.
            PostProcessLayer mainLayer = viewCamera.GetComponent<PostProcessLayer>();
            if (mainLayer != null)
            {
                PostProcessLayer pipLayer = go.AddComponent<PostProcessLayer>();
                pipLayer.antialiasingMode = PostProcessLayer.Antialiasing.None;

                FieldInfo volumeField = typeof(PostProcessLayer).GetField("volumeLayer",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo resourcesField = typeof(PostProcessLayer).GetField("m_Resources",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (volumeField != null)
                    volumeField.SetValue(pipLayer, volumeField.GetValue(mainLayer));

                PostProcessResources resources = resourcesField != null
                    ? resourcesField.GetValue(mainLayer) as PostProcessResources
                    : null;
                if (resources == null)
                {
                    PostProcessResources[] allResources = Resources.FindObjectsOfTypeAll<PostProcessResources>();
                    resources = allResources != null && allResources.Length > 0 ? allResources[0] : null;
                }
                if (resources != null)
                    pipLayer.Init(resources);
                else
                    Debug.LogWarning(LogPrefix + " PostProcessResources not found - PIP stays untonemapped");
            }
            else
            {
                Debug.LogWarning(LogPrefix + " main camera has no PostProcessLayer - PIP stays untonemapped");
            }

            Debug.Log(LogPrefix + " PIP camera created, RT=" + RtSize + "^2");
        }

        private void UpdatePipPose()
        {
            if (pipCamera == null)
                return;
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
                return;

            Transform mt = viewCamera.transform;
            pipCamera.transform.position = mt.position;
            Vector3 mainEuler = mt.eulerAngles;
            pipCamera.transform.rotation = Quaternion.Euler((float)arcAngle, mainEuler.y, 0f);
            pipCamera.fieldOfView = viewCamera.fieldOfView / FovDivisor;
            pipCamera.cullingMask = viewCamera.cullingMask;
            // Keep the SRP pass mask in sync with the live cullingMask
            try
            {
                var pipAdd = pipCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                if (pipAdd != null)
                {
                    Array passes = pipAdd.Passes as Array;
                    if (passes != null)
                    {
                        foreach (object p in passes)
                        {
                            var prop = p.GetType().GetProperty("CullingMask");
                            if (prop != null && prop.CanWrite)
                                prop.SetValue(p, pipCamera.cullingMask);
                            else
                            {
                                FieldInfo f = p.GetType().GetField("cullingMask", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                                if (f != null) f.SetValue(p, pipCamera.cullingMask);
                            }
                        }
                    }
                }
            }
            catch { }
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
            if (pipCamera != null && TryGetOwnSun(out sunDir, out sunAlt, out sunAz))
            {
                float angle = Vector3.Angle(pipCamera.transform.forward, sunDir);
                g = (float)SextantGlare.Glare(angle, sunAlt, filterCount, pipCamera.fieldOfView);
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

        private void DebugRt(float dt)
        {
            if (!DebugRtEnabled)
                return;
            debugRtLogTimer -= dt;
            debugRtPngTimer -= dt;
            bool doLog = debugRtLogTimer <= 0f;
            bool doPng = debugRtPngTimer <= 0f;
            if (!doLog && !doPng)
                return;
            if (doLog)
                debugRtLogTimer = 2f;
            if (doPng)
                debugRtPngTimer = 5f;

            if (rt == null || pipCamera == null || !rt.IsCreated())
            {
                if (doLog)
                {
                    string rtState = rt == null ? "null" : (!rt.IsCreated() ? "notCreated" : "ok");
                    string camState = pipCamera == null ? "null" : (!pipCamera.isActiveAndEnabled ? "disabled" : "enabled");
                    UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd = pipCamera != null ? pipCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>() : null;
                    int passes = 0;
                    if (pipAdd != null)
                    {
                        try { Array a = pipAdd.Passes as Array; if (a != null) passes = a.Length; } catch { }
                    }
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "{0} RTDBG rt={1} cam={2} passes={3} cullingMask={4} near={5:F3} far={6:F0} pos=({7:F1},{8:F1},{9:F1})",
                        LogPrefix, rtState, camState, passes,
                        pipCamera != null ? pipCamera.cullingMask.ToString() : "n/a",
                        pipCamera != null ? pipCamera.nearClipPlane : 0f,
                        pipCamera != null ? pipCamera.farClipPlane : 0f,
                        pipCamera != null ? pipCamera.transform.position.x : 0f,
                        pipCamera != null ? pipCamera.transform.position.y : 0f,
                        pipCamera != null ? pipCamera.transform.position.z : 0f));
                }
                return;
            }

            // Read RT on main thread after render (previous frame is fine for diagnosis)
            if (debugRtTex == null)
                debugRtTex = new Texture2D(256, 256, TextureFormat.RGB24, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            debugRtTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            debugRtTex.Apply();
            RenderTexture.active = prev;

            if (doLog)
            {
                Color c = debugRtTex.GetPixel(128, 128);
                Color t = debugRtTex.GetPixel(128, 220);
                Color b = debugRtTex.GetPixel(128, 35);
                Color l = debugRtTex.GetPixel(35, 128);
                Color rr = debugRtTex.GetPixel(220, 128);
                // average of center 3x3
                Color avg = Color.black;
                for (int x = 126; x <= 130; x++) for (int y = 126; y <= 130; y++) avg += debugRtTex.GetPixel(x, y);
                avg /= 25f;
                UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd = pipCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                int passes = 0;
                try { Array a = pipAdd.Passes as Array; if (a != null) passes = a.Length; } catch { }
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} RTPIXEL c=#{1} t=#{2} b=#{3} l=#{4} r=#{5} avg=#{6} rt={7}x{8} passes={9} mask={10}",
                    LogPrefix,
                    ColorUtility.ToHtmlStringRGB(c), ColorUtility.ToHtmlStringRGB(t),
                    ColorUtility.ToHtmlStringRGB(b), ColorUtility.ToHtmlStringRGB(l),
                    ColorUtility.ToHtmlStringRGB(rr), ColorUtility.ToHtmlStringRGB(avg),
                    rt.width, rt.height, passes, pipCamera.cullingMask));
            }

            if (doPng)
            {
                try
                {
                    byte[] png = UnityEngine.ImageConversion.EncodeToPNG(debugRtTex);
                    string dir = Path.Combine(Application.persistentDataPath, "RadioNavigation");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, string.Format("sextant_{0:D06}.png", Time.frameCount));
                    File.WriteAllBytes(path, png);
                    // also latest
                    File.WriteAllBytes(Path.Combine(dir, "sextant_last.png"), png);
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "{0} RTPNG saved {1} ({2} bytes)", LogPrefix, path, png.Length));
                }
                catch (Exception e)
                {
                    Debug.LogWarning(LogPrefix + " RTPNG failed: " + e.Message);
                }
            }
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
                lastFoundName = FindNearestBodyName();
                if (lastFoundName != null)
                {
                    holdTimer += StarIdIntervalSeconds;
                    if (holdTimer >= IdentifyHoldSeconds)
                        identifiedName = lastFoundName;
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

        private string FindNearestBodyName()
        {
            if (pipCamera == null)
                return null;

            Vector3 sunDir;
            float sunAlt;
            float sunAz;
            if (TryGetOwnSun(out sunDir, out sunAlt, out sunAz)
                && Vector3.Angle(pipCamera.transform.forward, sunDir) <= IdentifyConeDeg)
                return SunName;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return null;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            Vector3 fwd = pipCamera.transform.forward;
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
            // horizon). Mask clips RT view, glare, ring and tick to the circle;
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

            GameObject pipImageGo = new GameObject("View", typeof(RawImage));
            pipImageGo.transform.SetParent(pipGo.transform, false);
            RawImage pipImage = pipImageGo.GetComponent<RawImage>();
            pipImage.raycastTarget = false;
            pipImage.texture = rt;
            Stretch(pipImage.rectTransform);

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

            // Horizon tick line across the exact diameter.
            GameObject tickGo = new GameObject("TickLine", typeof(Image));
            tickGo.transform.SetParent(pipGo.transform, false);
            Image tick = tickGo.GetComponent<Image>();
            tick.color = new Color(1f, 1f, 1f, 0.8f);
            tick.raycastTarget = false;
            AnchorCenter(tick.rectTransform, OcularSizeUnits, 3f);

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
}
