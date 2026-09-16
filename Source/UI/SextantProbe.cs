using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Read-write test probe for the sextant (MCP bridge /sextant*): live
    /// device state (open, arc, filters, calibration, identification, the two
    /// worksheet sights and the published fix) plus setters that drive the
    /// same code paths as the keys (open/close, arc, filters, record, reset)
    /// and a test-only aim override gated by PlayerPrefs RadioNav.TestAim.
    /// Pull model, main thread — mirrors RadioNavProbe.
    /// </summary>
    public static class SextantProbe
    {
        private const string MoonDiagPrefsKey = "RadioNav.MoonDiag";
        private const string TestAimPrefsKey = "RadioNav.TestAim";

        public static string Snapshot()
        {
            try
            {
                SextantController controller = ResolveController();
                if (controller == null)
                    return "{\"ok\":true,\"loaded\":false}";
                SextantWorksheet worksheet = controller.WorksheetForProbe;
                StringBuilder sb = new StringBuilder(1024);
                sb.Append("{\"ok\":true,\"loaded\":true");
                sb.Append(",\"open\":").Append(SextantController.IsOpen ? "true" : "false");
                sb.Append(",\"arcDeg\":").Append(Num(controller.ArcAngle));
                sb.Append(",\"filters\":{\"left\":").Append(controller.LeftFilterCount)
                  .Append(",\"right\":").Append(controller.RightFilterCount).Append('}');
                sb.Append(",\"calibrationMin\":").Append(Num(SunEphemerisCalibration.DeltaMinutes));
                sb.Append(",\"moonDiag\":").Append(MoonDiagEnabled() ? "true" : "false");
                AppendIdentified(sb, controller.StarIdForProbe);
                AppendOwnBodies(sb, controller);
                AppendSight(sb, "sight1", worksheet.Calculator.Observation1);
                AppendSight(sb, "sight2", worksheet.Calculator.Observation2);
                sb.Append(",\"fix\":");
                AppendFixBody(sb, worksheet);
                AppendMarker(sb);
                sb.Append('}');
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        /// <summary>Partial device update; null/empty arguments stay unchanged.</summary>
        public static string Set(string openText, string arcText, string filterLeftText,
            string filterRightText, string moonDiagText)
        {
            SextantController controller = ResolveController();
            if (controller == null)
                return "{\"ok\":false,\"error\":\"no-live-probe\"}";

            SetArgs args = new SetArgs();
            string error = ParseSetArgs(openText, arcText, filterLeftText,
                filterRightText, moonDiagText, ref args);
            if (error != null)
                return error;
            if (!args.Any)
                return "{\"ok\":false,\"error\":\"nothing-to-set\"}";

            try
            {
                return ApplySet(controller, ref args);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + RadioNavProbeJson.Escape(ex.GetType().Name) + "\"}";
            }
        }

        private struct SetArgs
        {
            public bool HasOpen;
            public bool Open;
            public bool HasArc;
            public double Arc;
            public bool HasLeft;
            public int Left;
            public bool HasRight;
            public int Right;
            public bool HasMoon;
            public bool Moon;

            public bool Any
            {
                get { return HasOpen || HasArc || HasLeft || HasRight || HasMoon; }
            }
        }

        private static string ParseSetArgs(string openText, string arcText,
            string filterLeftText, string filterRightText, string moonDiagText,
            ref SetArgs args)
        {
            args.HasOpen = !string.IsNullOrEmpty(openText);
            args.HasArc = !string.IsNullOrEmpty(arcText);
            args.HasLeft = !string.IsNullOrEmpty(filterLeftText);
            args.HasRight = !string.IsNullOrEmpty(filterRightText);
            args.HasMoon = !string.IsNullOrEmpty(moonDiagText);
            if (args.HasOpen && !TryBool(openText, out args.Open))
                return "{\"ok\":false,\"error\":\"bad-open\"}";
            if (args.HasArc && !TryArc(arcText, out args.Arc))
                return "{\"ok\":false,\"error\":\"bad-arc\"}";
            if (args.HasLeft && !TryFilter(filterLeftText, out args.Left))
                return "{\"ok\":false,\"error\":\"bad-filter\"}";
            if (args.HasRight && !TryFilter(filterRightText, out args.Right))
                return "{\"ok\":false,\"error\":\"bad-filter\"}";
            if (args.HasMoon && !TryBool(moonDiagText, out args.Moon))
                return "{\"ok\":false,\"error\":\"bad-moonDiag\"}";
            return null;
        }

        private static string ApplySet(SextantController controller, ref SetArgs args)
        {
            if (args.HasArc)
                controller.SetArcForProbe(SextantArc.Quantize(args.Arc));
            if (args.HasLeft || args.HasRight)
                controller.SetFiltersForProbe(args.HasLeft ? args.Left : controller.LeftFilterCount,
                    args.HasRight ? args.Right : controller.RightFilterCount);
            if (args.HasMoon)
                ApplyMoonDiag(controller, args.Moon);
            if (args.HasOpen)
                return ApplyOpen(controller, args.Open);
            return BuildSetResult(controller, args.HasMoon ? args.Moon : MoonDiagEnabled());
        }

        private static string ApplyOpen(SextantController controller, bool open)
        {
            if (!open)
            {
                controller.CloseForProbe();
                return BuildSetResult(controller, MoonDiagEnabled());
            }
            if (!controller.TryOpenForProbe())
                return "{\"ok\":false,\"error\":\"cannot-open\"}";
            return BuildSetResult(controller, MoonDiagEnabled());
        }

        private static bool TryArc(string text, out double arc)
        {
            return TryDouble(text, out arc) && InRange(arc, 0.0, SextantArc.MaxDeg);
        }

        /// <summary>Finite value inside [min, max] — NaN/Infinity are rejected
        /// (NaN passes plain comparisons and would poison the arc/camera pose).</summary>
        private static bool InRange(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;
            return value >= min && value <= max;
        }

        private static bool TryFilter(string text, out int value)
        {
            return TryInt(text, out value) && value >= 0 && value <= 3;
        }

        /// <summary>I-key equivalent: records a sight of the identified body.</summary>
        public static string Record()
        {
            SextantController controller = ResolveController();
            if (controller == null)
                return "{\"ok\":false,\"error\":\"no-live-probe\"}";
            if (!SextantController.IsOpen)
                return "{\"ok\":false,\"error\":\"not-open\"}";
            CelestialBody body = controller.StarIdForProbe.IdentifiedBody;
            if (body.IsNone)
                return "{\"ok\":false,\"error\":\"no-body\"}";
            float lat, lon;
            if (!controller.TryGetPositionForProbe(out lat, out lon))
                return "{\"ok\":false,\"error\":\"no-position\"}";
            SextantWorksheet worksheet = controller.WorksheetForProbe;
            if (!worksheet.CaptureObservation(body, controller.ArcAngle))
                return "{\"ok\":false,\"error\":\"no-body\"}";
            int index = worksheet.Calculator.Observation2 != null ? 2 : 1;
            SextantObservation sight = index == 2
                ? worksheet.Calculator.Observation2
                : worksheet.Calculator.Observation1;
            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"recorded\":").Append(index);
            sb.Append(",\"body\":\"").Append(RadioNavProbeJson.Escape(body.DisplayName)).Append('"');
            sb.Append(",\"sight\":");
            AppendObservationBody(sb, sight);
            sb.Append(",\"fix\":");
            AppendFixBody(sb, worksheet);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>O-key equivalent: starts a new worksheet series.</summary>
        public static string Reset()
        {
            SextantController controller = ResolveController();
            if (controller == null)
                return "{\"ok\":false,\"error\":\"no-live-probe\"}";
            controller.WorksheetForProbe.ResetSeries();
            return "{\"ok\":true,\"cleared\":true}";
        }

        /// <summary>Test-only aim: yaw override + arc = altDeg. Requires the
        /// PlayerPrefs RadioNav.TestAim gate and an open sextant.</summary>
        public static string Aim(string altText, string azText)
        {
            SextantController controller = ResolveController();
            if (controller == null)
                return "{\"ok\":false,\"error\":\"no-live-probe\"}";
            if (PlayerPrefs.GetInt(TestAimPrefsKey, 0) == 0)
                return "{\"ok\":false,\"error\":\"aim-disabled\"}";
            if (!SextantController.IsOpen)
                return "{\"ok\":false,\"error\":\"not-open\"}";
            double alt;
            double az;
            if (!TryDouble(altText, out alt) || !InRange(alt, -90.0, 90.0))
                return "{\"ok\":false,\"error\":\"bad-aim\"}";
            if (!TryDouble(azText, out az) || !InRange(az, 0.0, 360.0))
                return "{\"ok\":false,\"error\":\"bad-aim\"}";
            SextantPipPose.SetTestAim((float)az);
            controller.SetArcForProbe(SextantArc.Quantize(alt));
            StringBuilder sb = new StringBuilder(160);
            sb.Append("{\"ok\":true,\"aim\":{\"altDeg\":").Append(Num(alt))
              .Append(",\"azDeg\":").Append(Num(az)).Append('}');
            sb.Append(",\"arcDeg\":").Append(Num(controller.ArcAngle)).Append('}');
            return sb.ToString();
        }

        // ---- helpers -------------------------------------------------------

        private static SextantController ResolveController()
        {
            SextantController controller = SextantController.Instance;
            if (controller != null)
                return controller;
            try
            {
                return UnityEngine.Object.FindObjectOfType<SextantController>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ApplyMoonDiag(SextantController controller, bool enabled)
        {
            PlayerPrefs.SetInt(MoonDiagPrefsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (enabled)
                controller.ResetMoonDiagTimerForProbe();
        }

        private static bool MoonDiagEnabled()
        {
            return PlayerPrefs.GetInt(MoonDiagPrefsKey, 0) != 0;
        }

        private static string BuildSetResult(SextantController controller, bool moonDiag)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("{\"ok\":true,\"open\":").Append(SextantController.IsOpen ? "true" : "false");
            sb.Append(",\"arcDeg\":").Append(Num(controller.ArcAngle));
            sb.Append(",\"filters\":{\"left\":").Append(controller.LeftFilterCount)
              .Append(",\"right\":").Append(controller.RightFilterCount).Append('}');
            sb.Append(",\"moonDiag\":").Append(moonDiag ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendIdentified(StringBuilder sb, SextantStarId starId)
        {
            CelestialBody body = starId.IdentifiedBody;
            sb.Append(",\"identified\":{\"kind\":\"").Append(body.Kind.ToString()).Append('"');
            sb.Append(",\"name\":\"")
              .Append(RadioNavProbeJson.Escape(body.IsNone ? "" : body.DisplayName)).Append('"');
            sb.Append(",\"hip\":").Append(body.Hip.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"altDeg\":").Append(body.IsNone ? "null" : Num(starId.IdentifiedAltDeg));
            sb.Append('}');
        }

        private static void AppendOwnBodies(StringBuilder sb, SextantController controller)
        {
            Vector3 dir;
            float sunAlt;
            float sunAz;
            bool sunOk = controller.TryGetOwnSun(out dir, out sunAlt, out sunAz);
            sb.Append(",\"ownSun\":{\"altDeg\":").Append(sunOk ? Num(sunAlt) : "null");
            sb.Append(",\"azDeg\":").Append(sunOk ? Num(sunAz) : "null").Append('}');

            float moonAlt;
            float moonAz;
            bool moonOk = controller.TryGetOwnMoon(out moonAlt, out moonAz);
            double illumination;
            bool illumOk = TryGetMoonIllumination(out illumination);
            sb.Append(",\"ownMoon\":{\"altDeg\":").Append(moonOk ? Num(moonAlt) : "null");
            sb.Append(",\"azDeg\":").Append(moonOk ? Num(moonAz) : "null");
            sb.Append(",\"illumination\":").Append(illumOk ? Num(illumination) : "null");
            sb.Append('}');
        }

        private static bool TryGetMoonIllumination(out double illumination)
        {
            illumination = 0.0;
            try
            {
                double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), GameTimeResolver.GetUtcNow());
                illumination = AstroMath.MoonIllumination(jd);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void AppendSight(StringBuilder sb, string key, SextantObservation obs)
        {
            sb.Append(',').Append('"').Append(key).Append("\":");
            AppendObservationBody(sb, obs);
        }

        private static void AppendObservationBody(StringBuilder sb, SextantObservation obs)
        {
            if (obs == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append("{\"kind\":\"").Append(obs.BodyKind.ToString()).Append('"');
            sb.Append(",\"body\":\"").Append(RadioNavProbeJson.Escape(obs.BodyName ?? "")).Append('"');
            sb.Append(",\"utc\":\"")
              .Append(obs.Utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
              .Append('"');
            sb.Append(",\"hoDeg\":").Append(Num(obs.HoDeg));
            sb.Append(",\"hcDeg\":").Append(Num(obs.HcDeg));
            sb.Append(",\"azDeg\":").Append(Num(obs.AzDeg));
            sb.Append(",\"interceptNm\":").Append(Num(obs.InterceptNm));
            sb.Append('}');
        }

        private static void AppendFixBody(StringBuilder sb, SextantWorksheet worksheet)
        {
            if (!worksheet.FixPublished)
            {
                sb.Append("null");
                return;
            }
            sb.Append("{\"latDeg\":").Append(Num(worksheet.FixLatDeg));
            sb.Append(",\"lonDeg\":").Append(Num(worksheet.FixLonDeg));
            sb.Append(",\"errorNm\":")
              .Append(double.IsNaN(worksheet.FixErrorNm) ? "null" : Num(worksheet.FixErrorNm));
            sb.Append(",\"crossingDeg\":").Append(Num(worksheet.FixCrossingDeg));
            sb.Append(",\"sameBody\":").Append(worksheet.FixSameBody ? "true" : "false").Append('}');
        }

        private static void AppendMarker(StringBuilder sb)
        {
            sb.Append(",\"marker\":{\"hasFix\":").Append(FixMapMarker.HasFix ? "true" : "false");
            sb.Append(",\"lat\":").Append(Num(FixMapMarker.FixLat));
            sb.Append(",\"lon\":").Append(Num(FixMapMarker.FixLon));
            sb.Append(",\"suppressed\":").Append(FixMapMarker.Suppressed ? "true" : "false").Append('}');
        }

        private static string Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryBool(string text, out bool value)
        {
            value = false;
            if (text == "1" || text.ToLowerInvariant() == "true")
            {
                value = true;
                return true;
            }
            if (text == "0" || text.ToLowerInvariant() == "false")
            {
                value = false;
                return true;
            }
            return false;
        }
    }
}
