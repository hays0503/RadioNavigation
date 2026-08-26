using System;
using System.Globalization;
using System.Reflection;
using DWS.Common.InjectionFramework;
using TMPro;
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

        private RenderTexture rt;
        private Camera pipCamera;
        private GameObject canvasRoot;
        private RectTransform pipRect;
        private Image glareImage;
        private TextMeshProUGUI angleText;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI painText;
        private TextMeshProUGUI filterText;
        private Texture2D ringTexture;

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
            IsOpen = false;

            if (canvasRoot != null) Destroy(canvasRoot);
            if (pipCamera != null) Destroy(pipCamera.gameObject);
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
            if (ringTexture != null) Destroy(ringTexture);
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
            pipCamera.enabled = true;
            IsOpen = true;

            UpdatePipPose();
            UpdateFilterLabel();
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
            if (pipCamera != null)
                pipCamera.enabled = false;
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
        }

        // ---- glare -----------------------------------------------------------

        private void UpdateGlare(float dt)
        {
            float g = 0f;
            Vector3 sunDir;
            float sunAlt;
            if (pipCamera != null && TryGetOwnSun(out sunDir, out sunAlt))
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
        private bool TryGetOwnSun(out Vector3 dirWorld, out float altDeg)
        {
            dirWorld = Vector3.zero;
            altDeg = 0f;

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
            if (!TryGetOwnSun(out ownDir, out ownAlt))
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
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} SUNDRIFT sep={1:F3} dalt={2:F3} meeus(alt={3:F2}) game(alt={4:F2})",
                LogPrefix, sepDeg, dAlt, ownAlt, gameAlt));
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
            if (TryGetOwnSun(out sunDir, out sunAlt)
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

            // Ocular: square PIP showing the RT.
            GameObject pipGo = new GameObject("Ocular", typeof(RawImage));
            pipGo.transform.SetParent(canvasRoot.transform, false);
            pipRect = pipGo.GetComponent<RectTransform>();
            AnchorCenter(pipRect, OcularSizeUnits, OcularSizeUnits);
            RawImage pipImage = pipGo.GetComponent<RawImage>();
            pipImage.raycastTarget = false;
            pipImage.texture = rt;

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

            angleText = MakeLabel("AngleText", 44, TextAlignmentOptions.Right, Color.white);
            SetChild(column, angleText, 160f);
            nameText = MakeLabel("NameText", 32, TextAlignmentOptions.Right, new Color(0.75f, 0.9f, 1f));
            SetChild(column, nameText, 116f);
            painText = MakeLabel("PainText", 32, TextAlignmentOptions.Right, new Color(1f, 0.45f, 0.35f));
            painText.text = PainMessage;
            painText.enabled = false;
            SetChild(column, painText, 72f);
            filterText = MakeLabel("FilterText", 28, TextAlignmentOptions.Right, new Color(0.85f, 0.85f, 0.85f));
            SetChild(column, filterText, 36f);

            // Bottom hint line.
            TextMeshProUGUI hint = MakeLabel("HintText", 26, TextAlignmentOptions.Center,
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

        private TextMeshProUGUI MakeLabel(string labelName, int fontSize, TextAlignmentOptions align, Color color)
        {
            GameObject go = new GameObject(labelName, typeof(TextMeshProUGUI));
            go.transform.SetParent(canvasRoot.transform, false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.font = NavMapMarkerBase.GetTooltipFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static void SetChild(RectTransform column, TextMeshProUGUI label, float y)
        {
            RectTransform r = label.rectTransform;
            r.SetParent(column, false);
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.offsetMin = new Vector2(0f, r.offsetMin.y);
            r.offsetMax = new Vector2(0f, r.offsetMax.y);
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
            const int size = 64;
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
    }
}
