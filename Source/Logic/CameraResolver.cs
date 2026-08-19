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

        private const float WorldRadiusM = 3900f;

        /// <summary>
        /// Резолвит камеру текущего вида. Приоритет как у LighthouseBeamGlow:
        /// 1. Camera.main 2. Periscope device 3. hook 4. FindScreenCamera
        /// </summary>
        public static bool TryResolve(out Camera cam)
        {
            InstallOnce();

            if (gameMainCamera == null || !gameMainCamera.gameObject.activeInHierarchy)
                gameMainCamera = Object.FindObjectOfType<MainCamera>();

            Camera candidate = null;

            Camera main = Camera.main;
            if (main != null && main.gameObject.activeInHierarchy && !IsUtilityCamera(main))
                candidate = main;

            if (candidate == null)
                candidate = ResolveDeviceCamera();

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

        public static bool IsMapMode()
        {
            if (gameMainCamera == null) return false;
            try { return gameMainCamera.CurrentMode.ToString() == "Map"; } catch { return false; }
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
        }

        private static Camera ResolveDeviceCamera()
        {
            if (gameMainCamera == null) return null;
            try
            {
                // CameraMode enum сравниваем строкой, чтобы не зависеть от точного типа в сборке игры
                if (gameMainCamera.CurrentMode.ToString() == "Generic" &&
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

        private static bool IsUtilityCamera(Camera cam)
        {
            if (cam == null) return false;
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
