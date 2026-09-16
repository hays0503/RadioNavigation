using System;
using System.Collections.Generic;
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

        // Per-camera caches for the per-frame pass-mask sync (P5.4 slice 4b):
        // the AdditionalCameraData component and the last applied culling mask.
        private static readonly Dictionary<Camera, UBOAT.Game.Scene.Rendering.AdditionalCameraData> addCache =
            new Dictionary<Camera, UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
        private static readonly Dictionary<Camera, int> maskCache =
            new Dictionary<Camera, int>();

        // Test-aim override (MCP bridge /sextant/aim): replaces the camera yaw
        // while active so an agent can point the alidade at a body without
        // mouse input. Gated by PlayerPrefs RadioNav.TestAim; cleared on close.
        private static float testAimYawDeg;
        private static bool testAimActive;

        /// <summary>Test override: both PIP cameras yaw to the given azimuth.</summary>
        public static void SetTestAim(float yawDeg)
        {
            testAimYawDeg = yawDeg;
            testAimActive = true;
        }

        /// <summary>Drops the test override (sextant close/teardown).</summary>
        public static void ClearTestAim()
        {
            testAimActive = false;
        }

        public static void Update(SextantPipFactory factory, double arcAngle,
            float fovDivisor, ref Camera viewCamera, Action manualRender)
        {
            if (factory.LeftCamera == null || factory.RightCamera == null)
                return;
            if (!CameraResolver.TryResolve(out viewCamera) || viewCamera == null)
                return;

            Transform mt = viewCamera.transform;
            Vector3 mainEuler = mt.eulerAngles;
            float yaw = testAimActive ? testAimYawDeg : mainEuler.y;
            factory.LeftCamera.transform.position = mt.position;
            factory.LeftCamera.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            factory.RightCamera.transform.position = mt.position;
            // Positive Euler X pitches DOWN in Unity; the alidade looks UP.
            factory.RightCamera.transform.rotation = Quaternion.Euler(-(float)arcAngle, yaw, 0f);

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
            int mask = source.cullingMask | SunDiscRenderer.SunDiscLayerMask;
            cam.cullingMask = mask;

            // Per-frame hot path: cache the AdditionalCameraData per camera and
            // skip the pass walk entirely while the mask is unchanged. Pass
            // member lookups go through TypeMemberCache (no per-frame
            // GetProperty/GetField, no string key allocation).
            if (maskCache.TryGetValue(cam, out int lastMask) && lastMask == mask)
                return;
            maskCache[cam] = mask;

            try
            {
                UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd;
                if (!addCache.TryGetValue(cam, out pipAdd) || pipAdd == null)
                {
                    pipAdd = cam.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                    addCache[cam] = pipAdd;
                }
                if (pipAdd == null)
                    return;

                if (!(pipAdd.Passes is Array passes))
                    return;

                for (int i = 0; i < passes.Length; i++)
                {
                    object p = passes.GetValue(i);
                    if (p == null)
                        continue;
                    ApplyPassMask(p, mask);
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

        /// <summary>Writes the culling mask onto one SRP pass (property or field).</summary>
        private static void ApplyPassMask(object pass, int mask)
        {
            Type passType = pass.GetType();
            MemberInfo member = TypeMemberCache.GetInstanceMember(passType, "CullingMask");
            if (member is PropertyInfo property)
            {
                if (property.CanWrite)
                {
                    property.SetValue(pass, mask);
                    return;
                }
            }
            else if (member is FieldInfo field)
            {
                field.SetValue(pass, mask);
                return;
            }

            MemberInfo fallback = TypeMemberCache.GetInstanceMember(passType, "cullingMask");
            if (fallback is FieldInfo fallbackField)
                fallbackField.SetValue(pass, mask);
        }

        /// <summary>
        /// Drops per-camera caches (PIP cameras are recreated per open).
        /// Called from the controller's teardown.
        /// </summary>
        public static void Reset()
        {
            addCache.Clear();
            maskCache.Clear();
        }

        public static Vector3 Forward(SextantPipFactory factory, Camera viewCamera,
            MainCamera gameMainCamera, double arcAngle)
        {
            if (factory.RightCamera != null)
                return factory.RightCamera.transform.forward;
            float yaw = 0f;
            if (testAimActive)
                yaw = testAimYawDeg;
            else if (viewCamera != null)
                yaw = viewCamera.transform.eulerAngles.y;
            else if (gameMainCamera != null)
                yaw = gameMainCamera.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(-(float)arcAngle, yaw, 0f);
            return rot * Vector3.forward;
        }
    }
}
