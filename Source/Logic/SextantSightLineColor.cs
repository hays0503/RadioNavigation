using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sight-line and aiming-window colour (author decision 2026-09-22): a thin
    /// BLACK line by day (reads against the bright sky and sea) that turns into
    /// greenish luminous paint at night (visible in the dark, "светомасса").
    /// Driven by the same darkness factor the star sky uses. Pure, no Unity
    /// state — the renderer just assigns the returned colour.
    /// </summary>
    public static class SextantSightLineColor
    {
        /// <summary>Daytime: thin black, slightly transparent.</summary>
        public static readonly Color Day = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>Night: greenish luminous paint.</summary>
        public static readonly Color Night = new Color(0.55f, 1.0f, 0.62f, 0.85f);

        /// <summary>Colour for a night factor in [0,1] (0 = day, 1 = night).</summary>
        public static Color For(float night)
        {
            float t = night < 0f ? 0f : (night > 1f ? 1f : night);
            return new Color(
                Day.r + (Night.r - Day.r) * t,
                Day.g + (Night.g - Day.g) * t,
                Day.b + (Night.b - Day.b) * t,
                Day.a + (Night.a - Day.a) * t);
        }
    }
}
