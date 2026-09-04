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
            var flare = ReadFlareState(s);
            var cam = ReadCameraState(s, worldPosition);
            float beamAngle = ComputeBeamAngle(s, worldPosition);
            var heights = ReadHeights(s, worldPosition);
            var model = ComputeModelBreakdown(s, worldPosition, cam.Distance);
            string devInfo = ReadDeviceInfo(s);

            Debug.Log("[RadioNav][Lighthouse] glow diag: cam='" + cam.CamName + "' night=" + s.IsNight +
                " dist=" + cam.Distance.ToString("F0") + " camFar=" + cam.FarClip.ToString("F0") +
                " beam=" + beamAngle.ToString("F0") + " fog=" + model.Fog.ToString("F2") +
                " A=" + model.Attenuation.ToString("F3") + " T=" + model.Transmittance.ToString("F3") + " beam=" + model.HotSpot.ToString("F2") +
                " Ivis=" + s.LastIVisual.ToString("F3") + " sizePx=" + s.LastDotSize.ToString("F1") +
                " flareActive=" + flare.Active +
                " flareI=" + flare.Intensity +
                " renderer=" + flare.RendererEnabled + "/" + flare.RendererVisible +
                " dot=" + (s.DotCanvas.IsNotNull() && s.DotCanvas.enabled) +
                " spotI=" + heights.SpotIntensity.ToString("F2") +
                " alphas core=" + s.CoreAlpha.ToString("F2") + " halo=" + s.HaloAlpha.ToString("F2") + " streak=" + s.StreakAlpha.ToString("F2") +
                " rotY=" + heights.RotatorEuler.y.ToString("F1") +
                " " + devInfo +
                " heights: beaconY=" + heights.Beacon.ToString("F1") +
                " lampY=" + heights.Lamp.ToString("F1") +
                " objY=" + heights.Object.ToString("F1") +
                " spotY=" + heights.SpotLight.ToString("F1") +
                " farY=" + heights.FarLight.ToString("F1") +
                " camY=" + heights.Camera.ToString("F1"));
        }

        private struct FlareState
        {
            public bool Active;
            public bool RendererEnabled;
            public bool RendererVisible;
            public string Intensity;
        }

        private static FlareState ReadFlareState(GlowSnapshot s)
        {
            var state = new FlareState { Intensity = "?" };
            if (s.LensFlare.IsNull())
                return state;

            state.Active = s.LensFlare.gameObject.activeInHierarchy;
            MeshRenderer flareRenderer = s.LensFlare.GetComponent<MeshRenderer>();
            if (flareRenderer.IsNotNull())
            {
                state.RendererEnabled = flareRenderer.enabled;
                state.RendererVisible = flareRenderer.isVisible;
            }
            if (s.FlareIntensityProperty.IsNotNull())
            {
                try
                {
                    state.Intensity = ((float)s.FlareIntensityProperty.GetValue(s.LensFlare, null)).ToString("F3");
                }
                catch (Exception e)
                {
                    state.Intensity = "read-failed";
                    if (WarnOnce.ShouldLog("LighthouseDiagnostics.FlareIntensity"))
                        Debug.LogWarning("[RadioNav][Lighthouse] flare intensity read failed: " + e.Message);
                }
            }
            return state;
        }

        private struct CameraState
        {
            public string CamName;
            public float Distance;
            public float FarClip;
        }

        private static CameraState ReadCameraState(GlowSnapshot s, Vector3 worldPosition)
        {
            var state = new CameraState { CamName = "null", Distance = -1f, FarClip = -1f };
            if (s.RenderingCamera.IsNull())
                return state;

            state.CamName = s.RenderingCamera.name;
            state.Distance = (s.RenderingCamera.transform.position - worldPosition).magnitude;
            state.FarClip = s.RenderingCamera.farClipPlane;
            return state;
        }

        private static float ComputeBeamAngle(GlowSnapshot s, Vector3 worldPosition)
        {
            if (s.Rotator.IsNull() || s.RenderingCamera.IsNull())
                return 0f;
            Vector3 dir = (s.RenderingCamera.transform.position - worldPosition).normalized;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude <= 0.0001f)
                return 0f;
            return Vector3.Angle(s.Rotator.forward, flat.normalized);
        }

        private struct HeightsState
        {
            public float Beacon;
            public float Lamp;
            public float Object;
            public float SpotLight;
            public float FarLight;
            public float Camera;
            public float SpotIntensity;
            public Vector3 RotatorEuler;
        }

        private static HeightsState ReadHeights(GlowSnapshot s, Vector3 worldPosition)
        {
            var h = new HeightsState
            {
                Beacon = s.Container.IsNotNull() ? s.Container.position.y : float.NaN,
                Lamp = s.Rotator.IsNotNull() ? s.Rotator.position.y : float.NaN,
                Object = s.ObjectY,
                SpotLight = s.SpotLight.IsNotNull() ? s.SpotLight.transform.position.y : float.NaN,
                FarLight = worldPosition.y,
                Camera = s.RenderingCamera.IsNotNull() ? s.RenderingCamera.transform.position.y : float.NaN,
                SpotIntensity = s.SpotLight.IsNotNull() ? s.SpotLight.intensity : float.NaN,
                RotatorEuler = s.Rotator.IsNotNull() ? s.Rotator.eulerAngles : Vector3.zero,
            };
            return h;
        }

        private struct ModelBreakdown
        {
            public float Fog;
            public double Attenuation;
            public double Transmittance;
            public double HotSpot;
        }

        private static ModelBreakdown ComputeModelBreakdown(GlowSnapshot s, Vector3 worldPosition, float distance)
        {
            float fog = SkyVisibilityResolver.GetFogginess();
            var m = new ModelBreakdown
            {
                Fog = fog,
                Attenuation = LighthouseVisualModel.DistanceAttenuation(distance),
                Transmittance = LighthouseVisualModel.Transmittance(distance, fog),
            };
            try
            {
                if (s.Rotator.IsNotNull() && s.RenderingCamera.IsNotNull())
                {
                    Vector3 toCamDbg = (s.RenderingCamera.transform.position - worldPosition).normalized;
                    float hotAlignDbg = Mathf.Abs(Vector3.Dot(toCamDbg, s.Rotator.forward));
                    m.HotSpot = LighthouseVisualModel.BeamIntensityFactor(hotAlignDbg);
                }
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("LighthouseDiagnostics.HotSpot"))
                    Debug.LogWarning("[RadioNav][Lighthouse] hotspot compute failed: " + e.Message);
            }
            return m;
        }

        private static string ReadDeviceInfo(GlowSnapshot s)
        {
            try
            {
                MainCamera mc = s.GameMainCamera;
                if (mc.IsNull())
                {
                    mc = Object.FindObjectOfType<MainCamera>();
                }
                if (mc.IsNull())
                    return "mode=?";
                string info = "mode=" + mc.CurrentMode;
                if (mc.CurrentMode.ToString() == "Generic" &&
                    mc.GenericController.IsNotNull() &&
                    mc.GenericController.ExternalController is PeriscopeController pc &&
                    pc.IsNotNull() && pc.Periscope.IsNotNull())
                {
                    info += " dev=Periscope fullScreen=" + pc.Periscope.FullScreenView;
                    Camera devCam = pc.Periscope.PeriscopeCamera;
                    info += " devEn=" + (devCam != null ? devCam.enabled.ToString() : "null") +
                        " devRT=" + (devCam != null && devCam.targetTexture != null ? "yes" : "no");
                }
                info += " mainEn=" + (mc.Camera != null ? mc.Camera.enabled.ToString() : "?") +
                    " intEn=" + GetInteriorCameraEnabled(mc);
                return info;
            }
            catch (Exception e)
            {
                return "mode=err:" + e.Message;
            }
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
