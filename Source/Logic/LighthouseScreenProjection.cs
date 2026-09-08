using System;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Screen-space projection of the lighthouse position for the dot
    /// overlay (P5.4 LBG slice): the game's non-jittered external-view
    /// projection first (correct in binoculars/UZO where the plain
    /// projection is stale), the plain rendering-camera projection as the
    /// in-view fallback, and the throttled screenPt/binocular diagnostics.
    /// Extracted from LighthouseBeamGlow (SRP: projection plumbing is not
    /// the visibility controller's logic). Stateless — the caller supplies
    /// the cameras and the throttle inputs.
    /// </summary>
    internal static class LighthouseScreenProjection
    {
        /// <summary>
        /// Projects the world position to screen space. Priority:
        /// 1. MainCamera.WorldToScreenPointNonJitteredExternalView —
        ///    matches what the device views actually render.
        /// 2. The rendering camera's plain WorldToScreenPoint (current
        ///    projection matrix; z&lt;=0 = behind the camera).
        /// 3. Fallback: plain projection with z&lt;=0 (hides the dot).
        /// </summary>
        public static Vector3 Project(MainCamera gameMainCamera, Camera renderingCamera,
            Vector3 worldPos, bool throttled)
        {
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
                    if (TryExternalViewProjection(mc, renderingCamera, worldPos, throttled, out projected))
                        return projected;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Lighthouse] glow: external view screen point failed: " + e.Message);
            }
            return FallbackProjection(renderingCamera, worldPos, throttled);
        }

        private static bool TryExternalViewProjection(MainCamera mc, Camera renderingCamera,
            Vector3 worldPos, bool throttled, out Vector3 projected)
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

        private static Vector3 FallbackProjection(Camera renderingCamera, Vector3 worldPos, bool throttled)
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
        private static void LogBinocularDiag(MainCamera mc, Vector4 v)
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
    }
}
