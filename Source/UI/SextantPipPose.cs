using System;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-frame PIP camera pose behind SextantController (P5.4 slice 4b,
    /// SRP): main-camera tracking, SRP state sync and forced render live
    /// here with explicit inputs; the controller owns open/close/input and
    /// passes its factory, arc and zoom divisor. Creation still lives in
    /// SextantPipFactory. Moved verbatim (pose order, log strings).
    /// </summary>
    public static class SextantPipPose
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        public static void Update(SextantPipFactory factory, double arcAngle,
            float fovDivisor, ref Camera viewCamera, Action manualRender)
        {
            if (factory.LeftCamera == null || factory.RightCamera == null)
                return;
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
                return;

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;
            factory.LeftCamera.transform.position = mt.position;
            factory.LeftCamera.transform.rotation = Quaternion.Euler(0f, mainEuler.y, 0f);
            factory.RightCamera.transform.position = mt.position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            factory.RightCamera.transform.rotation = Quaternion.Euler(-(float)arcAngle, mainEuler.y, 0f);

            SyncCameraState(factory.LeftCamera, viewCamera, fovDivisor);
            SyncCameraState(factory.RightCamera, viewCamera, fovDivisor);
            factory.RefreshMirroredState();

            // The game's SRP renders attached cameras once and never
            // refreshes them with the updated pose; force the render so the
            // right RT follows the alidade.
            if (SextantVectorLog.Enabled && manualRender != null)
                manualRender();
            try
            {
                factory.LeftCamera.Render();
                factory.RightCamera.Render();
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " MANUALRENDER failed: " + e.Message);
            }
        }

        public static void SyncCameraState(Camera cam, Camera source, float fovDivisor)
        {
            cam.fieldOfView = source.fieldOfView / fovDivisor;
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
            catch (Exception e)
            {
                // Culling/sun-layer divergence must surface once instead of
                // silently rendering a wrong PIP stack every pose update.
                if (WarnOnce.ShouldLog("SextantController.SyncCameraState"))
                    Debug.LogWarning("[RadioNav][Sextant] SyncCameraState failed: " + e.Message);
            }
        }

        public static Vector3 Forward(SextantPipFactory factory, Camera viewCamera,
            MainCamera gameMainCamera, double arcAngle)
        {
            if (factory.RightCamera != null)
                return factory.RightCamera.transform.forward;
            float yaw = 0f;
            if (viewCamera != null)
                yaw = viewCamera.transform.eulerAngles.y;
            else if (gameMainCamera != null)
                yaw = gameMainCamera.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(-(float)arcAngle, yaw, 0f);
            return rot * Vector3.forward;
        }
    }
}
