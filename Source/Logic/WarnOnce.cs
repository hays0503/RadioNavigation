using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Once-per-key warning gate for per-frame/setup reflection paths that
    /// must stay silent in Player.log but whose FIRST failure must be
    /// visible — a silent catch is a silent dead feature (risk register
    /// iter3). Main thread only; keys are bounded call-site constants, so
    /// the set never grows. Unit-tested offline in WarnOnceTests.
    /// Pure C#, no Unity dependency.
    /// </summary>
    public static class WarnOnce
    {
        private static readonly HashSet<string> fired = new HashSet<string>();

        public static bool ShouldLog(string key)
        {
            if (fired.Contains(key))
                return false;
            fired.Add(key);
            return true;
        }

        public static void Reset()
        {
            fired.Clear();
        }
    }
}
