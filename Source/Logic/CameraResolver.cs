using System;
using UBOAT.Game.Scene.Camera;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Общий резолвер камеры для маяков и звезды (ADR-018).
    /// Вынесено из LighthouseBeamGlow.cs:361-517 чтобы не дублировать логику ResolveCamera
    /// для STAR_SKY_PLAN и маяков. Best practice — 1 подписчик onPreCull, 1 current камера.
    /// Безопасно: вызовы только на main thread, fallback на null → слой скрыт.
    /// </summary>
    public static class CameraResolver
    {
        private static Camera renderingCamera;
        private static bool renderHookInstalled;
        private static MainCamera gameMainCamera;
        private static Camera lastLoggedCamera;

        // P5.1: FindObjectOfType<MainCamera> is a whole-scene scan; TryResolve
        // runs from several LateUpdates per frame and the MainCamera is
        // null/inactive exactly during scene loads, so memo it per frame.
        private static int mainCameraProbeFrame = -1;
        // Reused buffer for FindScreenCamera (Camera.allCameras allocates a new
        // array every call).
        private static Camera[] cameraBuffer = new Camera[16];

        // Cached "Generic" enum value: Enum.ToString() allocates on Mono and
        // ResolveDeviceCamera used to format the mode every frame.
        private static bool genericModeResolved;
        private static object genericModeValue;

        private const float WorldRadiusM = 3900f;

        /// <summary>
        /// Резолвит камеру текущего вида. Приоритет:
        /// 1. Periscope/device camera (Generic mode) — она реально рисует
        ///    картинку в перископе/бинокле/UZO, позиционирование по ней
        ///    совпадает с экраном.
        /// 2. Camera.main 3. hook 4. FindScreenCamera
        /// </summary>
        public static bool TryResolve(out Camera cam)
        {
            InstallOnce();

            if (gameMainCamera == null || !gameMainCamera.gameObject.activeInHierarchy)
            {
                if (mainCameraProbeFrame != Time.frameCount)
                {
                    mainCameraProbeFrame = Time.frameCount;
                    gameMainCamera = Object.FindObjectOfType<MainCamera>();
                }
            }

            Camera candidate = null;

            // Device cameras (periscope/binocular/UZO render into a
            // RenderTexture) must win over Camera.main: the screen shows
            // their output, so WorldToScreenPoint must use them.
            ProbeDeviceCamera(gameMainCamera);
            candidate = deviceProbeCamera;

            if (candidate == null)
            {
                Camera main = Camera.main;
                if (main != null && main.gameObject.activeInHierarchy && !IsUtilityCamera(main))
                    candidate = main;
            }

            if (candidate == null && renderingCamera != null &&
                renderingCamera.gameObject.activeInHierarchy && !IsUtilityCamera(renderingCamera))
                candidate = renderingCamera;

            if (candidate == null)
                candidate = FindScreenCamera();

            renderingCamera = candidate;

            if (candidate != lastLoggedCamera)
            {
                lastLoggedCamera = candidate;
                    if (candidate == null)
                    Debug.LogWarning("[RadioNav][CameraResolver] no rendering camera found");
                else
                    Debug.Log("[RadioNav][CameraResolver] camera: '" + candidate.name + "' rt=" + (candidate.targetTexture != null));
            }

            cam = candidate;
            return cam != null;
        }

        public static float SafeRadius(Camera cam)
        {
            if (cam == null) return WorldRadiusM;
            return Mathf.Min(WorldRadiusM, cam.farClipPlane * 0.85f);
        }

        /// <summary>
        /// True when the camera is the active periscope/binocular device
        /// camera (Generic mode with a PeriscopeController external
        /// controller). Shared with LighthouseBeamGlow (ADR-018 follow-up:
        /// the device-camera check lived in two places).
        /// </summary>
        // P5.1: per-frame memo for the device-camera probe. Enum.ToString()
        // allocates on Mono and both TryResolve and IsDeviceCamera used to
        // pay it every frame; the probe result is identical within a frame.
        private static int deviceProbeFrame = -1;
        private static Camera deviceProbeCamera;

        public static bool IsDeviceCamera(MainCamera mainCamera, Camera cam)
        {
            if (mainCamera == null || cam == null)
            {
                return false;
            }
            ProbeDeviceCamera(mainCamera);
            return deviceProbeCamera != null && ReferenceEquals(deviceProbeCamera, cam);
        }

        private static void ProbeDeviceCamera(MainCamera mainCamera)
        {
            if (Time.frameCount == deviceProbeFrame)
                return;
            deviceProbeFrame = Time.frameCount;
            gameMainCamera = mainCamera;
            deviceProbeCamera = ResolveDeviceCamera();
        }

        public static void InstallOnce()
        {
            if (renderHookInstalled) return;
            renderHookInstalled = true;
            Camera.onPreCull += OnAnyCameraPreCull;
        }

        public static void ResetForReload()
        {
            if (renderHookInstalled)
            {
                Camera.onPreCull -= OnAnyCameraPreCull;
                renderHookInstalled = false;
            }
            renderingCamera = null;
            lastLoggedCamera = null;
            gameMainCamera = null;
            deviceProbeFrame = -1;
            deviceProbeCamera = null;
            mainCameraProbeFrame = -1;
            genericModeResolved = false;
            genericModeValue = null;
        }

        private static Camera ResolveDeviceCamera()
        {
            if (gameMainCamera == null) return null;
            try
            {
                // CameraMode enum сравниваем значением (кэш Enum.Parse), а не
                // строкой: Enum.ToString() аллоцирует на Mono.
                if (IsGenericMode(gameMainCamera) &&
                    gameMainCamera.GenericController.IsNotNull() &&
                    gameMainCamera.GenericController.ExternalController is UBOAT.Game.Scene.Camera.ExternalControllers.PeriscopeController pc &&
                    pc.IsNotNull() && pc.Periscope.IsNotNull())
                {
                    Camera cam = pc.Periscope.PeriscopeCamera;
                    if (cam != null && cam.gameObject.activeInHierarchy)
                        return cam;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][CameraResolver] device camera resolve failed: " + e.Message);
            }
            return null;
        }

        private static bool IsGenericMode(MainCamera mainCamera)
        {
            if (mainCamera == null)
                return false;
            if (!genericModeResolved)
            {
                genericModeResolved = true;
                Type modeType = mainCamera.CurrentMode.GetType();
                if (modeType.IsEnum)
                {
                    try { genericModeValue = Enum.Parse(modeType, "Generic"); }
                    catch (ArgumentException) { genericModeValue = null; }
                }
            }
            return genericModeValue != null && genericModeValue.Equals(mainCamera.CurrentMode);
        }

        private static bool IsUtilityCamera(Camera cam)        {
            if (cam == null) return false;
            string name = cam.name;
            return name == "Loading Camera" ||
                   name == "Utility GUI Camera" ||
                   name.IndexOf("GUI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Camera FindScreenCamera()
        {
            if (cameraBuffer == null || cameraBuffer.Length == 0)
                cameraBuffer = new Camera[16];

            int count = Camera.GetAllCameras(cameraBuffer);
            if (count >= cameraBuffer.Length)
            {
                cameraBuffer = new Camera[count + 4];
                count = Camera.GetAllCameras(cameraBuffer);
            }

            Camera best = null;
            for (int i = 0; i < count; i++)
            {
                Camera cam = cameraBuffer[i];
                if (cam == null || !cam.isActiveAndEnabled || cam.orthographic || cam.targetTexture != null || IsUtilityCamera(cam))
                    continue;
                if (best == null || cam.depth > best.depth)
                    best = cam;
            }
            return best;
        }

        private static void OnAnyCameraPreCull(Camera cam)
        {
            if (cam.orthographic || IsUtilityCamera(cam)) return;
            renderingCamera = cam;
        }
    }
}
