using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Ocular size policy (2026-11-04, author decision): the ocular is a
    /// FRACTION of the screen height, not a fixed 600-unit circle. The overlay
    /// canvas lives in the game's UI-scale space (screen / uiScale), so a fixed
    /// unit size shrinks on screen as the UI scale grows (600 units = 954 px at
    /// uiScale 1.59 on 1080p, 66% of the height). Sizing by the unit height
    /// keeps the ocular at the same fraction of the screen at any uiScale and
    /// resolution — the "large ocular" the author asked for.
    ///
    /// Clamps (in unit space):
    ///  - height: the unit height itself, so the circle never leaves the screen;
    ///  - width: the unit width minus the two side columns (reading + sun) and
    ///    their gaps, so the columns never overlap the circle.
    /// The fraction is a PlayerPrefs-tunable (RadioNav.OcularScale, 0.6…0.95,
    /// default 0.88). Pure, no Unity — unit-tested offline.
    /// </summary>
    public static class SextantOcularSize
    {
        /// <summary>Default fraction of the unit height (author 2026-11-04).</summary>
        public const float DefaultFraction = 0.88f;

        /// <summary>Slider band for the PlayerPrefs override.</summary>
        public const float MinFraction = 0.6f;
        public const float MaxFraction = 0.95f;

        /// <summary>PlayerPrefs key for the fraction override.</summary>
        public const string PrefsKey = "RadioNav.OcularScale";

        /// <summary>Clamps a raw fraction into the slider band; invalid values
        /// (zero/NaN) fall back to the default.</summary>
        public static float ClampFraction(float fraction)
        {
            if (fraction <= 0f || float.IsNaN(fraction))
                return DefaultFraction;
            if (fraction < MinFraction)
                return MinFraction;
            if (fraction > MaxFraction)
                return MaxFraction;
            return fraction;
        }

        /// <summary>
        /// Ocular side in canvas units: the fraction of the unit height, then
        /// clamped by the unit width so the reading/sun columns keep their
        /// space. <paramref name="sideColumnsUnits"/> is the total horizontal
        /// budget the columns and their gaps need (both sides). Degenerate
        /// inputs fall back to the 600-unit legacy size.
        /// </summary>
        public static float Units(float unitWidth, float unitHeight, float fraction,
            float sideColumnsUnits)
        {
            if (unitWidth <= 0f || unitHeight <= 0f
                || float.IsNaN(unitWidth) || float.IsNaN(unitHeight))
            {
                return 600f;
            }

            float size = unitHeight * ClampFraction(fraction);
            float widthCap = unitWidth - sideColumnsUnits;
            if (widthCap < size)
                size = widthCap;
            if (size < 1f)
                size = 1f;
            return size;
        }

        /// <summary>Convenience overload for the live call sites: unit space
        /// derived from the screen and the game's UI scale.</summary>
        public static float UnitsForScreen(float screenWidth, float screenHeight,
            float uiScale, float fraction, float sideColumnsUnits)
        {
            if (uiScale <= 0f || float.IsNaN(uiScale))
                uiScale = 1f;
            return Units(screenWidth / uiScale, screenHeight / uiScale, fraction,
                sideColumnsUnits);
        }
    }
}
