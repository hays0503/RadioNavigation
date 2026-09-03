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
        // Unified perceptual model lives in LighthouseVisualModel (pure math,
        // unit-tested). Dot/streak layer constants and layout math live in
        // LighthouseDotLayout (P5.4 LBG-2); pixel sizes come from the model.
        private const float BeamVisibleAngleDeg = 60f;
        // Native flare is replaced by the screen-space dot beyond this range
        // (mirrors LighthouseFlareDriver.FlareMaxDistanceM; kept here for
        // the dot-takeover decision in FlareActiveByLogic).
        private const float FlareMaxDistanceM = 1200f;
        // The game hard-codes the periscope/binocular camera far clip to 11000
        // (Periscope.cs:1249). Lighthouses sit beyond it (12+ km), so the
        // device view culls them entirely. Raise the clip while a device
        // camera is active — same order of magnitude as the game's own
        // FreeFly far clip (26000).
        private const float MinDeviceCameraFarM = 30000f;
        private const float DiagnosticIntervalSeconds = 2f;

        // Shared dot/streak textures and window-shader sync live in
        // LighthouseGlowArt (extracted); the static renderingCamera stays
        // here (per-frame camera hand-off with CameraResolver).
        private static Camera renderingCamera;

        private Transform container;
        private Transform rotator;
        private Vector3 rotatorLocalPosition;
        // Spot-light world illumination + day/night gate live in
        // LighthouseSpotLight (extracted).
        private readonly LighthouseSpotLight spotLightDriver = new LighthouseSpotLight();
        // HDRPLensFlare pipeline lives in LighthouseFlareDriver (extracted).
        private readonly LighthouseFlareDriver flareDriver = new LighthouseFlareDriver();
        private MainCamera gameMainCamera;
        private MaterialPropertyBlock rotationBlock;
        private readonly List<Renderer> windowRenderers = new List<Renderer>();
        // Screen-space dot overlay lives in LighthouseDotUi (extracted).
        private readonly LighthouseDotUi dotUi = new LighthouseDotUi();
        private bool initialized;
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
            spotLightDriver.Initialize(rotator);
            flareDriver.Initialize(rotator);
            windowRenderers.Clear();
            var found = LighthouseGlowArt.FindWindowRenderers(rotator);
            for (int i = 0; i < found.Count; i++)
                windowRenderers.Add(found[i]);
            initialized = true;
            // --- Init heights dump (requested) ---
            try
            {
                Vector3 wp = GetWorldPosition();
                float hBeaconInit = container.IsNotNull() ? container.position.y : float.NaN;
                float hLampInit = rotator.IsNotNull() ? rotator.position.y : float.NaN;
                float hObjInit = transform.position.y;
                float hSpotInit = spotLightDriver.Spot.IsNotNull() ? spotLightDriver.Spot.transform.position.y : float.NaN;
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
                "' (flare=" + (flareDriver.HasFlare ? "yes" : "no, dot fallback") + ", windows=" + windowRenderers.Count + ")");
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }
            ResolveCamera();
            EnsureDeviceCameraFarClip();
            spotLightDriver.UpdateDayNight();
            Vector3 worldPosition = GetWorldPosition();
            LighthouseGlowArt.UpdateWindowRotation(windowRenderers, rotator, ref rotationBlock);
            spotLightDriver.Update(worldPosition, renderingCamera,
                VerboseDiagnostics, nextDiagnosticTime);
            var frame = new FlareFrame();
            frame.isNight = spotLightDriver.IsNight;
            frame.renderingCamera = renderingCamera;
            frame.rotator = rotator;
            frame.worldPosition = worldPosition;
            frame.wideGate = GetBeamVisibilityFactor(worldPosition);
            frame.deviceView = IsDeviceCamera(renderingCamera);
            frame.verbose = VerboseDiagnostics;
            frame.now = Time.unscaledTime;
            frame.nextDiagTime = nextDiagnosticTime;
            frame.worldPositionProvider = GetWorldPosition;
            flareDriver.Update(frame);
            flareDriver.KeepInView();
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

            Vector3 screen = LighthouseScreenProjection.Project(gameMainCamera, renderingCamera,
                worldPosition, VerboseDiagnostics && Time.unscaledTime >= nextDiagnosticTime);
            if (screen.z <= 0f)
            {
                SetDotEnabled(false, DotWillLog(false)
                    ? "behind camera (screen.z=" + screen.z.ToString("F1") + ")"
                    : null);
                return;
            }

            if (!dotUi.EnsureBuilt(name))
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
            return spotLightDriver.IsNight && renderingCamera != null;
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
                LighthouseGlowArt.GetHorizonAngleDeg(renderingCamera, worldPosition));
            lastDotSize = metrics.Size;

            RectTransform dotRect = dotUi.CoreRect;
            dotRect.anchoredPosition = metrics.CenterOffset;
            dotRect.sizeDelta = new Vector2(metrics.Size, metrics.Size);
            RectTransform dotHaloRect = dotUi.HaloRect;
            dotHaloRect.anchoredPosition = metrics.CenterOffset;
            dotHaloRect.sizeDelta = new Vector2(metrics.Size * 3f, metrics.Size * 3f);
            RectTransform dotStreakRect = dotUi.StreakRect;
            if (dotStreakRect.IsNotNull())
            {
                dotStreakRect.anchoredPosition = metrics.CenterOffset;
                dotStreakRect.sizeDelta = new Vector2(metrics.StreakLength, metrics.StreakThickness);
                dotStreakRect.localRotation = Quaternion.Euler(0f, 0f, metrics.HorizonAngleDeg);
                dotUi.SetImageColor(dotUi.StreakImage, metrics.StreakAlphaFinal);
            }
            dotUi.SetImageColor(dotUi.CoreImage, metrics.CoreAlpha);
            dotUi.SetImageColor(dotUi.HaloImage, metrics.HaloAlpha);
        }

        /// <summary>
        /// Transition-only debug logging for the screen-space dot fallback.
        /// Reports why the dot is hidden — especially when the native flare
        /// takes over, the dot disappears by design (flare renders inside
        /// the binocular/periscope RenderTexture, the overlay dot does not).
        /// </summary>
        private void SetDotEnabled(bool enabled, string reason)
        {
            if (!dotUi.Built)
            {
                return;
            }
            bool previous = dotWasActive;
            dotUi.SetEnabled(enabled);
            dotWasActive = enabled;
            if (previous != enabled)
            {
                string camName = renderingCamera.IsNotNull() ? renderingCamera.name : "null";
                Debug.Log("[RadioNav][Lighthouse] glow: dot " + (enabled ? "ON" : "OFF") +
                    (string.IsNullOrEmpty(reason) ? "" : " [" + reason + "]") +
                    " cam='" + camName + "'" +
                    " flareRendering=" + flareDriver.RenderingNow());
            }
        }

        // P5.1: transition-gated reason formatting — the expensive detail
        // strings are built only on the frame the state actually flips.
        private bool DotWillLog(bool enabled)
        {
            return dotUi.Built && dotWasActive != enabled;
        }

        /// <summary>
        /// Mirror of the flare driver's activation decision (night, camera,
        /// distance cap, beam gate). Deterministic per frame — unlike
        /// RenderingNow(), which depends on renderer.isVisible and
        /// flickers while the bounds inflation is active.
        /// </summary>
        private bool FlareActiveByLogic(Vector3 worldPosition, float distance)
        {
            if (!flareDriver.HasFlare || !flareDriver.LensFlare.IsNotNull() || !spotLightDriver.IsNight || renderingCamera == null || rotator.IsNull())
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

        private void OnDestroy()
        {
            // Unity destroyed-object references compare equal to null only through
            // Unity's operator; custom IsNotNull can still pass during scene unload.
            dotUi.Destroy();
        }

        private Vector3 GetWorldPosition()
        {
            // The bundle geometry is the single source of truth: the light is
            // anchored to the "Spot Light" child of the rotator (the same
            // node that hosts the HDRPLensFlare), so every light effect —
            // spot intensity, flare, screen-space dot — reads the same world
            // position and stays consistent with the model regardless of any
            // container rescale by the game.
            if (spotLightDriver.Spot.IsNotNull() && spotLightDriver.Spot.transform.IsNotNull())
            {
                return spotLightDriver.Spot.transform.position;
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
            LighthouseGlowArt.ResetStatics();
            renderingCamera = null;
            CameraResolver.ResetForReload();
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
                LensFlare = flareDriver.LensFlare,
                FlareIntensityProperty = flareDriver.IntensityProperty,
                RenderingCamera = renderingCamera,
                Rotator = rotator,
                Container = container,
                SpotLight = spotLightDriver.Spot,
                GameMainCamera = gameMainCamera,
                DotCanvas = dotUi.Canvas,
                IsNight = spotLightDriver.IsNight,
                LastIVisual = lastIVisual,
                LastDotSize = lastDotSize,
                ObjectY = transform.position.y,
                CoreAlpha = LighthouseDotLayout.CoreDotAlpha * lastIVisual,
                HaloAlpha = LighthouseDotLayout.HaloMaxAlpha * lastIVisual,
                StreakAlpha = LighthouseDotLayout.StreakAlpha * lastIVisual,
            };
            LighthouseDiagnostics.Log(snap, worldPosition);
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
    }
}
