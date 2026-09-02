using DWS.Common.InjectionFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Makes the lighthouse lantern visible from 10+ km. Lives on its own
    /// scene-root GameObject ("RadioNavLighthouseGlow_<GroupName>", created
    /// by LighthousePlacer), NOT on the spawned prefab, so it keeps running
    /// when the game LOD-deactivates the model and survives save/load (the
    /// placer re-points it to the respawned container).
    ///
    /// Mechanisms, mirroring the game's own LighthouseLight component:
    /// 1. HDRPLensFlare from the prefab (if present): activated at night and
    ///    driven with Intensity = dot(cameraDir, beamForward)² × 2 — a
    ///    screen-space flare, visible at any distance and in any camera mode
    ///    (FPP included). This is the primary long-range signal.
    /// 2. Screen-space dot on a uGUI overlay Canvas — automatic fallback
    ///    when the prefab has no HDRPLensFlare component (fog can't hide it,
    ///    and it works in every camera mode). Built without UnityEngine.UI
    ///    compile references (the game's Roslyn mod compiler does not
    ///    include them) — the Image type is resolved via reflection.
    /// 3. Spot Light boost — the rotating beam illuminates the surrounding
    ///    water at night.
    /// 4. _RotationOffset material property on the lighthouse-shader
    ///    renderers, kept in sync with the beam rotation so the emissive
    ///    windows sweep together with the light.
    ///
    /// Day/night gating uses the game's own Weather.IsDarkWeather (the same
    /// signal as the game's lighthouse effect), with a sun-position
    /// heuristic as fallback.
    /// </summary>
    public sealed class LighthouseBeamGlow : MonoBehaviour
    {
        private const string SpotLightName = "Spot Light";
        private const string FlareComponentTypeName = "HDRPLensFlare";
        private const string LighthouseShaderName = "Standard (DWS, Lighthouse)";
        private const float MaxLightRangeM = 2500f;
        private const float MinLightIntensity = 2.5f;
        private const float DaySunIntensityThreshold = 0.6f;

        // Unified perceptual model lives in LighthouseVisualModel (pure math,
        // unit-tested). Dot/streak layer constants and layout math live in
        // LighthouseDotLayout (P5.4 LBG-2); pixel sizes come from the model.
        private const float BeamVisibleAngleDeg = 60f;
        // Native flare is replaced by the screen-space dot beyond this range:
        // the dot path obeys the model's exact pixel caps (≤10px / ≤13px).
        private const float FlareMaxDistanceM = 1200f;
        private const float FlareIntensityScale = 2f;
        // User preference: the flare reads too small through the binoculars,
        // especially up close. Base sizes were the far pixel caps from the
        // visual model spec (glow 0.010 ≈ 11px @1080p, streak 0.012×0.006 ≈
        // 13px, star 0.015); +25% on top.
        private const float FlareSizeScale = 1.25f;
        // The game hard-codes the periscope/binocular camera far clip to 11000
        // (Periscope.cs:1249). Lighthouses sit beyond it (12+ km), so the
        // device view culls them entirely. Raise the clip while a device
        // camera is active — same order of magnitude as the game's own
        // FreeFly far clip (26000).
        private const float MinDeviceCameraFarM = 30000f;
        private const int StreakTextureWidth = 128;
        private const int StreakTextureHeight = 16;
        private const int DotTextureSize = 64;
        private const int DotCanvasSortingOrder = 500;
        private const string ImageTypeName = "UnityEngine.UI.Image";
        private const float DiagnosticIntervalSeconds = 2f;

        private static readonly Color DotColor = new Color(1f, 0.94f, 0.84f);
        private static readonly int RotationOffsetId = Shader.PropertyToID("_RotationOffset");
        private static PropertyInfo imageColorProperty;

        [Inject] private static Weather weather = null;

        private static Texture2D dotTexture;
        private static Texture2D streakTexture;
        private static Camera renderingCamera;

        private Transform container;
        private Transform rotator;
        private Vector3 rotatorLocalPosition;
        private Light spotLight;
        private Light sun;
        private Component lensFlare;
        private PropertyInfo flareIntensityProperty;
        private MeshFilter flareMeshFilter;
        private object flareGlowElement;
        private object flareStreakElement;
        private object flareStarElement;
        private MainCamera gameMainCamera;
        private MaterialPropertyBlock rotationBlock;
        private readonly List<Renderer> windowRenderers = new List<Renderer>();
        private Type imageType;
        private Canvas dotCanvas;
        private Component dotImage;
        private Component dotHaloImage;
        private Component dotStreakImage;
        private RectTransform dotRect;
        private RectTransform dotHaloRect;
        private RectTransform dotStreakRect;
        private bool initialized;
        private bool hasFlare;
        private bool isNight;
        private bool dotUiBuilt;
        private bool flareWasActive;
        private bool dotWasActive;
        private float nextDiagnosticTime;
        private float lastIVisual;
        private float lastDotSize;

        /// <summary>
        /// Binds the controller to a spawned lighthouse instance. Safe to
        /// call again after a save/load respawn — the new container and
        /// rotator simply replace the old references.
        /// </summary>
        public void Initialize(Transform container, Vector3 rotatorLocalPosition, Transform rotator, string lighthouseName)
        {
            this.container = container;
            this.rotator = rotator;
            this.rotatorLocalPosition = rotatorLocalPosition;
            FindSun();
            FindSpotLight(rotator);
            FindLensFlare(rotator);
            if (lensFlare.IsNotNull())
            {
                flareMeshFilter = lensFlare.GetComponent<MeshFilter>();
                try
                {
                    var flareType = lensFlare.GetType();
                    var nearStart = flareType.GetField("NearFadeStartDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nearStart != null) nearStart.SetValue(lensFlare, 70f);
                    var nearEnd = flareType.GetField("NearFadeEndDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nearEnd != null) nearEnd.SetValue(lensFlare, 200f);
                    var flaresField = flareType.GetField("Flares", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (flaresField != null)
                    {
                        var flares = flaresField.GetValue(lensFlare) as System.Array;
                        if (flares != null && flares.Length > 2)
                        {
                            flareGlowElement = flares.GetValue(0);
                            flareStreakElement = flares.GetValue(1);
                            flareStarElement = flares.GetValue(2);
                            ApplyFlareElementSizes();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RadioNav][Lighthouse] flare runtime fix failed: " + ex.Message);
                }
            }
            FindWindowRenderers(rotator);
            initialized = true;
            // --- Init heights dump (requested) ---
            try
            {
                Vector3 wp = GetWorldPosition();
                float hBeaconInit = container.IsNotNull() ? container.position.y : float.NaN;
                float hLampInit = rotator.IsNotNull() ? rotator.position.y : float.NaN;
                float hObjInit = transform.position.y;
                float hSpotInit = spotLight.IsNotNull() ? spotLight.transform.position.y : float.NaN;
                float hFarInit = wp.y;
                Debug.Log("[RadioNav][Lighthouse] init heights for '" + lighthouseName + "':" +
                    " beaconY=" + hBeaconInit.ToString("F1") +
                    " lampY=" + hLampInit.ToString("F1") +
                    " objY=" + hObjInit.ToString("F1") +
                    " spotY=" + hSpotInit.ToString("F1") +
                    " farY=" + hFarInit.ToString("F1") +
                    " rotLocal=" + rotatorLocalPosition.ToString("F2") +
                    " containerPos=" + (container.IsNotNull() ? container.position.ToString("F1") : "null"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioNav][Lighthouse] init heights log failed: " + ex.Message);
            }
            Debug.Log("[RadioNav][Lighthouse] glow controller ready for '" + lighthouseName +
                "' (flare=" + (hasFlare ? "yes" : "no, dot fallback") + ", windows=" + windowRenderers.Count + ")");
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }
            ResolveCamera();
            EnsureDeviceCameraFarClip();
            UpdateDayNight();
            Vector3 worldPosition = GetWorldPosition();
            UpdateWindowRotation();
            UpdateSpot(worldPosition);
            UpdateFlare(worldPosition);
            KeepFlareInView();
            UpdateDot(worldPosition);
            if (VerboseDiagnostics)
            {
                LogDiagnostics(worldPosition);
            }
        }

        /// <summary>
        /// PlayerPrefs-gated verbose diagnostics (ADR refactor step): the
        /// 2-second glow diag/screenPt/dot/flare model dumps run only when
        /// RadioNav.LighthouseGlowVerbose is non-zero (default: on —
        /// behavior unchanged).
        /// </summary>
        private static bool VerboseDiagnostics =>
            RadioNavDiagnostics.IsEnabledCached(RadioNavDiagnostics.LighthouseGlowVerboseKey, true);

        private void UpdateDot(Vector3 worldPosition)
        {
            // In the periscope/binocular/UZO view the screen-space dot cannot
            // be positioned: the device camera's projection (zoom +
            // stabilization) is incompatible with WorldToScreenPoint, and the
            // game itself renders its lighthouse light as a world-space flare
            // in that view (LighthouseLight.cs). The flare takes over there.
            if (IsDeviceView())
            {
                SetDotEnabled(false, "device view, flare renders");
                return;
            }

            float distance = renderingCamera.IsNotNull()
                ? (renderingCamera.transform.position - worldPosition).magnitude
                : -1f;

            // Hide the dot only when the native flare is actually drawing and
            // the lighthouse is inside the camera's far clip. Beyond the far
            // clip the world-space flare may be culled, so the screen-space
            // dot takes over — this guarantees the light stays visible in
            // every camera mode (FPP included) at any distance.
            // NOTE: the decision mirrors UpdateFlare's own logic instead of
            // FlareRenderingNow() (renderer.isVisible flickers while
            // KeepFlareInView inflates the bounds), which made the dot jump
            // around as the beam rotated.
            if (IsFlareTakingOver(worldPosition, distance))
            {
                SetDotEnabled(false, "flare rendering, in far clip");
                return;
            }

            if (!IsNightVisible())
            {
                SetDotEnabled(false, "not night or no camera");
                return;
            }

            Vector3 screen = GetExternalViewScreenPoint(worldPosition);
            if (screen.z <= 0f)
            {
                SetDotEnabled(false, DotWillLog(false)
                    ? "behind camera (screen.z=" + screen.z.ToString("F1") + ")"
                    : null);
                return;
            }

            EnsureDotUi();
            if (dotCanvas.IsNull())
            {
                return;
            }

            // B_wide: the real rotating beam is fully bright while it points
            // at the player and fades out past BeamVisibleAngleDeg.
            float beamFactor = GetBeamVisibilityFactor(worldPosition);
            if (beamFactor <= 0.01f)
            {
                SetDotEnabled(false, DotWillLog(false)
                    ? "beam factor " + beamFactor.ToString("F2")
                    : null);
                return;
            }

            Vector3 toCamera = (renderingCamera.transform.position - worldPosition).normalized;
            float hotAlign = rotator.IsNotNull() ? Mathf.Abs(Vector3.Dot(toCamera, rotator.forward)) : 1f;
            float fogDot = SkyVisibilityResolver.GetFogginess();
            double attenDot = LighthouseVisualModel.DistanceAttenuation(distance);
            double transDot = LighthouseVisualModel.Transmittance(distance, fogDot);
            double hotDot = LighthouseVisualModel.BeamIntensityFactor(hotAlign);
            float iVisual = (float)LighthouseVisualModel.VisualIntensity(distance, fogDot, beamFactor, hotAlign);
            lastIVisual = iVisual;

            // Verbose per-dot model dump (once per diag interval, not per frame)
            if (ShouldLogDotModel(distance))
            {
                LogDotModel(worldPosition, distance, fogDot, attenDot, transDot, beamFactor, hotDot, iVisual);
            }

            SetDotEnabled(true, DotWillLog(true)
                ? "I=" + iVisual.ToString("F2") +
                  " screen=" + screen.x.ToString("F0") + "," + screen.y.ToString("F0") +
                  " onScreen=" + IsOnScreen(screen)
                : null);
            ApplyDotLayout(worldPosition, distance, screen, iVisual);
        }

        private bool IsDeviceView()
        {
            return renderingCamera.IsNotNull() && IsDeviceCamera(renderingCamera);
        }

        private bool IsFlareTakingOver(Vector3 worldPosition, float distance)
        {
            bool beyondFarClip = renderingCamera.IsNotNull() &&
                distance > renderingCamera.farClipPlane * 0.9f;
            return FlareActiveByLogic(worldPosition, distance) && !beyondFarClip;
        }

        private bool IsNightVisible()
        {
            return isNight && renderingCamera != null;
        }

        private bool ShouldLogDotModel(float distance)
        {
            return VerboseDiagnostics && Time.unscaledTime >= nextDiagnosticTime && distance >= 0;
        }

        private bool IsOnScreen(Vector3 screen)
        {
            return screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
        }

        private void LogDotModel(Vector3 worldPosition, float distance, float fogDot,
            double attenDot, double transDot, float beamFactor, double hotDot, float iVisual)
        {
            float capDot = (float)LighthouseVisualModel.DotSizePx(distance);
            float capStreak = (float)LighthouseVisualModel.StreakLengthCapPx(distance);
            Debug.Log("[RadioNav][Lighthouse] dot model: dist=" + distance.ToString("F0") +
                " fog=" + fogDot.ToString("F2") + " A=" + attenDot.ToString("F3") + " T=" + transDot.ToString("F3") +
                " wide=" + beamFactor.ToString("F2") + " beam=" + hotDot.ToString("F2") + " Ivis=" + iVisual.ToString("F3") +
                " size=" + capDot.ToString("F1") + " streakCap=" + capStreak.ToString("F1") +
                " lampY=" + (rotator.IsNotNull() ? rotator.position.y.ToString("F1") : "NaN") +
                " farY=" + worldPosition.y.ToString("F1"));
        }

        private void ApplyDotLayout(Vector3 worldPosition, float distance, Vector3 screen, float iVisual)
        {
            var metrics = LighthouseDotLayout.Compute(
                distance,
                screen.x, screen.y,
                Screen.width, Screen.height,
                iVisual,
                GetHorizonAngleDeg(renderingCamera, worldPosition));
            lastDotSize = metrics.Size;

            dotRect.anchoredPosition = metrics.CenterOffset;
            dotRect.sizeDelta = new Vector2(metrics.Size, metrics.Size);
            dotHaloRect.anchoredPosition = metrics.CenterOffset;
            dotHaloRect.sizeDelta = new Vector2(metrics.Size * 3f, metrics.Size * 3f);
            if (dotStreakRect.IsNotNull())
            {
                dotStreakRect.anchoredPosition = metrics.CenterOffset;
                dotStreakRect.sizeDelta = new Vector2(metrics.StreakLength, metrics.StreakThickness);
                dotStreakRect.localRotation = Quaternion.Euler(0f, 0f, metrics.HorizonAngleDeg);
                SetImageColor(dotStreakImage, metrics.StreakAlphaFinal);
            }
            SetImageColor(dotImage, metrics.CoreAlpha);
            SetImageColor(dotHaloImage, metrics.HaloAlpha);
        }

        /// <summary>
        /// Transition-only debug logging for the screen-space dot fallback.
        /// Reports why the dot is hidden — especially when the native flare
        /// takes over, the dot disappears by design (flare renders inside
        /// the binocular/periscope RenderTexture, the overlay dot does not).
        /// </summary>
        private void SetDotEnabled(bool enabled, string reason)
        {
            if (dotCanvas.IsNull())
            {
                return;
            }
            bool previous = dotWasActive;
            dotCanvas.enabled = enabled;
            dotWasActive = enabled;
            if (previous != enabled)
            {
                string camName = renderingCamera.IsNotNull() ? renderingCamera.name : "null";
                Debug.Log("[RadioNav][Lighthouse] glow: dot " + (enabled ? "ON" : "OFF") +
                    (string.IsNullOrEmpty(reason) ? "" : " [" + reason + "]") +
                    " cam='" + camName + "'" +
                    " flareRendering=" + FlareRenderingNow());
            }
        }

        // P5.1: transition-gated reason formatting — the expensive detail
        // strings are built only on the frame the state actually flips.
        private bool DotWillLog(bool enabled)
        {
            return dotCanvas.IsNotNull() && dotWasActive != enabled;
        }

        private bool FlareWillLog(GameObject flareObject, bool enabled)
        {
            return flareObject.IsNotNull() && flareWasActive != enabled;
        }

        private bool FlareRenderingNow()
        {
            if (!hasFlare || lensFlare.IsNull() || !lensFlare.gameObject.activeInHierarchy)
            {
                return false;
            }
            MeshRenderer renderer = lensFlare.GetComponent<MeshRenderer>();
            return renderer.IsNotNull() && renderer.enabled && renderer.isVisible;
        }

        /// <summary>
        /// Mirror of UpdateFlare's activation decision (night, camera,
        /// distance cap, beam gate). Deterministic per frame — unlike
        /// FlareRenderingNow(), which depends on renderer.isVisible and
        /// flickers while KeepFlareInView inflates the flare bounds.
        /// </summary>
        private bool FlareActiveByLogic(Vector3 worldPosition, float distance)
        {
            if (!hasFlare || lensFlare.IsNull() || !isNight || renderingCamera == null || rotator.IsNull())
            {
                return false;
            }
            // In the device view (periscope/binocular/UZO) the flare is the
            // only working renderer — the game itself has no distance limit
            // for its lighthouse flare (LighthouseLight.cs), and the
            // screen-space dot cannot be positioned there. In FPP the dot
            // takes over beyond FlareMaxDistanceM.
            if (!IsDeviceCamera(renderingCamera) && distance > FlareMaxDistanceM)
            {
                return false;
            }
            return GetBeamVisibilityFactor(worldPosition) > 0.01f;
        }

        private void EnsureDotUi()
        {
            if (dotUiBuilt || !ResolveImageType())
            {
                return;
            }

            var canvasGo = new GameObject("RadioNavLighthouseDot_" + name, typeof(RectTransform));
            var canvasRect = canvasGo.GetComponent<RectTransform>();
            dotCanvas = canvasGo.AddComponent<Canvas>();
            dotCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dotCanvas.sortingOrder = DotCanvasSortingOrder;
            dotCanvas.enabled = false;

            Texture2D texture = GetDotTexture();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            Texture2D streakTex = GetStreakTexture();
            Sprite streakSprite = Sprite.Create(streakTex, new Rect(0f, 0f, streakTex.width, streakTex.height), new Vector2(0.5f, 0.5f));

            dotStreakImage = CreateDotImage(canvasRect, "Streak", streakSprite);
            dotImage = CreateDotImage(canvasRect, "Core", sprite);
            dotHaloImage = CreateDotImage(canvasRect, "Halo", sprite);
            dotStreakRect = dotStreakImage.GetComponent<RectTransform>();
            dotRect = dotImage.GetComponent<RectTransform>();
            dotHaloRect = dotHaloImage.GetComponent<RectTransform>();
            dotUiBuilt = true;
        }

        private Component CreateDotImage(RectTransform parent, string childName, Sprite sprite)
        {
            var child = new GameObject(childName, typeof(RectTransform));
            var childRect = child.GetComponent<RectTransform>();
            childRect.SetParent(parent, false);

            Component image = child.AddComponent(imageType);
            imageType.GetProperty("sprite", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(image, sprite, null);
            imageType.GetProperty("raycastTarget", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(image, false, null);
            return image;
        }

        private bool ResolveImageType()
        {
            if (imageType.IsNotNull())
            {
                return true;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(ImageTypeName, false);
                if (type.IsNotNull())
                {
                    imageType = type;
                    return true;
                }
            }

            Debug.LogWarning("[RadioNav][Lighthouse] glow: '" + ImageTypeName +
                "' not found; screen-space dot fallback unavailable");
            return false;
        }

        private void SetImageColor(Component image, float alpha)
        {
            if (image.IsNull())
            {
                return;
            }
            // Hot path (dot + halo + streak every frame): resolve the
            // reflection property once instead of per call.
            if (imageColorProperty == null)
            {
                imageColorProperty = imageType.GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
            }
            imageColorProperty
                ?.SetValue(image, new Color(DotColor.r, DotColor.g, DotColor.b, alpha), null);
        }

        private void OnDestroy()
        {
            // Unity destroyed-object references compare equal to null only through
            // Unity's operator; custom IsNotNull can still pass during scene unload.
            if (dotCanvas != null)
            {
                Object.Destroy(dotCanvas.gameObject);
            }
        }

        private Vector3 GetWorldPosition()
        {
            // The bundle geometry is the single source of truth: the light is
            // anchored to the "Spot Light" child of the rotator (the same
            // node that hosts the HDRPLensFlare), so every light effect —
            // spot intensity, flare, screen-space dot — reads the same world
            // position and stays consistent with the model regardless of any
            // container rescale by the game.
            if (spotLight.IsNotNull() && spotLight.transform.IsNotNull())
            {
                return spotLight.transform.position;
            }
            if (rotator.IsNotNull())
            {
                return rotator.position;
            }
            if (container == null)
            {
                return transform.position;
            }
            return container.TransformPoint(rotatorLocalPosition);
        }

        /// <summary>
        /// Screen-space position of the light for the overlay dot, using the
        /// game's own MainCamera.WorldToScreenPointNonJitteredExternalView:
        /// it handles every periscope mode — FullScreenView (periscope/UZO)
        /// and the windowed 3D lens (binoculars: Vision mesh → LensCenter →
        /// interiorCamera) — and uses the non-jittered projection matrix, so
        /// the dot does not swim with TAA. Falls back to the plain camera
        /// projection when the game camera is unavailable.
        /// </summary>
        private Vector3 GetExternalViewScreenPoint(Vector3 worldPos)
        {
            bool throttled = VerboseDiagnostics && Time.unscaledTime >= nextDiagnosticTime;
            try
            {
                MainCamera mc = gameMainCamera;
                if (mc.IsNull())
                {
                    mc = Object.FindObjectOfType<MainCamera>();
                }
                if (mc.IsNotNull())
                {
                    Vector3 projected;
                    if (TryExternalViewProjection(mc, worldPos, throttled, out projected))
                        return projected;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Lighthouse] glow: external view screen point failed: " + e.Message);
            }
            return FallbackProjection(worldPos, throttled);
        }

        private bool TryExternalViewProjection(MainCamera mc, Vector3 worldPos, bool throttled, out Vector3 projected)
        {
            Vector4 v = mc.WorldToScreenPointNonJitteredExternalView(worldPos);
            bool njOnScreen = v.x >= 0f && v.x <= Screen.width && v.y >= 0f && v.y <= Screen.height;
            if (throttled)
            {
                LogBinocularDiag(mc, v);
            }
            if (njOnScreen)
            {
                projected = new Vector3(v.x, v.y, 1f);
                return true;
            }
            // The non-jittered projection can be stale in device views
            // (binoculars/UZO). Fall back to the plain projection of
            // the camera that actually renders — its projectionMatrix
            // is always current. z<=0 means the light is behind the
            // camera, which hides the dot.
            if (renderingCamera != null)
            {
                Vector3 plain = renderingCamera.WorldToScreenPoint(worldPos);
                if (throttled)
                {
                    Debug.Log("[RadioNav][Lighthouse] screenPt: nj off-screen, plain=" +
                        plain.x.ToString("F0") + "," + plain.y.ToString("F0") + " z=" + plain.z.ToString("F1") +
                        " onScreen=" + (plain.z > 0f && plain.x >= 0f && plain.x <= Screen.width && plain.y >= 0f && plain.y <= Screen.height));
                }
                if (plain.z > 0f)
                {
                    projected = plain;
                    return true;
                }
            }
            // Both projections failed: report failure so the caller falls
            // back to the plain projection path (which yields an off-screen
            // z<=0 and hides the dot).
            projected = new Vector3(v.x, v.y, -1f);
            return false;
        }

        private Vector3 FallbackProjection(Vector3 worldPos, bool throttled)
        {
            Vector3 result = renderingCamera != null ? renderingCamera.WorldToScreenPoint(worldPos) : Vector3.zero;
            if (throttled)
            {
                Debug.Log("[RadioNav][Lighthouse] screenPt: src=fallback" +
                    " v=" + result.x.ToString("F0") + "," + result.y.ToString("F0") +
                    " onScreen=" + (result.x >= 0f && result.x <= Screen.width && result.y >= 0f && result.y <= Screen.height) +
                    " cam=" + (renderingCamera != null ? renderingCamera.name : "null"));
            }
            return result;
        }

        /// <summary>
        /// Throttled diagnostic dump for the periscope/binocular/UZO view:
        /// which projection path the game used, where the dot landed, and the
        /// enabled/targetTexture state of every camera involved.
        /// </summary>
        private void LogBinocularDiag(MainCamera mc, Vector4 v)
        {
            try
            {
                string devCtl = "none";
                string fullScreen = "?";
                string devEn = "?";
                string devRT = "?";
                string intEn;
                if (mc.CurrentMode.ToString() == "Generic" &&
                    mc.GenericController.IsNotNull() &&
                    mc.GenericController.ExternalController is PeriscopeController pc &&
                    pc.IsNotNull() && pc.Periscope.IsNotNull())
                {
                    devCtl = "PeriscopeController";
                    fullScreen = pc.Periscope.FullScreenView.ToString();
                    Camera devCam = pc.Periscope.PeriscopeCamera;
                    devEn = devCam != null ? devCam.enabled.ToString() : "null";
                    devRT = devCam != null && devCam.targetTexture != null ? "yes" : "no";
                }
                intEn = LighthouseDiagnostics.GetInteriorCameraEnabled(mc);
                Debug.Log("[RadioNav][Lighthouse] screenPt: src=maincam" +
                    " mode=" + mc.CurrentMode +
                    " dev=" + devCtl +
                    " fullScreen=" + fullScreen +
                    " devEn=" + devEn + " devRT=" + devRT +
                    " intEn=" + intEn +
                    " mainEn=" + (mc.Camera != null ? mc.Camera.enabled.ToString() : "?") +
                    " v=" + v.x.ToString("F0") + "," + v.y.ToString("F0") +
                    " onScreen=" + (v.x >= 0f && v.x <= Screen.width && v.y >= 0f && v.y <= Screen.height) +
                    " screen=" + Screen.width + "x" + Screen.height);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Lighthouse] screenPt diag failed: " + e.Message);
            }
        }

        /// <summary>
        /// Resolves the camera that actually renders the current view.
        /// Priority order:
        /// 1. Camera.main — the exact same source the game's own
        ///    LighthouseLight.Update uses, so the flare behaves identically
        ///    in every camera mode, FPP included.
        /// 2. The periscope/binocular device camera (Generic mode with a
        ///    PeriscopeController external controller). It renders to a
        ///    RenderTexture, which the generic scan below must skip.
        /// 3. The camera captured by the pre-cull hook.
        /// 4. A screen-camera scan (GUI/loading cameras excluded).
        /// </summary>
        private void ResolveCamera()
        {
            // ADR-018: делегируем в общий CameraResolver (1 подписчик onPreCull, не дублируем логику)
            if (CameraResolver.TryResolve(out var cam))
                renderingCamera = cam;
            else
                renderingCamera = null;
            // gameMainCamera никогда не присваивался (CS0649) — из-за этого
            // IsDeviceCamera/EnsureDeviceCameraFarClip были мёртвыми. Берём
            // инстанс из резолвера (тот же FindObjectOfType внутри).
            if (gameMainCamera.IsNull())
            {
                gameMainCamera = Object.FindObjectOfType<MainCamera>();
            }
        }

        /// <summary>
        /// The game hard-codes the periscope/binocular camera far clip to
        /// 11000 m (Periscope.cs:1249), which culls lighthouses 12+ km away
        /// from the device view entirely. While a device camera is active,
        /// raise its far clip so the lighthouse (and its flare) render.
        /// The game may reset the clip on height changes, so this runs every
        /// frame while the device view is up.
        /// </summary>
        private void EnsureDeviceCameraFarClip()
        {
            Camera cam = renderingCamera;
            if (cam.IsNull() || !IsDeviceCamera(cam))
            {
                return;
            }
            if (cam.farClipPlane < MinDeviceCameraFarM)
            {
                float previous = cam.farClipPlane;
                cam.farClipPlane = MinDeviceCameraFarM;
                Debug.Log("[RadioNav][Lighthouse] device far clip raised " + previous.ToString("F0") +
                    " -> " + cam.farClipPlane.ToString("F0") + " (camera '" + cam.name + "')");
            }
        }

        /// <summary>
        /// True when the camera is the active periscope/binocular device
        /// camera. Delegates to the shared CameraResolver check (ADR-018
        /// follow-up — the logic was duplicated here and in the resolver).
        /// </summary>
        private bool IsDeviceCamera(Camera cam)
        {
            if (gameMainCamera.IsNull())
            {
                return false;
            }
            return CameraResolver.IsDeviceCamera(gameMainCamera, cam);
        }

        /// <summary>
        /// ADR-017/018: уносит статику маяка при hot-reload. Best practice — 1 подписчик onPreCull, без GPU утечки.
        /// Вызывается только на main thread из Main.OnLoaded (loaded==true), когда все glow-GO уже уничтожены.
        /// </summary>
        public static void ResetStatics()
        {
            if (dotTexture.IsNotNull())
            {
                Object.Destroy(dotTexture);
                dotTexture = null;
            }
            if (streakTexture.IsNotNull())
            {
                Object.Destroy(streakTexture);
                streakTexture = null;
            }
            renderingCamera = null;
            CameraResolver.ResetForReload();
        }

        private void FindSun()
        {
            Light[] lights = Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    sun = lights[i];
                    return;
                }
            }
        }

        /// <summary>
        /// Periodic state dump (throttled) used to diagnose why the flare
        /// is or is not visible in a given camera mode.
        /// </summary>
        private void LogDiagnostics(Vector3 worldPosition)
        {
            if (Time.unscaledTime < nextDiagnosticTime)
            {
                return;
            }
            nextDiagnosticTime = Time.unscaledTime + DiagnosticIntervalSeconds;

            var snap = new LighthouseDiagnostics.GlowSnapshot
            {
                LensFlare = lensFlare,
                FlareIntensityProperty = flareIntensityProperty,
                RenderingCamera = renderingCamera,
                Rotator = rotator,
                Container = container,
                SpotLight = spotLight,
                GameMainCamera = gameMainCamera,
                DotCanvas = dotCanvas,
                IsNight = isNight,
                LastIVisual = lastIVisual,
                LastDotSize = lastDotSize,
                ObjectY = transform.position.y,
                CoreAlpha = LighthouseDotLayout.CoreDotAlpha * lastIVisual,
                HaloAlpha = LighthouseDotLayout.HaloMaxAlpha * lastIVisual,
                StreakAlpha = LighthouseDotLayout.StreakAlpha * lastIVisual,
            };
            LighthouseDiagnostics.Log(snap, worldPosition);
        }

        private void FindSpotLight(Transform rotator)
        {
            Transform spot = TransformHelpers.FindChildRecursive(rotator, SpotLightName);
            if (spot.IsNull())
            {
                Debug.LogWarning("[RadioNav][Lighthouse] glow: '" + SpotLightName + "' not found under '" + rotator.name + "'");
                return;
            }
            spotLight = spot.GetComponent<Light>();
            if (spotLight.IsNull())
            {
                Debug.LogWarning("[RadioNav][Lighthouse] glow: '" + SpotLightName + "' has no Light component");
                return;
            }
            spotLight.range = Mathf.Max(spotLight.range, MaxLightRangeM);
            spotLight.intensity = Mathf.Max(spotLight.intensity, MinLightIntensity);
            if (Vector3.Dot(spot.forward, rotator.forward) < -0.5f)
            {
                spot.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                Debug.Log("[RadioNav][Lighthouse] spot auto-flipped 180 (prefab opposite)");
            }
        }

        /// <summary>
        /// World illumination channel: the Spot Light is driven by the
        /// physical A(r)·T(r) attenuation (no display gamma, no cones — the
        /// rotating cone is the spotlight transform itself). Without this the
        /// beam floodlights terrain like a stadium even when the source on
        /// screen has shrunk to a 10px point. Off during daytime.
        /// </summary>
        private float lastLoggedSpotIntensity = float.NaN;
        private void UpdateSpot(Vector3 worldPosition)
        {
            if (spotLight.IsNull())
            {
                return;
            }
            if (!isNight || renderingCamera.IsNull())
            {
                if (spotLight.intensity != 0f)
                    Debug.Log("[RadioNav][Lighthouse] spot OFF (night=" + isNight + " camNull=" + renderingCamera.IsNull() + ") was=" + spotLight.intensity.ToString("F2"));
                spotLight.intensity = 0f;
                return;
            }
            float distance = (renderingCamera.transform.position - worldPosition).magnitude;
            float fog = SkyVisibilityResolver.GetFogginess();
            double atten = LighthouseVisualModel.DistanceAttenuation(distance);
            double trans = LighthouseVisualModel.Transmittance(distance, fog);
            float worldI = (float)LighthouseVisualModel.WorldLightIntensity(distance, fog);
            float newIntensity = MinLightIntensity * worldI;
            // Throttled verbose: log once per diag interval (not per frame)
            bool significant = float.IsNaN(lastLoggedSpotIntensity) || Mathf.Abs(newIntensity - lastLoggedSpotIntensity) > 0.15f;
            if (significant && VerboseDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
            {
                float hSpot = spotLight.transform.position.y;
                float hFar = worldPosition.y;
                Debug.Log("[RadioNav][Lighthouse] spot: dist=" + distance.ToString("F0") +
                    " A=" + atten.ToString("F3") + " T=" + trans.ToString("F3") + " fog=" + fog.ToString("F2") +
                    " worldI=" + worldI.ToString("F3") + " -> intensity " + spotLight.intensity.ToString("F2") + "->" + newIntensity.ToString("F2") +
                    " heights spotY=" + hSpot.ToString("F1") + " farY=" + hFar.ToString("F1"));
                lastLoggedSpotIntensity = newIntensity;
            }
            spotLight.intensity = newIntensity;
        }

        private void UpdateDayNight()
        {
            if (weather.IsNotNull())
            {
                isNight = weather.IsDarkWeather;
                return;
            }

            bool sunDown = sun.IsNull() || sun.transform.forward.y > -0.05f || sun.intensity < DaySunIntensityThreshold;
            isNight = sunDown;
        }

        /// <summary>
        /// Locates the HDRPLensFlare component on the spawned prefab (by type
        /// name via reflection — assembly-agnostic, works whether the
        /// component was compiled into the game or into a plugin assembly).
        /// </summary>
        private void FindLensFlare(Transform rotator)
        {
            Component[] components = rotator.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component.IsNotNull() && component.GetType().Name == FlareComponentTypeName)
                {
                    lensFlare = component;
                    flareIntensityProperty = component.GetType().GetProperty(
                        "Intensity", BindingFlags.Public | BindingFlags.Instance);
                    hasFlare = true;
                    Debug.Log("[RadioNav][Lighthouse] glow: HDRPLensFlare found on '" + component.name + "'");
                    return;
                }
            }
            hasFlare = false;
        }

        /// <summary>
        /// Drives the native HDRPLensFlare with the unified perceptual model:
        /// intensity = FlareIntensityScale × I_visual, where I_visual already
        /// contains distance attenuation, atmosphere transmittance, the wide
        /// 60° loom gate and the 2°/8° hot cone. Beyond FlareMaxDistanceM the
        /// flare is disabled entirely — the screen-space dot takes over and
        /// enforces the exact far pixel caps (≤10px dot / ≤13px streak).
        /// EXCEPTION: in the device view (periscope/binocular/UZO) the dot
        /// cannot be positioned, so the flare stays active at any distance —
        /// same as the game's own LighthouseLight (no distance limit).
        /// </summary>
        private void UpdateFlare(Vector3 worldPosition)
        {
            if (lensFlare.IsNull())
            {
                return;
            }

            GameObject flareObject = lensFlare.gameObject;
            if (!isNight || renderingCamera == null || rotator.IsNull())
            {
                DisableFlare(flareObject, FlareWillLog(flareObject, false)
                    ? "night=" + isNight + " cam=" + (renderingCamera.IsNotNull()) + " rot=" + rotator.IsNotNull()
                    : null);
                return;
            }

            float distance = (renderingCamera.transform.position - worldPosition).magnitude;
            // In the device view (periscope/binocular/UZO) the flare is the
            // only working renderer — the game itself has no distance limit
            // for its lighthouse flare (LighthouseLight.cs), and the
            // screen-space dot cannot be positioned there. Mirror the
            // FlareActiveByLogic exception: keep the flare alive beyond
            // FlareMaxDistanceM while a device camera renders.
            bool deviceView = IsDeviceCamera(renderingCamera);
            if (!deviceView && distance > FlareMaxDistanceM)
            {
                DisableFlare(flareObject, FlareWillLog(flareObject, false)
                    ? "beyond " + FlareMaxDistanceM.ToString("F0") +
                      "m (dist=" + distance.ToString("F0") + ")"
                    : null);
                return;
            }
            if (deviceView && distance > FlareMaxDistanceM && Time.unscaledTime >= nextDiagnosticTime)
            {
                Debug.Log("[RadioNav][Lighthouse] flare: device view keeps flare active beyond " +
                    FlareMaxDistanceM.ToString("F0") + "m (dist=" + distance.ToString("F0") +
                    " cam='" + renderingCamera.name + "')");
            }

            // B_wide: same 60° loom window as the screen-space dot.
            float wideGate = GetBeamVisibilityFactor(worldPosition);
            if (wideGate <= 0.01f)
            {
                DisableFlare(flareObject, FlareWillLog(flareObject, false)
                    ? "beam not aligned (wide=" + wideGate.ToString("F2") + ")"
                    : null);
                return;
            }

            SetFlareActive(flareObject, true, FlareWillLog(flareObject, true)
                ? "aligned wide=" + wideGate.ToString("F2")
                : null);
            ApplyFlareIntensity(flareObject, distance, wideGate);
        }

        private void DisableFlare(GameObject flareObject, string reason)
        {
            SetFlareActive(flareObject, false, FlareWillLog(flareObject, false) ? reason : null);
        }

        private void ApplyFlareIntensity(GameObject flareObject, float distance, float wideGate)
        {
            if (flareIntensityProperty.IsNull())
                return;
            Vector3 directionToCamera = (renderingCamera.transform.position - flareObject.transform.position).normalized;
            float hotAlign = Mathf.Abs(Vector3.Dot(directionToCamera, rotator.forward));
            float fogFlare = SkyVisibilityResolver.GetFogginess();
            double attenFlare = LighthouseVisualModel.DistanceAttenuation(distance);
            double transFlare = LighthouseVisualModel.Transmittance(distance, fogFlare);
            double hotFlare = LighthouseVisualModel.BeamIntensityFactor(hotAlign);
            float iVisual = (float)LighthouseVisualModel.VisualIntensity(distance, fogFlare, wideGate, hotAlign);
            float flareIntensityValue = FlareIntensityScale * iVisual;
            if (VerboseDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
            {
                Debug.Log("[RadioNav][Lighthouse] flare model: dist=" + distance.ToString("F0") +
                    " A=" + attenFlare.ToString("F3") + " T=" + transFlare.ToString("F3") + " fog=" + fogFlare.ToString("F2") +
                    " wide=" + wideGate.ToString("F2") + " beam=" + hotFlare.ToString("F2") + " Ivis=" + iVisual.ToString("F3") + " -> flareI=" + flareIntensityValue.ToString("F3") +
                    " lampY=" + (rotator.IsNotNull() ? rotator.position.y.ToString("F1") : "NaN") + " farY=" + flareObject.transform.position.y.ToString("F1"));
            }
            flareIntensityProperty.SetValue(lensFlare, flareIntensityValue, null);
        }

        /// <summary>
        /// Transition-only debug logging: reports flare state changes with
        /// the reason and the camera that was current at the moment of the
        /// change (periscope/binocular cameras render into a RenderTexture,
        /// so camera name matters for diagnosing visibility gaps).
        /// </summary>
        private void SetFlareActive(GameObject flareObject, bool active, string reason)
        {
            bool previous = flareWasActive;
            flareObject.SetActive(active);
            flareWasActive = active;
            if (previous != active)
            {
                if (active)
                {
                    // HDRPLensFlare may rebuild its flare elements when
                    // enabled — re-apply the scaled sizes so the +25% holds
                    // in every enable cycle.
                    ApplyFlareElementSizes();
                }
                string camName = renderingCamera.IsNotNull() ? renderingCamera.name : "null";
                float distance = renderingCamera.IsNotNull()
                    ? (renderingCamera.transform.position - GetWorldPosition()).magnitude
                    : -1f;
                Debug.Log("[RadioNav][Lighthouse] glow: flare " + (active ? "ON" : "OFF") +
                    (string.IsNullOrEmpty(reason) ? "" : " [" + reason + "]") +
                    " cam='" + camName + "' dist=" + distance.ToString("F0"));
            }
        }

        /// <summary>
        /// Screen-space flare element sizes (fractions of screen height),
        /// scaled by FlareSizeScale (+25% user preference — the light reads
        /// too small through the binoculars, especially up close). Applied
        /// at Initialize and re-applied on every flare enable transition.
        /// </summary>
        private void ApplyFlareElementSizes()
        {
            if (flareGlowElement != null)
            {
                var gf = flareGlowElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (gf != null) gf.SetValue(flareGlowElement, new Vector2(0.010f * FlareSizeScale, 0.010f * FlareSizeScale));
            }
            if (flareStreakElement != null)
            {
                var sf = flareStreakElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null) sf.SetValue(flareStreakElement, new Vector2(0.012f * FlareSizeScale, 0.006f * FlareSizeScale));
            }
            if (flareStarElement != null)
            {
                var stf = flareStarElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (stf != null) stf.SetValue(flareStarElement, new Vector2(0.015f * FlareSizeScale, 0.015f * FlareSizeScale));
            }
        }

        /// <summary>
        /// The HDRPLensFlare component rebuilds the flare mesh with bounds
        /// equal to OcclusionRadius every time it is enabled. Those small
        /// bounds get frustum-culled when the lighthouse is beyond the
        /// camera's far clip plane. Inflating the bounds makes Unity keep
        /// the flare renderer; the shader then keeps the quad inside the
        /// depth range (_InfinitelyFar), so the light stays visible from
        /// any distance in every camera mode, exactly like the game's own
        /// lighthouse flares.
        /// </summary>
        private void KeepFlareInView()
        {
            if (flareMeshFilter.IsNull() || !lensFlare.gameObject.activeInHierarchy)
            {
                return;
            }
            Mesh mesh = flareMeshFilter.sharedMesh;
            if (mesh.IsNull())
            {
                return;
            }
            Vector3 extents = new Vector3(1e9f, 1e9f, 1e9f);
            if (mesh.bounds.extents != extents)
            {
                mesh.bounds = new Bounds(Vector3.zero, extents * 2f);
                Debug.Log("[RadioNav][Lighthouse] glow: flare bounds inflated (HDRPLensFlare rebuilds them on enable)");
            }
        }

        /// <summary>
        /// Collects renderers whose material uses the lighthouse shader and
        /// feeds their _RotationOffset so the emissive windows sweep together
        /// with the rotating beam.
        /// </summary>
        private void FindWindowRenderers(Transform rotator)
        {
            windowRenderers.Clear();
            Renderer[] renderers = rotator.root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer.IsNull() || renderer.sharedMaterial.IsNull())
                {
                    continue;
                }
                Shader shader = renderer.sharedMaterial.shader;
                if (shader.IsNotNull() && shader.name == LighthouseShaderName)
                {
                    windowRenderers.Add(renderer);
                }
            }
        }

        private void UpdateWindowRotation()
        {
            if (windowRenderers.Count == 0 || rotator.IsNull())
            {
                return;
            }
            if (rotationBlock.IsNull())
            {
                rotationBlock = new MaterialPropertyBlock();
            }
            rotationBlock.SetFloat(RotationOffsetId, rotator.eulerAngles.y);
            for (int i = 0; i < windowRenderers.Count; i++)
            {
                if (windowRenderers[i].IsNotNull())
                {
                    windowRenderers[i].SetPropertyBlock(rotationBlock);
                }
            }
        }

        /// <summary>
        /// Beam visibility factor: the lighthouse light is directed along
        /// the horizon like a flashlight. Factor is 1 while the beam points
        /// at the player and fades smoothly to 0 once the beam has rotated
        /// more than BeamVisibleAngleDeg away around the Y axis.
        /// </summary>
        private float GetBeamVisibilityFactor(Vector3 worldPosition)
        {
            if (rotator.IsNull() || renderingCamera.IsNull())
            {
                return 1f;
            }
            Vector3 toCamera = (renderingCamera.transform.position - worldPosition).normalized;
            Vector3 flat = new Vector3(toCamera.x, 0f, toCamera.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return 1f;
            }
            flat.Normalize();
            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(rotator.forward, flat), -1f, 1f)) * Mathf.Rad2Deg;
            float t = Mathf.Clamp01(1f - angle / BeamVisibleAngleDeg);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        private static Texture2D GetDotTexture()
        {
            if (dotTexture.IsNotNull())
            {
                return dotTexture;
            }

            dotTexture = new Texture2D(DotTextureSize, DotTextureSize, TextureFormat.RGBA32, false);
            dotTexture.name = "RadioNavLighthouseDot";
            dotTexture.hideFlags = HideFlags.DontSave;
            float half = DotTextureSize * 0.5f;
            for (int y = 0; y < DotTextureSize; y++)
            {
                for (int x = 0; x < DotTextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = r >= 1f ? 0f : (1f - r) * (1f - r);
                    Color32 pixel = new Color32(
                        (byte)Mathf.RoundToInt(DotColor.r * 255f),
                        (byte)Mathf.RoundToInt(DotColor.g * 255f),
                        (byte)Mathf.RoundToInt(DotColor.b * 255f),
                        (byte)Mathf.RoundToInt(falloff * 255f));
                    dotTexture.SetPixel(x, y, pixel);
                }
            }
            dotTexture.Apply();
            return dotTexture;
        }

        /// <summary>
        /// Horizontal line texture for the screen-space streak element:
        /// a soft gaussian falloff along the line axis and a tighter one
        /// across it, so the stretched glow reads as a thin beam.
        /// </summary>
        private static Texture2D GetStreakTexture()
        {
            if (streakTexture.IsNotNull())
            {
                return streakTexture;
            }

            streakTexture = new Texture2D(StreakTextureWidth, StreakTextureHeight, TextureFormat.RGBA32, false);
            streakTexture.name = "RadioNavLighthouseStreak";
            streakTexture.hideFlags = HideFlags.DontSave;
            float hw = StreakTextureWidth * 0.5f;
            float hh = StreakTextureHeight * 0.5f;
            for (int y = 0; y < StreakTextureHeight; y++)
            {
                for (int x = 0; x < StreakTextureWidth; x++)
                {
                    float dx = (x + 0.5f - hw) / hw;
                    float dy = (y + 0.5f - hh) / hh;
                    float falloff = Mathf.Exp(-(dx * dx * 2.5f)) * Mathf.Exp(-(dy * dy * 5f));
                    Color32 pixel = new Color32(
                        (byte)Mathf.RoundToInt(DotColor.r * 255f),
                        (byte)Mathf.RoundToInt(DotColor.g * 255f),
                        (byte)Mathf.RoundToInt(DotColor.b * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(falloff) * 255f));
                    streakTexture.SetPixel(x, y, pixel);
                }
            }
            streakTexture.Apply();
            return streakTexture;
        }

        /// <summary>
        /// Screen-space angle of the world horizon line, so the streak
        /// stays parallel to the ground even when the camera rolls
        /// (boat heel, wave motion). The world direction is the ground
        /// plane vector perpendicular to the camera's view direction,
        /// projected through the camera into screen space.
        /// </summary>
        private static float GetHorizonAngleDeg(Camera cam, Vector3 worldPos)
        {
            Vector3 forward = cam.transform.forward;
            Vector3 groundForward = new Vector3(forward.x, 0f, forward.z);
            if (groundForward.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }
            groundForward.Normalize();
            Vector3 horizonDir = Vector3.Cross(groundForward, Vector3.up);
            Vector3 p0 = cam.WorldToScreenPoint(worldPos);
            Vector3 p1 = cam.WorldToScreenPoint(worldPos + horizonDir * 1000f);
            float dx = p1.x - p0.x;
            float dy = p1.y - p0.y;
            if (dx * dx + dy * dy < 0.0001f)
            {
                return 0f;
            }
            return Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        }
    }
}
