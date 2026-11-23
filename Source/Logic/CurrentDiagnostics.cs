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
                + " tidal=" + CurrentSettings.TidalCurrentsEnabled());
        }
    }
}
