using System;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Phase0 diagnostic probe (STAR_SKY_PLAN:10, ADR-020).
    /// Logs Sun/Moon vs Stellarium, farClip, shader, water, north, LerpValue.
    /// Enabled via PlayerPrefs "RadioNav.Phase0Probe"=1 or #if DEBUG with same key.
    /// Throttled to 2s, no alloc in Update beyond reflection cache.
    /// </summary>
    public sealed class StarSkyPhase0Probe : MonoBehaviour
    {
        private const string EnableKey = "RadioNav.Phase0Probe";
        private const float IntervalSec = 2f;
        private const string LogPrefix = "[RadioNav][Phase0]";

        [Inject] private static Weather weather = null;

        private float nextLogTime;
        private bool shaderLogged;
        private IPlayerPositionProvider positionProvider;

        public static void EnsureRunning()
        {
            if (FindObjectOfType<StarSkyPhase0Probe>() != null)
                return;
            if (PlayerPrefs.GetInt(EnableKey, 0) != 1)
                return;
            var go = new GameObject("StarSkyPhase0Probe");
            go.AddComponent<StarSkyPhase0Probe>();
            DontDestroyOnLoad(go);
            Debug.Log(LogPrefix + " probe enabled (PlayerPrefs " + EnableKey + "=1)");
        }

        public static void Reset() { }

        private void Awake()
        {
            positionProvider = new GamePlayerPositionProvider();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextLogTime)
                return;
            nextLogTime = Time.unscaledTime + IntervalSec;

            if (PlayerPrefs.GetInt(EnableKey, 0) != 1)
                return;

            LogOnceShader();
            LogSunMoon();
            LogCameras();
            LogVisibility();
            LogPosition();
        }

        private void LogOnceShader()
        {
            if (shaderLogged)
                return;
            shaderLogged = true;
            bool spritesDefault = Shader.Find("Sprites/Default") != null;
            bool additive = Shader.Find("Legacy Shaders/Particles/Additive") != null;
            Debug.Log(LogPrefix + " shader Sprites/Default=" + spritesDefault + " Additive=" + additive);
            if (!spritesDefault && !additive)
                Debug.LogWarning(LogPrefix + " no suitable star shader found - StarsMesh will fallback");
        }

        private void LogSunMoon()
        {
            try
            {
                if (weather == null)
                {
                    // try late resolve via injection
                    weather = InjectionFramework.Instance?.GetInstance<Weather>();
                    if (weather == null)
                    {
                        Debug.Log(LogPrefix + " weather=null (not in gameplay yet)");
                        return;
                    }
                }

                object sky = ReadMember(weather, "Sky");
                if (sky == null)
                {
                    Debug.Log(LogPrefix + " weather.Sky=null");
                    return;
                }

                float sunZenith = ReadFloat(sky, "SunZenith");
                float sunAlt = ReadFloat(sky, "SunAltitude");
                float sunAz = ReadFloat(sky, "SunAzimuth");
                float moonAlt = ReadFloat(sky, "MoonAltitude");
                float moonAz = ReadFloat(sky, "MoonAzimuth");
                float moonZenith = ReadFloat(sky, "MoonZenith");
                float lerp = ReadFloat(sky, "LerpValue");
                bool isNight = ReadBool(sky, "IsNight");
                DateTime utc = GameTimeResolver.GetUtcNow();
                double jd = AstroMath.ToJulianDay(utc);

                // Fogginess via Atmosphere
                object atmosphere = ReadMember(sky, "Atmosphere");
                float fogginess = ReadFloat(atmosphere, "Fogginess");

                // World lat/lon if exposed
                object world = ReadMember(sky, "World");
                float worldLat = ReadFloat(world, "Latitude");
                float worldLon = ReadFloat(world, "Longitude");

                Debug.Log(LogPrefix + $" utc={utc:yyyy-MM-dd HH:mm:ss} JD={jd:F5} SunZenith={sunZenith:F2} SunAlt={sunAlt:F2} SunAz={sunAz:F1} MoonAlt={moonAlt:F2} MoonAz={moonAz:F1} MoonZen={moonZenith:F2} Lerp={lerp:F3} Fog={fogginess:F3} IsNight={isNight} WorldLat={worldLat:F2} WorldLon={worldLon:F2}");

                // Monotonic Lerp check hint
                if (sunZenith >= 86.5f && sunZenith <= 97f)
                {
                    float expected = Mathf.InverseLerp(97f, 86.5f, sunZenith);
                    Debug.Log(LogPrefix + $" Lerp check SunZenith {sunZenith:F1} -> expected {expected:F3} actual {lerp:F3} {(Mathf.Abs(expected - lerp) < 0.05f ? "OK" : "MISMATCH")}");
                }

                // Stellarium comparison hint for two reference dates (log only when near those dates)
                if ((utc.Month == 6 && utc.Day == 15) || (utc.Month == 1 && utc.Day == 10))
                {
                    Debug.Log(LogPrefix + " REFERENCE DATE - compare with Stellarium (same UTC): SunAlt/SunAz MoonAlt/MoonAz above");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " SunMoon read failed: " + e.Message);
            }
        }

        private void LogCameras()
        {
            try
            {
                Camera cam;
                bool hasMain = CameraResolver.TryResolve(out cam);
                if (hasMain && cam != null)
                {
                    float safe = CameraResolver.SafeRadius(cam);
                    Debug.Log(LogPrefix + $" cam main='{cam.name}' farClip={cam.farClipPlane:F0} safeR={safe:F0} 0.85*far={cam.farClipPlane * 0.85f:F0} WorldR=3900 {(safe < 3900f ? "CLIPPED" : "OK")}");
                }
                else
                {
                    Debug.Log(LogPrefix + " no main rendering camera resolved");
                }

                // enumerate all farClips for bridge/periscope/binocular diagnosis
                Camera[] cams = Camera.allCameras;
                for (int i = 0; i < cams.Length && i < 6; i++)
                {
                    Camera c = cams[i];
                    if (c == null || c.orthographic) continue;
                    Debug.Log(LogPrefix + $" cam[{i}] '{c.name}' active={c.isActiveAndEnabled} depth={c.depth} far={c.farClipPlane:F0} ortho={c.orthographic} rt={(c.targetTexture != null ? c.targetTexture.name : "null")}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " camera log failed: " + e.Message);
            }
        }

        private void LogVisibility()
        {
            try
            {
                float vis = SkyVisibilityResolver.GetVisibility();
                // also read raw components for diagnosis
                object sky = ReadMember(weather, "Sky");
                object atm = ReadMember(sky, "Atmosphere");
                float fog = ReadFloat(atm, "Fogginess");
                float lerp = ReadFloat(sky, "LerpValue");
                float expected = (1f - fog) * (1f - lerp);
                Debug.Log(LogPrefix + $" visibility={vis:F3} (fog={fog:F3} lerp={lerp:F3} expected={(1f - fog) * (1f - lerp):F3}) {(Mathf.Abs(vis - expected) < 0.01f ? "OK" : "MISMATCH")}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " visibility log failed: " + e.Message);
            }
        }

        private void LogPosition()
        {
            try
            {
                float lat, lon;
                bool hasPos = positionProvider.TryGetPosition(out lat, out lon);
                if (!hasPos)
                {
                    Debug.Log(LogPrefix + " player pos unavailable (menu/port)");
                    return;
                }

                // world units roundtrip check
                // GeoPositionConverter is 1/111.319888 per world unit
                float wx = lon / (float)GeoPositionConverter.DegreesPerWorldUnit;
                float wy = lat / (float)GeoPositionConverter.DegreesPerWorldUnit;
                GeoPositionConverter.ToLatLong(wx, wy, out float lat2, out float lon2);
                Debug.Log(LogPrefix + $" pos lat={lat:F4} lon={lon:F4} world=({wx:F1},{wy:F1}) roundtrip lat={lat2:F4} lon={lon2:F4} diff={(Mathf.Abs(lat - lat2) + Mathf.Abs(lon - lon2)):F6}");

                // compare with Weather World if available
                object sky = ReadMember(weather, "Sky");
                object world = ReadMember(sky, "World");
                float wLat = ReadFloat(world, "Latitude");
                float wLon = ReadFloat(world, "Longitude");
                if (world != null && (wLat != 0f || wLon != 0f))
                    Debug.Log(LogPrefix + $" compare Weather.World lat={wLat:F4} lon={wLon:F4} diffPos={(Mathf.Abs(wLat - lat) + Mathf.Abs(wLon - lon)):F4}");

                // north check: ENU north vector should align with +Z in world
                // We log Az of a star due north at alt 45 for sanity
                double jd = AstroMath.ToJulianDay(GameTimeResolver.GetUtcNow());
                double lst = AstroMath.LstDeg(jd, lon);
                // Pick Polaris approx RA 37.95deg Dec 89.26 deg -> should be alt≈lat az≈0
                AstroMath.RaDecToEnu(37.95456067 * Mathf.Deg2Rad, 89.26410985 * Mathf.Deg2Rad, lst, lat, out double alt, out double az, out _, out _, out _);
                Debug.Log(LogPrefix + $" north check Polaris alt={alt * Mathf.Rad2Deg:F1} az={az * Mathf.Rad2Deg:F1} (exp alt≈{lat:F1} az≈0) {(Mathf.Abs((float)(az * Mathf.Rad2Deg)) < 5f ? "OK" : "CHECK")}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " position log failed: " + e.Message);
            }
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(target, null);
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(target);
        }

        private static float ReadFloat(object target, string name)
        {
            object v = ReadMember(target, name);
            return v == null ? 0f : Convert.ToSingle(v);
        }

        private static bool ReadBool(object target, string name)
        {
            object v = ReadMember(target, name);
            if (v is bool b) return b;
            return v != null && Convert.ToBoolean(v);
        }
    }
}
