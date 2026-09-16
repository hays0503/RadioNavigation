using System;
using System.Globalization;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Game-sun access for SUNDRIFT diagnostics (SEXTANT_PLAN v1.3):
    /// TOD_Sky is not referenceable by the in-game mod compiler, so the
    /// game values are read via cached reflection and never named.
    /// Also owns the 10 s drift log comparing the game sun against the
    /// mod's own Meeus ephemeris. Extracted from SextantController (SRP:
    /// reflection plumbing is not device logic).
    /// </summary>
    internal sealed class SextantGameSun
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const float DriftLogIntervalSeconds = 10f;
        private const string MoonDiagPrefsKey = "RadioNav.MoonDiag";
        private const float MoonDiagIntervalSeconds = 10f;
        private const double Deg2Rad = Math.PI / 180.0;

        private Weather weather;
        private PropertyInfo skyProperty;
        private MemberInfo sunDirMember;
        private MemberInfo sunAltMember;
        private bool membersResolved;
        private bool unavailableWarned;
        private float driftLogTimer;
        private MemberInfo moonAltMember;
        private MemberInfo moonAzMember;
        private bool moonMembersResolved;
        private float moonDiagTimer;

        /// <summary>
        /// Reads the game's TOD_Sky sun direction/altitude via cached
        /// reflection. False when the weather/sky is unavailable.
        /// </summary>
        public bool TryGetSun(out Vector3 dir, out float alt)
        {
            dir = Vector3.zero;
            alt = 0f;
            if (weather == null && InjectionFramework.Instance.IsNotNull())
                weather = InjectionFramework.Instance.GetInstance<Weather>();
            if (weather == null)
                return false;

            try
            {
                if (skyProperty == null)
                    skyProperty = weather.GetType().GetProperty("Sky");
                if (skyProperty == null)
                    return false;

                object sky = skyProperty.GetValue(weather, null);
                if (sky == null)
                    return false;

                if (!membersResolved)
                {
                    membersResolved = true;
                    sunDirMember = FindMember(sky.GetType(), "SunDirection");
                    sunAltMember = FindMember(sky.GetType(), "SunAltitude");
                }
                if (sunDirMember == null || sunAltMember == null)
                    return false;

                object dirBoxed = ReadMember(sky, sunDirMember);
                if (!(dirBoxed is Vector3))
                    return false;
                dir = (Vector3)dirBoxed;
                alt = Convert.ToSingle(ReadMember(sky, sunAltMember));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " SUNDRIFT reflection failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// SUNDRIFT telemetry: every 10 s while the sextant is open, log
        /// the angular separation between our Meeus sun vector and the
        /// game's TOD_Sky sun vector. A steady sep above ~0.5° means an
        /// ephemeris or axis-convention drift.
        /// </summary>
        public void LogDrift(float dt,
            bool ownOk, Vector3 ownDir, float ownAlt, float ownAz)
        {
            driftLogTimer -= dt;
            if (driftLogTimer > 0f)
                return;
            driftLogTimer = DriftLogIntervalSeconds;

            if (!ownOk)
                return; // no sandbox position - nothing to compare against

            Vector3 gameDir;
            float gameAlt;
            if (!TryGetSun(out gameDir, out gameAlt))
            {
                if (!unavailableWarned)
                {
                    unavailableWarned = true;
                    Debug.LogWarning(LogPrefix + " SUNDRIFT game sun unavailable via reflection - drift unmonitored");
                }
                return;
            }

            float sepDeg = Vector3.Angle(ownDir, gameDir);
            float dAlt = ownAlt - gameAlt;
            // Azimuths 0=N +east; game azimuth derived from the world vector
            // with the same (e>x, n>z) mapping the star sky proved in-game.
            float gameAz = Mathf.Atan2(gameDir.x, gameDir.z) * Mathf.Rad2Deg;
            if (gameAz < 0f) gameAz += 360f;
            float dAz = ownAz - gameAz;
            if (dAz > 180f) dAz -= 360f;
            if (dAz < -180f) dAz += 360f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} SUNDRIFT sep={1:F3} dalt={2:F3} daz={3:F3} meeus(alt={4:F2} az={5:F1}) game(alt={6:F2} az={7:F1})",
                LogPrefix, sepDeg, dAlt, dAz, ownAlt, ownAz, gameAlt, gameAz));
        }

        /// <summary>Forces the first drift line on the next Update (open).</summary>
        public void ResetDriftTimer()
        {
            driftLogTimer = 0f;
        }

        /// <summary>Forces the first moon-parity line on the next Update.</summary>
        public void ResetMoonDiagTimer()
        {
            moonDiagTimer = 0f;
        }

        /// <summary>Game TOD_Sky moon altitude/azimuth via cached reflection.
        /// False when the weather/sky or the moon members are unavailable.</summary>
        public bool TryGetMoon(out float alt, out float az)
        {
            alt = 0f;
            az = 0f;
            if (weather == null && InjectionFramework.Instance.IsNotNull())
                weather = InjectionFramework.Instance.GetInstance<Weather>();
            if (weather == null)
                return false;

            try
            {
                if (skyProperty == null)
                    skyProperty = weather.GetType().GetProperty("Sky");
                if (skyProperty == null)
                    return false;

                object sky = skyProperty.GetValue(weather, null);
                if (sky == null)
                    return false;

                if (!moonMembersResolved)
                {
                    moonMembersResolved = true;
                    moonAltMember = FindMember(sky.GetType(), "MoonAltitude");
                    moonAzMember = FindMember(sky.GetType(), "MoonAzimuth");
                }
                if (moonAltMember == null || moonAzMember == null)
                    return false;

                alt = Convert.ToSingle(ReadMember(sky, moonAltMember));
                az = Convert.ToSingle(ReadMember(sky, moonAzMember));
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantGameSun.moonReflection"))
                    Debug.LogWarning(LogPrefix + " MOONDIAG reflection failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// PlayerPrefs-gated Moon parity telemetry (SEXTANT_PLAN v2.2, V1):
        /// compares the game TOD_Sky moon with the mod's own MoonRaDec moon so
        /// the "two moons" question is answered from a log, not by eye.
        /// sep &lt; 0.3° -> the discs coincide; sep &gt; 0.5° -> two moons.
        /// Throttled to 10 s; enable with PlayerPrefs RadioNav.MoonDiag=1.
        /// </summary>
        public void LogMoonParity(float dt, bool ownOk, float ownAlt, float ownAz)
        {
            // Throttle first: PlayerPrefs is only read once per interval, not
            // every frame.
            moonDiagTimer -= dt;
            if (moonDiagTimer > 0f)
                return;
            moonDiagTimer = MoonDiagIntervalSeconds;
            if (PlayerPrefs.GetInt(MoonDiagPrefsKey, 0) == 0)
                return;
            if (!ownOk)
                return;

            float gameAlt, gameAz;
            if (!TryGetMoon(out gameAlt, out gameAz))
            {
                if (WarnOnce.ShouldLog("SextantGameSun.moonUnavailable"))
                    Debug.LogWarning(LogPrefix + " MOONDIAG game moon unavailable via reflection");
                return;
            }

            double sep = SphericalSeparationDeg(ownAlt, ownAz, gameAlt, gameAz);
            float dAlt = ownAlt - gameAlt;
            float dAz = ownAz - gameAz;
            if (dAz > 180f) dAz -= 360f;
            if (dAz < -180f) dAz += 360f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} MOONDIAG sep={1:F3} dalt={2:F3} daz={3:F3} mod(alt={4:F2} az={5:F1}) game(alt={6:F2} az={7:F1})",
                LogPrefix, sep, dAlt, dAz, ownAlt, ownAz, gameAlt, gameAz));
        }

        /// <summary>Great-circle separation (deg) of two alt/az pairs.</summary>
        private static double SphericalSeparationDeg(double alt1, double az1, double alt2, double az2)
        {
            double a1 = alt1 * Deg2Rad;
            double a2 = alt2 * Deg2Rad;
            double dAz = (az1 - az2) * Deg2Rad;
            double cosSep = Math.Sin(a1) * Math.Sin(a2)
                + Math.Cos(a1) * Math.Cos(a2) * Math.Cos(dAz);
            if (cosSep > 1.0) cosSep = 1.0;
            if (cosSep < -1.0) cosSep = -1.0;
            return Math.Acos(cosSep) * 180.0 / Math.PI;
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            PropertyInfo prop = type.GetProperty(name);
            if (prop != null)
                return prop;
            return type.GetField(name);
        }

        private static object ReadMember(object target, MemberInfo member)
        {
            FieldInfo field = member as FieldInfo;
            if (field != null)
                return field.GetValue(target);
            return ((PropertyInfo)member).GetValue(target, null);
        }
    }
}
