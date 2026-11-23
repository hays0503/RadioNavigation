using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using TMPro;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Environment.Terrain;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live cross-check of the wind row (map label).
    public static class WindLabelLiveFixture
    {
        private static readonly FieldInfo LabelField = typeof(MapUI).GetField("windSpeedLabel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeWeatherField = typeof(MapUI).GetField("gameEnvironment",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeWindCacheField = typeof(MapUI).GetField("lastDisplayedWindSpeed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeDepthLabelField = typeof(MapUI).GetField("depthLabel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CacheKnField = typeof(UBOAT.Game.Core.UnitsUtility).GetField("cache_kn",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo CacheKmhField = typeof(UBOAT.Game.Core.UnitsUtility).GetField("cache_kmh",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo LanguageField = typeof(Locale).GetField("language",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StringsField = typeof(Locale).GetField("strings",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static Modifier calmModifier;
        private static bool savedLocaleState;
        private static string origLanguage;
        private static string origKnots;
        private static string origKmh;
        private static Units origUnits;

        /// <summary>Bridge numbers plus every wind-speed label with its parsed text and bounds.</summary>
        public static string Probe()
        {
            var framework = InjectionFramework.Instance;
            if (framework == null)
                return "missing";
            var ship = framework.GetInstance<PlayerShip>();
            var time = framework.GetInstance<GameTime>();
            var text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture,
                "depth={0:R};velocity={1:R};heading={2:R};docked={3};gmt={4:O}",
                ship ? ship.Depth : float.NaN, ship ? ship.AverageVelocity : float.NaN,
                ship ? ship.transform.eulerAngles.y : float.NaN,
                ship != null && ship.Docked, time ? time.GmtDateTime : default(DateTime));
            AppendState(text, framework, ship, time);
            if (CurrentRuntimeAdapter.TrySampleWind(out Vector3 wind, out float knots))
                text.AppendFormat(CultureInfo.InvariantCulture,
                    ";wind={0:R},{1:R};knots={2:R};bearingFrom={3:R};bearingTo={4:R}",
                    wind.x, wind.z, knots, WindCompass.BearingFrom(wind.x, wind.z),
                    (WindCompass.BearingFrom(wind.x, wind.z) + 180f) % 360f);
            else
                text.AppendFormat(CultureInfo.InvariantCulture, ";wind=0,0;knots={0:R}", knots);
            AppendMaps(text);
            AppendLabels(text);
            return text.ToString();
        }

        private static void AppendState(StringBuilder text, InjectionFramework framework, PlayerShip ship, GameTime time)
        {
            var weather = NativeWeatherField?.GetValue(null) as Weather;
            var settings = framework.GetInstance<UserSettings>();
            var loc = framework.GetInstance<Locale>();
            text.AppendFormat(CultureInfo.InvariantCulture,
                ";frame={0};ticks={1};tc={2:R};nativeWeather={3};nativeKnots={4:R};units={5};lang={6}",
                Time.frameCount, time ? time.StoryTicks : 0L, time ? (float)time.TimeScale.Value : float.NaN,
                weather ? weather.GetInstanceID() : 0, weather ? weather.TotalWindSpeed : float.NaN,
                settings != null ? settings.GameplaySettings.units.ToString() : "?",
                loc != null ? (loc.Language ?? "?") : "?");
            if (!ship || !ship.RigidBody)
                return;
            Vector3 vector = ship.RigidBody.velocity;
            text.AppendFormat(CultureInfo.InvariantCulture, ";ground={0:R},{1:R};targetSpeed={2:R}",
                vector.x, vector.z, ship.TargetVelocity);
            var sandbox = Sandbox.Instance;
            if (sandbox == null || !weather || weather.Sea == null)
                return;
            var depth = weather.Sea.VerticalOffset
                - WorldMaps.ElevationMap.GetAtWorldSpace(sandbox.SceneToEncounterSpace(ship.RigidBody.position));
            text.AppendFormat(CultureInfo.InvariantCulture, ";seaDepthAtBoatRasterM={0:R}", depth);
        }

        private static void AppendMaps(StringBuilder text)
        {
            foreach (var map in Resources.FindObjectsOfTypeAll<MapUI>())
            {
                if (!map)
                    continue;
                var label = LabelField?.GetValue(map) as TextMeshProUGUI;
                string parsed = label ? label.GetParsedText() : "";
                string shown = string.IsNullOrEmpty(parsed) ? (label ? label.text : "<no-label>") : parsed;
                text.AppendFormat(CultureInfo.InvariantCulture, ";map={0}:{1}:{2}",
                    map.GetInstanceID(), map.isActiveAndEnabled ? "on" : "off", shown);
                text.AppendFormat(CultureInfo.InvariantCulture, ";mapRaw={0}:{1};mapParsed={0}:{2}",
                    map.GetInstanceID(), label ? label.text : "", parsed);
                var mapDepth = NativeDepthLabelField?.GetValue(map) as TextMeshProUGUI;
                text.AppendFormat(CultureInfo.InvariantCulture, ";mapNative={0}:{1}:{2}",
                    map.GetInstanceID(), NativeWindCacheField?.GetValue(map), mapDepth ? mapDepth.text : "");
            }
        }

        private static void AppendLabels(StringBuilder text)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (!candidate || !candidate.isActiveAndEnabled)
                    continue;
                string shown = candidate.GetParsedText();
                if (string.IsNullOrEmpty(shown))
                    shown = candidate.text;
                if (!HasWind(shown))
                    continue;
                text.Append(";tmp=").Append(shown).Append('@')
                    .Append(ScreenPoint(candidate.rectTransform)).Append(':')
                    .Append(Chain(candidate.transform));
            }
        }

        /// <summary>Every Weather instance and the speed each one reports.</summary>
        public static string Weathers()
        {
            var text = new StringBuilder();
            foreach (var weather in Resources.FindObjectsOfTypeAll<Weather>())
            {
                if (!weather)
                    continue;
                text.AppendFormat(CultureInfo.InvariantCulture, "id={0};active={1};scene={2};speed={3:R};sea={4}\n",
                    weather.GetInstanceID(), weather.isActiveAndEnabled,
                    weather.gameObject.scene.IsValid(), weather.TotalWindSpeed,
                    weather.Sea != null);
            }
            var framework = InjectionFramework.Instance;
            var injected = framework?.GetInstance<Weather>();
            text.Append("injected=").Append(injected ? injected.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "null");
            return text.ToString();
        }

        /// <summary>Every descendant text and geometry of the wind row and container.</summary>
        public static string Panel()
        {
            var text = new StringBuilder();
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (!transform || transform.name != "Wind speed Icon" || !transform.gameObject.scene.IsValid())
                    continue;
                text.Append("group=").Append(transform.name).Append('@')
                    .Append(ScreenPoint(transform as RectTransform)).Append(':')
                    .Append(ScreenBounds(transform as RectTransform)).Append(':')
                    .Append(Chain(transform)).Append('\n');
                if (transform.parent is RectTransform parentRect)
                    text.Append("container=").Append(parentRect.name).Append(':')
                        .Append(ScreenBounds(parentRect)).Append('\n');
                foreach (var child in transform.GetComponentsInChildren<Transform>(true))
                    Describe(text, child);
                if (transform.parent != null)
                {
                    for (int i = 0; i < transform.parent.childCount; i++)
                    {
                        var sibling = transform.parent.GetChild(i);
                        if (sibling && sibling.name == "RadioNavWindBearing")
                            Describe(text, sibling);
                    }
                }
            }
            return text.Length == 0 ? "none" : text.ToString();
        }

        private static void Describe(StringBuilder text, Transform child)
        {
            var tmp = child.GetComponent<TextMeshProUGUI>();
            var mesh = child.GetComponent<TextMeshPro>();
            var legacy = child.GetComponent<Text>();
            string value = tmp ? (string.IsNullOrEmpty(tmp.GetParsedText()) ? tmp.text : tmp.GetParsedText())
                : mesh ? mesh.text : legacy ? legacy.text : null;
            if (value == null)
                return;
            var rect = child as RectTransform;
            string bounds = tmp ? GlyphScreenBounds(tmp) : (rect ? ScreenBounds(rect) : "-");
            text.Append("node=").Append(child.name)
                .Append(":active=").Append(child.gameObject.activeInHierarchy)
                .Append(":text=").Append(value.Replace("\n", " "))
                .Append('@').Append(rect ? ScreenPoint(rect) : "-")
                .Append(":bounds=").Append(bounds)
                .Append(":rect=").Append(rect ? ScreenBounds(rect) : "-")
                .Append('\n');
        }

        public static string SetMapOpen(bool open)
        {
            var cam = MainCamera.Instance;
            if (!cam)
                return "no camera";
            cam.SetMode(open ? CameraMode.Map : CameraMode.FreeFly, CameraTransitionType.Instant, 0f);
            return cam.CurrentMode.ToString();
        }

        public static bool SetDepth(double metres)
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return false;
            ship.SetTargetDepth((float)metres, true, true);
            return true;
        }

        public static string SetCalm(bool enabled)
        {
            var weather = InjectionFramework.Instance?.GetInstance<Weather>();
            if (!weather)
                return "no weather";
            if (enabled)
            {
                if (calmModifier == null)
                    calmModifier = weather.WindSpeed.AddScaleModifier("RadioNavWindTestCalm");
                calmModifier.Value = 0f;
            }
            else if (calmModifier != null)
            {
                calmModifier.Remove();
                calmModifier = null;
            }
            return weather.TotalWindSpeed.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void CaptureOriginalLocaleAndUnits()
        {
            if (savedLocaleState)
                return;
            var framework = InjectionFramework.Instance;
            var settings = framework?.GetInstance<UserSettings>();
            var loc = framework?.GetInstance<Locale>();
            if (settings == null || loc == null)
                return;
            origUnits = settings.GameplaySettings.units;
            origLanguage = loc.Language;
            if (StringsField?.GetValue(loc) is System.Collections.Generic.Dictionary<string, string> map)
            {
                map.TryGetValue("knots", out origKnots);
                map.TryGetValue("kmh", out origKmh);
            }
            savedLocaleState = true;
        }

        public static string SetUnits(bool metric)
        {
            CaptureOriginalLocaleAndUnits();
            var settings = InjectionFramework.Instance?.GetInstance<UserSettings>();
            if (settings == null)
                return "no settings";
            settings.GameplaySettings.units = metric ? Units.Metric : Units.MetricWithKnots;
            InvalidateMapWindCache();
            return settings.GameplaySettings.units.ToString();
        }

        public static string SetLanguageInMemory(string lang, string knotsText, string kmhText)
        {
            CaptureOriginalLocaleAndUnits();
            var loc = InjectionFramework.Instance?.GetInstance<Locale>();
            if (loc == null)
                return "no locale";
            LanguageField?.SetValue(loc, lang);
            if (StringsField?.GetValue(loc) is System.Collections.Generic.Dictionary<string, string> map)
            {
                if (knotsText != null)
                    map["knots"] = knotsText;
                if (kmhText != null)
                    map["kmh"] = kmhText;
            }
            CacheKnField?.SetValue(null, null);
            CacheKmhField?.SetValue(null, null);
            InvalidateMapWindCache();
            return loc.Language ?? "?";
        }

        public static string RestoreLocaleAndUnits()
        {
            if (!savedLocaleState)
                return "unmodified";
            var framework = InjectionFramework.Instance;
            var settings = framework?.GetInstance<UserSettings>();
            var loc = framework?.GetInstance<Locale>();
            if (settings != null)
                settings.GameplaySettings.units = origUnits;
            if (loc != null)
            {
                LanguageField?.SetValue(loc, origLanguage);
                if (StringsField?.GetValue(loc) is System.Collections.Generic.Dictionary<string, string> map)
                {
                    if (origKnots != null)
                        map["knots"] = origKnots;
                    if (origKmh != null)
                        map["kmh"] = origKmh;
                }
            }
            CacheKnField?.SetValue(null, null);
            CacheKmhField?.SetValue(null, null);
            InvalidateMapWindCache();
            return (origLanguage ?? "?") + ":" + origUnits;
        }

        private static void InvalidateMapWindCache()
        {
            foreach (var map in Resources.FindObjectsOfTypeAll<MapUI>())
            {
                if (map)
                    NativeWindCacheField?.SetValue(map, -1);
            }
        }

        private static bool HasWind(string value) =>
            !string.IsNullOrEmpty(value) && (value.Contains("уз") || value.Contains("knot")
                || value.Contains("м/с") || value.Contains("km/h") || value.Contains("км/ч"));

        private static string Chain(Transform start)
        {
            var text = new StringBuilder();
            for (int i = 0; i < 5 && start; i++)
            {
                text.Append(i == 0 ? "" : "<").Append(start.name);
                start = start.parent;
            }
            return text.ToString();
        }

        private static string ScreenPoint(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
            return point.x.ToString("F0", CultureInfo.InvariantCulture) + ","
                + point.y.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static string ScreenBounds(RectTransform rect)
        {
            if (!rect)
                return "-";
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}..{1:F1},{2:F1}..{3:F1}", minX, maxX, minY, maxY);
        }

        private static string GlyphScreenBounds(TextMeshProUGUI tmp)
        {
            var info = tmp.textInfo;
            if (info == null || info.characterCount == 0)
                return ScreenBounds(tmp.rectTransform);
            float localMin = float.MaxValue, localMax = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int n = Math.Min(info.characterCount, info.characterInfo.Length);
            for (int i = 0; i < n; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible)
                    continue;
                if (ch.bottomLeft.x < localMin) localMin = ch.bottomLeft.x;
                if (ch.topRight.x > localMax) localMax = ch.topRight.x;
                if (ch.bottomLeft.y < minY) minY = ch.bottomLeft.y;
                if (ch.topRight.y > maxY) maxY = ch.topRight.y;
            }
            if (localMin > localMax)
                return ScreenBounds(tmp.rectTransform);
            var canvas = tmp.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(camera,
                tmp.rectTransform.TransformPoint(new Vector3(localMin, minY, 0f)));
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(camera,
                tmp.rectTransform.TransformPoint(new Vector3(localMax, maxY, 0f)));
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}..{1:F1},{2:F1}..{3:F1}",
                Math.Min(bl.x, tr.x), Math.Max(bl.x, tr.x), Math.Min(bl.y, tr.y), Math.Max(bl.y, tr.y));
        }
    }
}
