using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Environment;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// World-illumination channel of the lighthouse glow (P5.4 LBG slice):
    /// the Spot Light boost that makes the rotating beam illuminate the
    /// surrounding water at night, driven by the physical A(r)·T(r)
    /// attenuation (no display gamma, no cones — the rotating cone is the
    /// spotlight transform itself), plus the day/night gate (the game's
    /// own Weather.IsDarkWeather with a sun-position heuristic fallback).
    /// Extracted from LighthouseBeamGlow (SRP: light driving is not the
    /// screen-space visibility controller's logic). One instance per
    /// lighthouse; the sun is shared static state.
    /// </summary>
    internal sealed class LighthouseSpotLight
    {
        private const string SpotLightName = "Spot Light";
        private const float MaxLightRangeM = 2500f;
        private const float MinLightIntensity = 2.5f;
        private const float DaySunIntensityThreshold = 0.6f;

        [Inject] private static Weather weather = null;

        private static Light sun;

        private Light spotLight;
        private float lastLoggedIntensity = float.NaN;

        /// <summary>True when the current time of day reads as night.</summary>
        public bool IsNight { get; private set; }

        /// <summary>The driven Spot Light (null when the prefab lacks one).</summary>
        public Light Spot
        {
            get { return spotLight; }
        }

        /// <summary>
        /// Finds the "Spot Light" child, raises its range/intensity floors
        /// and auto-flips a prefab light that points opposite the rotator.
        /// </summary>
        public void Initialize(Transform rotator)
        {
            FindSun();
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
        /// Day/night gate: the game's own Weather.IsDarkWeather (the same
        /// signal as the game's lighthouse effect), with a sun-position
        /// heuristic as fallback.
        /// </summary>
        public void UpdateDayNight()
        {
            if (weather.IsNotNull())
            {
                IsNight = weather.IsDarkWeather;
                return;
            }

            bool sunDown = sun.IsNull() || sun.transform.forward.y > -0.05f || sun.intensity < DaySunIntensityThreshold;
            IsNight = sunDown;
        }

        /// <summary>
        /// Drives the spot intensity from the physical attenuation. Without
        /// this the beam floodlights terrain like a stadium even when the
        /// source on screen has shrunk to a 10px point. Off during daytime.
        /// </summary>
        public void Update(Vector3 worldPosition, Camera renderingCamera,
            bool verbose, float nextDiagTime)
        {
            if (spotLight.IsNull())
            {
                return;
            }
            if (!IsNight || renderingCamera.IsNull())
            {
                if (spotLight.intensity != 0f)
                    Debug.Log("[RadioNav][Lighthouse] spot OFF (night=" + IsNight + " camNull=" + renderingCamera.IsNull() + ") was=" + spotLight.intensity.ToString("F2"));
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
            bool significant = float.IsNaN(lastLoggedIntensity) || Mathf.Abs(newIntensity - lastLoggedIntensity) > 0.15f;
            if (significant && verbose && Time.unscaledTime >= nextDiagTime)
            {
                float hSpot = spotLight.transform.position.y;
                float hFar = worldPosition.y;
                Debug.Log("[RadioNav][Lighthouse] spot: dist=" + distance.ToString("F0") +
                    " A=" + atten.ToString("F3") + " T=" + trans.ToString("F3") + " fog=" + fog.ToString("F2") +
                    " worldI=" + worldI.ToString("F3") + " -> intensity " + spotLight.intensity.ToString("F2") + "->" + newIntensity.ToString("F2") +
                    " heights spotY=" + hSpot.ToString("F1") + " farY=" + hFar.ToString("F1"));
                lastLoggedIntensity = newIntensity;
            }
            spotLight.intensity = newIntensity;
        }

        private static void FindSun()
        {
            if (sun.IsNotNull())
            {
                return;
            }
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
    }
}
