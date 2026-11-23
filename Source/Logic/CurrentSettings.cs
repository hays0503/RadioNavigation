using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Configuration resolution for currents, direct wind and steering.
    /// Production features are enabled by default (bundled ORA-20C 1939-1945
    /// atlas + direct wind + nearby drift + relative rudder + far drift +
    /// route compensation) unless opted out via PlayerPrefs or RADIONAV_*
    /// environment variables (preserving explicit "0"/"off" test baselines).
    /// </summary>
    internal static class CurrentSettings
    {
        internal const string MasterPrefsKey = "RadioNav.Currents";
        internal const string MasterVariable = "RADIONAV_CURRENTS";
        internal const string FieldVariable = "RADIONAV_CURRENT_FIELD";
        internal const string ProfileVariable = "RADIONAV_CURRENT_PROFILE";
        internal const string AtlasVariable = "RADIONAV_ATLAS_TILE";
        internal const string WindVariable = "RADIONAV_DIRECT_WIND";
        internal const string NearbyVariable = "RADIONAV_NEARBY_DRIFT";
        internal const string RudderVariable = "RADIONAV_RELATIVE_RUDDER";
        internal const string FarVariable = "RADIONAV_FAR_DRIFT";
        internal const string RouteVariable = "RADIONAV_ROUTE_CURRENT";
        internal const string BallisticWindVariable = "RADIONAV_BALLISTIC_WIND";
        internal const string TidalVariable = "RADIONAV_TIDAL_CURRENTS";
        internal const string VectorsPrefsKey = "RadioNav.CurrentVectors";
        internal const string VectorsVariable = "RADIONAV_CURRENT_VECTORS";
        internal const string VectorDepthPrefsKey = "RadioNav.CurrentVectorsDepthM";
        internal const string BundledAtlasValue = "bundled";

        internal static bool IsMasterEnabled()
        {
            string raw = Environment.GetEnvironmentVariable(MasterVariable);
            if (IsDisabledToken(raw))
                return false;
            if (IsEnabledToken(raw))
                return true;
            return PlayerPrefs.GetInt(MasterPrefsKey, 1) != 0;
        }

        internal static bool DirectWindEnabled()
        {
            return IsMasterEnabled() && ReadFlag(WindVariable, true);
        }

        internal static bool NearbyDriftEnabled()
        {
            return IsMasterEnabled() && ReadFlag(NearbyVariable, true);
        }

        internal static bool RelativeRudderEnabled(bool anyFlow)
        {
            return anyFlow && IsMasterEnabled() && ReadFlag(RudderVariable, true);
        }

        internal static bool FarDriftEnabled()
        {
            return IsMasterEnabled() && ReadFlag(FarVariable, true);
        }

        internal static bool RouteCurrentEnabled()
        {
            return IsMasterEnabled() && ReadFlag(RouteVariable, true);
        }

        internal static bool BallisticWindEnabled()
        {
            return IsMasterEnabled() && ReadFlag(BallisticWindVariable, true);
        }

        internal static bool TidalCurrentsEnabled()
        {
            return IsMasterEnabled() && ReadFlag(TidalVariable, true);
        }

        internal static bool MapVectorsEnabled()
        {
            if (!IsMasterEnabled())
                return false;
            string raw = Environment.GetEnvironmentVariable(VectorsVariable);
            if (IsDisabledToken(raw))
                return false;
            if (IsEnabledToken(raw))
                return true;
            return PlayerPrefs.GetInt(VectorsPrefsKey, 1) != 0;
        }

        internal static void SaveMapVectorsPreference(bool enabled)
        {
            PlayerPrefs.SetInt(VectorsPrefsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal static float GetVectorSampleDepthMeters()
        {
            return PlayerPrefs.GetFloat(VectorDepthPrefsKey, -1f);
        }

        internal static void SaveVectorSampleDepthMeters(float depthMeters)
        {
            PlayerPrefs.SetFloat(VectorDepthPrefsKey, depthMeters);
            PlayerPrefs.Save();
        }

        internal static string ResolveRequestedAtlas()
        {
            if (!IsMasterEnabled())
                return "";
            return ResolveRequestedAtlas(
                Environment.GetEnvironmentVariable(AtlasVariable),
                Environment.GetEnvironmentVariable(ProfileVariable),
                Environment.GetEnvironmentVariable(FieldVariable));
        }

        internal static string ResolveRequestedAtlas(string atlasRaw, string profileRaw, string fieldRaw)
        {
            if (!string.IsNullOrEmpty(atlasRaw))
                return IsDisabledToken(atlasRaw) ? "" : atlasRaw;
            if (!string.IsNullOrEmpty(profileRaw) || !string.IsNullOrEmpty(fieldRaw))
                return "";
            return BundledAtlasValue;
        }

        internal static void SaveMasterPreference(bool enabled)
        {
            PlayerPrefs.SetInt(MasterPrefsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal static bool ReadFlag(string envName, bool defaultValue)
        {
            string raw = Environment.GetEnvironmentVariable(envName);
            if (IsDisabledToken(raw))
                return false;
            if (IsEnabledToken(raw))
                return true;
            return defaultValue;
        }

        internal static bool IsDisabledToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            string trimmed = raw.Trim();
            return string.Equals(trimmed, "0", StringComparison.Ordinal)
                || string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsEnabledToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            string trimmed = raw.Trim();
            return string.Equals(trimmed, "1", StringComparison.Ordinal)
                || string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
