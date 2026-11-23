using System.Globalization;
using System.Text;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Read-only in-game view of the experimental drift adapter. It never
    /// changes the environment, the hull or the campaign; it only prints
    /// what the physics path is currently applying.
    /// </summary>
    public static class CurrentDiagnostics
    {
        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
                return;
            registered = true;
            DevConsole.AddListener("rn_current", Report);
            DevConsole.AddListener("rn_currents", ToggleOrDescribe);
            DevConsole.AddListener("rn_current_vectors", ToggleOrConfigureVectors);
            Debug.Log("[RadioNav][CURRENT] rn_current registered");
        }

        private static void Report(string[] args)
        {
            StringBuilder text = new StringBuilder();
            text.Append("[RadioNav][CURRENT] ").Append(CurrentRuntimeAdapter.Describe());
            text.Append('\n').Append("[RadioNav][CURRENT] ").Append(CurrentStrategicDrift.Describe());
            foreach (string line in CurrentRuntimeAdapter.DescribeNearby())
                text.Append('\n').Append(line);
            Debug.Log(text.ToString());
        }

        private static void ToggleOrDescribe(string[] args)
        {
            if (args?.Length >= 2)
            {
                string token = args[1];
                if (CurrentSettings.IsDisabledToken(token))
                {
                    CurrentSettings.SaveMasterPreference(false);
                    Debug.Log("[RadioNav][CURRENT] currents disabled (PlayerPrefs RadioNav.Currents=0; takes full effect on reload/restart)");
                    return;
                }
                if (CurrentSettings.IsEnabledToken(token))
                {
                    CurrentSettings.SaveMasterPreference(true);
                    Debug.Log("[RadioNav][CURRENT] currents enabled (PlayerPrefs RadioNav.Currents=1; takes full effect on reload/restart)");
                    return;
                }
                Debug.LogWarning("[RadioNav][CURRENT] usage: rn_currents [on|off]");
                return;
            }
            Debug.Log("[RadioNav][CURRENT] settings master=" + CurrentSettings.IsMasterEnabled()
                + " atlas=\"" + CurrentSettings.ResolveRequestedAtlas() + "\""
                + " wind=" + CurrentSettings.DirectWindEnabled()
                + " nearby=" + CurrentSettings.NearbyDriftEnabled()
                + " far=" + CurrentSettings.FarDriftEnabled()
                + " route=" + CurrentSettings.RouteCurrentEnabled()
                + " ballistics=" + CurrentSettings.BallisticWindEnabled()
                + " tidal=" + CurrentSettings.TidalCurrentsEnabled()
                + " vectors=" + CurrentSettings.MapVectorsEnabled());
        }

        private static void ToggleOrConfigureVectors(string[] args)
        {
            if (args?.Length >= 2)
            {
                string token = args[1];
                if (CurrentSettings.IsDisabledToken(token))
                {
                    CurrentSettings.SaveMapVectorsPreference(false);
                    MapCurrentVectorsDriver.InvalidateSamples();
                    Debug.Log("[RadioNav][CURRENT] map vectors OFF (Shift+Y to toggle)");
                    return;
                }
                if (CurrentSettings.IsEnabledToken(token))
                {
                    CurrentSettings.SaveMapVectorsPreference(true);
                    MapCurrentVectorsDriver.InvalidateSamples();
                    Debug.Log("[RadioNav][CURRENT] map vectors ON (Shift+Y to toggle)");
                    return;
                }
                if (string.Equals(token, "depth", System.StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
                {
                    ApplyVectorDepthArgument(args[2]);
                    return;
                }
                Debug.LogWarning("[RadioNav][CURRENT] usage: rn_current_vectors [on|off|depth <meters|ship>]");
                return;
            }
            float depthM = CurrentSettings.GetVectorSampleDepthMeters();
            Debug.Log("[RadioNav][CURRENT] map vectors=" + CurrentSettings.MapVectorsEnabled()
                + " depth=" + (depthM < 0f ? "ship" : depthM.ToString("F1", CultureInfo.InvariantCulture) + "m")
                + " (toggle: Shift+Y or rn_current_vectors [on|off|depth <m|ship>])");
        }

        private static void ApplyVectorDepthArgument(string arg)
        {
            if (string.Equals(arg, "ship", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "auto", System.StringComparison.OrdinalIgnoreCase))
            {
                CurrentSettings.SaveVectorSampleDepthMeters(-1f);
                MapCurrentVectorsDriver.InvalidateSamples();
                Debug.Log("[RadioNav][CURRENT] map vectors depth=ship");
                return;
            }
            if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float meters)
                && !float.IsNaN(meters) && !float.IsInfinity(meters) && meters >= 0f)
            {
                CurrentSettings.SaveVectorSampleDepthMeters(meters);
                MapCurrentVectorsDriver.InvalidateSamples();
                Debug.Log("[RadioNav][CURRENT] map vectors depth="
                    + meters.ToString("F1", CultureInfo.InvariantCulture) + "m");
                return;
            }
            Debug.LogWarning("[RadioNav][CURRENT] invalid depth \"" + arg + "\"; expected >= 0 or \"ship\"");
        }
    }
}
