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
        private const float MaxLightRangeM = 6000f;
        private const float MinLightIntensity = 4f;
        private const float DaySunIntensityThreshold = 0.6f;

        private const float DotAngularWidthDeg = 0.35f;
        private const float MinDotPx = 8f;
        private const float MaxDotPx = 80f;
        private const float CoreDotAlpha = 0.95f;
        private const float HaloDotAlpha = 0.35f;
        private const float StreakAlpha = 0.5f;
        private const float StreakLengthMultiplier = 6.5f;
        private const float StreakThicknessMultiplier = 0.25f;
        private const float MaxStreakPx = 150f;
        private const float BeamVisibleAngleDeg = 60f;
        private const float LightVisualOffsetY = -0.5f;
        private const int StreakTextureWidth = 128;
        private const int StreakTextureHeight = 16;
        private const int DotTextureSize = 64;
        private const int DotCanvasSortingOrder = 500;
        private const string ImageTypeName = "UnityEngine.UI.Image";
        private const float DiagnosticIntervalSeconds = 2f;

        private static readonly Color DotColor = new Color(1f, 0.94f, 0.84f);
        private static readonly int RotationOffsetId = Shader.PropertyToID("_RotationOffset");

        [Inject] private static Weather weather = null;

        private static Texture2D dotTexture;
        private static Texture2D streakTexture;
        private static Camera renderingCamera;
        private static bool renderHookInstalled;

        private Transform container;
        private Transform rotator;
        private Vector3 rotatorLocalPosition;
        private Light spotLight;
        private Light sun;
        private Camera lastLoggedCamera;
        private Component lensFlare;
        private PropertyInfo flareIntensityProperty;
        private MeshFilter flareMeshFilter;
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
            }
            FindWindowRenderers(rotator);
            initialized = true;
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
            UpdateDayNight();
            Vector3 worldPosition = GetWorldPosition();
            UpdateWindowRotation();
            UpdateFlare(worldPosition);
            KeepFlareInView();
            UpdateDot(worldPosition);
            LogDiagnostics(worldPosition);
        }

        private void UpdateDot(Vector3 worldPosition)
        {
            bool beyondFarClip = renderingCamera.IsNotNull() &&
                (renderingCamera.transform.position - worldPosition).magnitude >
                renderingCamera.farClipPlane * 0.9f;

            // Hide the dot only when the native flare is actually drawing and
            // the lighthouse is inside the camera's far clip. Beyond the far
            // clip the world-space flare may be culled, so the screen-space
            // dot takes over — this guarantees the light stays visible in
            // every camera mode (FPP included) at any distance.
            if (FlareRenderingNow() && !beyondFarClip)
            {
                SetDotEnabled(false, "flare rendering, in far clip");
                return;
            }

            if (!isNight || renderingCamera == null)
            {
                SetDotEnabled(false, "not night or no camera");
                return;
            }

            Vector3 screen = renderingCamera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f)
            {
                SetDotEnabled(false, "behind camera (screen.z=" + screen.z.ToString("F1") + ")");
                return;
            }

            EnsureDotUi();
            if (dotCanvas.IsNull())
            {
                return;
            }

            // The light behaves like the real rotating beam: full brightness
            // while it points at the player, fading out as the beam rotates
            // more than BeamVisibleAngleDeg away around the Y axis.
            float beamFactor = GetBeamVisibilityFactor(worldPosition);
            if (beamFactor <= 0.01f)
            {
                SetDotEnabled(false, "beam factor " + beamFactor.ToString("F2"));
                return;
            }

            SetDotEnabled(true, "beam factor " + beamFactor.ToString("F2"));
            float size = Mathf.Clamp(Screen.height * (DotAngularWidthDeg / renderingCamera.fieldOfView), MinDotPx, MaxDotPx);
            float sx = screen.x - Screen.width * 0.5f;
            float sy = screen.y - Screen.height * 0.5f;

            dotRect.anchoredPosition = new Vector2(sx, sy);
            dotRect.sizeDelta = new Vector2(size, size);
            dotHaloRect.anchoredPosition = new Vector2(sx, sy);
            dotHaloRect.sizeDelta = new Vector2(size * 3f, size * 3f);
            if (dotStreakRect.IsNotNull())
            {
                float streakLength = Mathf.Min(size * StreakLengthMultiplier, MaxStreakPx);
                float streakThickness = Mathf.Max(2f, size * StreakThicknessMultiplier);
                dotStreakRect.anchoredPosition = new Vector2(sx, sy);
                dotStreakRect.sizeDelta = new Vector2(streakLength, streakThickness);
                dotStreakRect.localRotation = Quaternion.Euler(0f, 0f, GetHorizonAngleDeg(renderingCamera, worldPosition));
                SetImageColor(dotStreakImage, StreakAlpha * beamFactor);
            }
            SetImageColor(dotImage, CoreDotAlpha * beamFactor);
            SetImageColor(dotHaloImage, HaloDotAlpha * beamFactor);
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
                    " [" + reason + "] cam='" + camName + "'" +
                    " flareRendering=" + FlareRenderingNow());
            }
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
            imageType.GetProperty("color", BindingFlags.Public | BindingFlags.Instance)
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
            Vector3 pos;
            if (container == null)
            {
                pos = transform.position;
            }
            else
            {
                pos = container.TransformPoint(rotatorLocalPosition);
            }
            // The rotator pivot sits slightly above the visual lamp center;
            // drop the marker so it points at the light itself.
            pos.y += LightVisualOffsetY;
            return pos;
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
            // lastLoggedCamera теперь внутри Resolver, локальный не нужен — синхронизируем для совместимости
            lastLoggedCamera = renderingCamera;
        }

        /// <summary>
        /// The game's periscope/binocular view: Generic camera mode driven by
        /// a PeriscopeController; the actual rendered camera is the device's
        /// PeriscopeCamera (renders into a RenderTexture in windowed mode).
        /// </summary>
        private Camera ResolveDeviceCamera()
        {
            if (gameMainCamera.IsNull())
            {
                return null;
            }
            try
            {
                if (gameMainCamera.CurrentMode == CameraMode.Generic &&
                    gameMainCamera.GenericController.IsNotNull() &&
                    gameMainCamera.GenericController.ExternalController is PeriscopeController periscopeController &&
                    periscopeController.IsNotNull() && periscopeController.Periscope.IsNotNull())
                {
                    Camera cam = periscopeController.Periscope.PeriscopeCamera;
                    if (cam.IsNotNull() && cam.gameObject.activeInHierarchy)
                    {
                        return cam;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Lighthouse] glow: device camera resolve failed: " + e.Message);
            }
            return null;
        }

        private static bool IsUtilityCamera(Camera cam)
        {
            if (cam.IsNull())
            {
                return false;
            }
            string name = cam.name;
            return name == "Loading Camera" ||
                name == "Utility GUI Camera" ||
                name.IndexOf("GUI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Camera FindScreenCamera()
        {
            Camera best = null;
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (!cam.isActiveAndEnabled || cam.orthographic || cam.targetTexture != null || IsUtilityCamera(cam))
                {
                    continue;
                }
                if (best == null || cam.depth > best.depth)
                {
                    best = cam;
                }
            }
            return best;
        }

        private static void InstallRenderHook()
        {
            // ADR-018: общий хук в CameraResolver, не дублируем
            CameraResolver.InstallOnce();
            // Для совместимости отметим локальный флаг, чтобы старый ResetStatics не висел
            renderHookInstalled = true;
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
            // Чистим локальную статику + общий резолвер (1 хук)
            if (renderHookInstalled)
            {
                try { Camera.onPreCull -= OnAnyCameraPreCull; } catch { }
                renderHookInstalled = false;
            }
            renderingCamera = null;
            // lastLoggedCamera — instance поле, чистим только на instance (не в static Reset)
            CameraResolver.ResetForReload();
        }

        private static void OnAnyCameraPreCull(Camera cam)
        {
            if (cam.orthographic || IsUtilityCamera(cam))
            {
                return;
            }
            renderingCamera = cam;
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

            bool flareActive = lensFlare.IsNotNull() && lensFlare.gameObject.activeInHierarchy;
            bool rendererEnabled = false;
            bool rendererVisible = false;
            MeshRenderer flareRenderer = lensFlare.IsNotNull() ? lensFlare.GetComponent<MeshRenderer>() : null;
            if (flareRenderer.IsNotNull())
            {
                rendererEnabled = flareRenderer.enabled;
                rendererVisible = flareRenderer.isVisible;
            }

            string flareIntensity = "?";
            if (lensFlare.IsNotNull() && flareIntensityProperty.IsNotNull())
            {
                try
                {
                    flareIntensity = ((float)flareIntensityProperty.GetValue(lensFlare, null)).ToString("F3");
                }
                catch
                {
                    flareIntensity = "read-failed";
                }
            }

            string camName = "null";
            float distance = -1f;
            float farClip = -1f;
            if (renderingCamera.IsNotNull())
            {
                camName = renderingCamera.name;
                distance = (renderingCamera.transform.position - worldPosition).magnitude;
                farClip = renderingCamera.farClipPlane;
            }

            float beamAngle = 0f;
            if (rotator.IsNotNull() && renderingCamera.IsNotNull())
            {
                Vector3 dir = (renderingCamera.transform.position - worldPosition).normalized;
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    beamAngle = Vector3.Angle(rotator.forward, flat.normalized);
                }
            }

            Debug.Log("[RadioNav][Lighthouse] glow diag: cam='" + camName + "' night=" + isNight +
                " dist=" + distance.ToString("F0") + " camFar=" + farClip.ToString("F0") +
                " beam=" + beamAngle.ToString("F0") + " flareActive=" + flareActive +
                " flareI=" + flareIntensity +
                " renderer=" + rendererEnabled + "/" + rendererVisible +
                " dot=" + (dotCanvas.IsNotNull() && dotCanvas.enabled));
        }

        private void FindSpotLight(Transform rotator)
        {
            Transform spot = FindChildRecursive(rotator, SpotLightName);
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
        /// Mimics the game's LighthouseLight flare driving: the flare is
        /// active only at night and only while the beam points toward the
        /// rendering camera; intensity follows the beam alignment squared.
        /// Uses the same formula as the game's LighthouseLight.Update:
        /// abs(dot(cameraDir, beamForward)) squared times 2.
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
                SetFlareActive(flareObject, false, "night=" + isNight + " cam=" + (renderingCamera.IsNotNull()) + " rot=" + rotator.IsNotNull());
                return;
            }

            Vector3 directionToCamera = (renderingCamera.transform.position - worldPosition).normalized;
            float alignment = Mathf.Abs(Vector3.Dot(directionToCamera, rotator.forward));
            if (alignment <= 0f)
            {
                SetFlareActive(flareObject, false, "beam not aligned (dot=" + alignment.ToString("F3") + ")");
                return;
            }

            SetFlareActive(flareObject, true, "aligned dot=" + alignment.ToString("F3"));
            if (flareIntensityProperty.IsNotNull())
            {
                flareIntensityProperty.SetValue(lensFlare, alignment * alignment * 2f, null);
            }
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
                string camName = renderingCamera.IsNotNull() ? renderingCamera.name : "null";
                float distance = renderingCamera.IsNotNull()
                    ? (renderingCamera.transform.position - GetWorldPosition()).magnitude
                    : -1f;
                Debug.Log("[RadioNav][Lighthouse] glow: flare " + (active ? "ON" : "OFF") +
                    " [" + reason + "] cam='" + camName + "' dist=" + distance.ToString("F0"));
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

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root.IsNull())
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found.IsNotNull())
                {
                    return found;
                }
            }
            return null;
        }
    }
}
