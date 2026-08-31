using System;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Verbose glow diagnostics for LighthouseBeamGlow (P5.4 LBG-3). The
    /// per-lighthouse controller snapshots its render state into a
    /// GlowSnapshot and hands it off here; the 2-second throttled dump is
    /// built entirely from that snapshot plus the pure visual-model math.
    /// Pure diagnostic output — no rendering state is touched, so an error
    /// here can only degrade the log, never the glow itself.
    /// </summary>
    public static class LighthouseDiagnostics
    {
        /// <summary>
        /// Immutable copy of the fields Log() needs. Built by the caller each
        /// time it would log (throttled to ~2 s), so no live references are
        /// held between frames.
        /// </summary>
        public struct GlowSnapshot
        {
            public Component LensFlare;
            public PropertyInfo FlareIntensityProperty;
            public Camera RenderingCamera;
            public Transform Rotator;
            public Transform Container;
            public Light SpotLight;
            public MainCamera GameMainCamera;
            public Canvas DotCanvas;
            public bool IsNight;
            public float LastIVisual;
            public float LastDotSize;
            // Owner's transform.position.y (the glow controller GameObject).
            public float ObjectY;
            // Layer alphas already scaled by LastIVisual (owner's constants).
            public float CoreAlpha;
            public float HaloAlpha;
            public float StreakAlpha;
        }

        /// <summary>
        /// Emits the 2-second "glow diag" line. Mirrors the pre-refactor
        /// output byte-for-byte (same field order, same formatting) so the
        /// PlayerPrefs-gated verbose log is unchanged.
        /// </summary>
        public static void Log(GlowSnapshot s, Vector3 worldPosition)
        {
            bool flareActive = s.LensFlare.IsNotNull() && s.LensFlare.gameObject.activeInHierarchy;
            bool rendererEnabled = false;
            bool rendererVisible = false;
            MeshRenderer flareRenderer = s.LensFlare.IsNotNull() ? s.LensFlare.GetComponent<MeshRenderer>() : null;
            if (flareRenderer.IsNotNull())
            {
                rendererEnabled = flareRenderer.enabled;
                rendererVisible = flareRenderer.isVisible;
            }

            string flareIntensity = "?";
            if (s.LensFlare.IsNotNull() && s.FlareIntensityProperty.IsNotNull())
            {
                try
                {
                    flareIntensity = ((float)s.FlareIntensityProperty.GetValue(s.LensFlare, null)).ToString("F3");
                }
                catch
                {
                    flareIntensity = "read-failed";
                }
            }

            string camName = "null";
            float distance = -1f;
            float farClip = -1f;
            if (s.RenderingCamera.IsNotNull())
            {
                camName = s.RenderingCamera.name;
                distance = (s.RenderingCamera.transform.position - worldPosition).magnitude;
                farClip = s.RenderingCamera.farClipPlane;
            }

            float beamAngle = 0f;
            if (s.Rotator.IsNotNull() && s.RenderingCamera.IsNotNull())
            {
                Vector3 dir = (s.RenderingCamera.transform.position - worldPosition).normalized;
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    beamAngle = Vector3.Angle(s.Rotator.forward, flat.normalized);
                }
            }

            // --- Heights requested: beacon / light / object / spot / distant light ---
            float hBeacon = s.Container.IsNotNull() ? s.Container.position.y : float.NaN;
            float hLamp = s.Rotator.IsNotNull() ? s.Rotator.position.y : float.NaN;
            float hObj = s.ObjectY;
            float hSpotLight = s.SpotLight.IsNotNull() ? s.SpotLight.transform.position.y : float.NaN;
            float hFarLight = worldPosition.y;
            float hCam = s.RenderingCamera.IsNotNull() ? s.RenderingCamera.transform.position.y : float.NaN;
            // Model breakdown for verbose diagnostics
            float fog = SkyVisibilityResolver.GetFogginess();
            double atten = LighthouseVisualModel.DistanceAttenuation(distance);
            double trans = LighthouseVisualModel.Transmittance(distance, fog);
            double hotDbg = 0;
            try
            {
                if (s.Rotator.IsNotNull() && s.RenderingCamera.IsNotNull())
                {
                    Vector3 toCamDbg = (s.RenderingCamera.transform.position - worldPosition).normalized;
                    float hotAlignDbg = Mathf.Abs(Vector3.Dot(toCamDbg, s.Rotator.forward));
                    hotDbg = LighthouseVisualModel.BeamIntensityFactor(hotAlignDbg);
                }
            }
            catch { }
            float spotI = s.SpotLight.IsNotNull() ? s.SpotLight.intensity : float.NaN;
            Vector3 rotEuler = s.Rotator.IsNotNull() ? s.Rotator.eulerAngles : Vector3.zero;

            // Device view diagnostics: mode, external controller, periscope
            // fullscreen/windowed state and camera enabled flags.
            string devInfo = "mode=?";
            try
            {
                MainCamera mc = s.GameMainCamera;
                if (mc.IsNull())
                {
                    mc = Object.FindObjectOfType<MainCamera>();
                }
                if (mc.IsNotNull())
                {
                    devInfo = "mode=" + mc.CurrentMode;
                    if (mc.CurrentMode.ToString() == "Generic" &&
                        mc.GenericController.IsNotNull() &&
                        mc.GenericController.ExternalController is PeriscopeController pc &&
                        pc.IsNotNull() && pc.Periscope.IsNotNull())
                    {
                        devInfo += " dev=Periscope fullScreen=" + pc.Periscope.FullScreenView;
                        Camera devCam = pc.Periscope.PeriscopeCamera;
                        devInfo += " devEn=" + (devCam != null ? devCam.enabled.ToString() : "null") +
                            " devRT=" + (devCam != null && devCam.targetTexture != null ? "yes" : "no");
                    }
                    devInfo += " mainEn=" + (mc.Camera != null ? mc.Camera.enabled.ToString() : "?") +
                        " intEn=" + GetInteriorCameraEnabled(mc);
                }
            }
            catch (Exception e)
            {
                devInfo = "mode=err:" + e.Message;
            }

            Debug.Log("[RadioNav][Lighthouse] glow diag: cam='" + camName + "' night=" + s.IsNight +
                " dist=" + distance.ToString("F0") + " camFar=" + farClip.ToString("F0") +
                " beam=" + beamAngle.ToString("F0") + " fog=" + fog.ToString("F2") +
                " A=" + atten.ToString("F3") + " T=" + trans.ToString("F3") + " beam=" + hotDbg.ToString("F2") +
                " Ivis=" + s.LastIVisual.ToString("F3") + " sizePx=" + s.LastDotSize.ToString("F1") +
                " flareActive=" + flareActive +
                " flareI=" + flareIntensity +
                " renderer=" + rendererEnabled + "/" + rendererVisible +
                " dot=" + (s.DotCanvas.IsNotNull() && s.DotCanvas.enabled) +
                " spotI=" + spotI.ToString("F2") +
                " alphas core=" + s.CoreAlpha.ToString("F2") + " halo=" + s.HaloAlpha.ToString("F2") + " streak=" + s.StreakAlpha.ToString("F2") +
                " rotY=" + rotEuler.y.ToString("F1") +
                " " + devInfo +
                " heights: beaconY=" + hBeacon.ToString("F1") +
                " lampY=" + hLamp.ToString("F1") +
                " objY=" + hObj.ToString("F1") +
                " spotY=" + hSpotLight.ToString("F1") +
                " farY=" + hFarLight.ToString("F1") +
                " camY=" + hCam.ToString("F1"));
        }

        /// <summary>
        /// Reads MainCamera.InteriorCamera via reflection (the game's Roslyn
        /// mod compiler does not expose the property type at compile time)
        /// and returns its enabled state as a string. Shared by the glow diag
        /// and the screenPt diag in LighthouseBeamGlow.
        /// </summary>
        public static string GetInteriorCameraEnabled(MainCamera mc)
        {
            try
            {
                PropertyInfo prop = typeof(MainCamera).GetProperty("InteriorCamera",
                    BindingFlags.Public | BindingFlags.Instance);
                object interior = prop != null ? prop.GetValue(mc, null) : null;
                if (interior == null)
                {
                    return "null";
                }
                return ((Behaviour)interior).enabled.ToString();
            }
            catch (Exception e)
            {
                return "err:" + e.Message;
            }
        }
    }
}
