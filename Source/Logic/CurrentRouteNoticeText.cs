using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Player-visible text for the route notices. Kept separate from the
    /// runtime so the wording and the ETA rounding are unit-tested.
    /// </summary>
    internal static class CurrentRouteNoticeText
    {
        internal const string Unreachable = "Течение: курс не удерживается, снос с линии";

        internal static string Corrected(float etaSeconds)
        {
            int minutes = Minutes(etaSeconds);
            if (minutes == 0)
                return "Течение: поправка курса возобновлена";
            return "Течение: поправка курса возобновлена, до точки ~"
                + minutes + " мин";
        }

        internal static int Minutes(float seconds)
        {
            double minutes = Math.Round(seconds / 60.0);
            return seconds > 0f && minutes <= int.MaxValue ? Math.Max(1, (int)minutes) : 0;
        }
    }
}
