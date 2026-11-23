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
    }
}
