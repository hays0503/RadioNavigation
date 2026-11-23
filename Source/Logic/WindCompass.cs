using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Wind direction for the map label. The scene vector points where the air
    /// moves TO (the drag convention); the shown bearing is where it comes
    /// FROM, degrees clockwise from north, with an eight-point name. Pure
    /// functions so the rounding, the sector boundaries and the calm threshold
    /// are unit-tested away from Unity.
    /// </summary>
    internal static class WindCompass
    {
        internal const float CalmMetersPerSecond = 0.3f;
        internal const float DefaultSurfaceToleranceMeters = 0.5f;

        private static readonly string[] Russian =
            { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
        private static readonly string[] English =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        internal static bool IsCalm(float east, float north)
        {
            if (float.IsNaN(east) || float.IsNaN(north))
                return true;
            return east * east + north * north < CalmMetersPerSecond * CalmMetersPerSecond;
        }

        /// <summary>Bearing the wind comes FROM, degrees clockwise from north.</summary>
        internal static float BearingFrom(float east, float north)
        {
            double degrees = Math.Atan2(-east, -north) * 180.0 / Math.PI;
            if (double.IsNaN(degrees) || double.IsInfinity(degrees))
                return 0f;
            degrees %= 360.0;
            if (degrees < 0.0)
                degrees += 360.0;
            return (float)degrees;
        }

        /// <summary>Eight-point index; each sector is centred on its bearing and 45° wide.</summary>
        internal static int Point(float bearing)
        {
            int sector = (int)Math.Floor((bearing + 22.5) / 45.0);
            return (sector % 8 + 8) % 8;
        }

        internal static string PointName(float bearing, bool russian) =>
            (russian ? Russian : English)[Point(bearing)];

        /// <summary>"235° ЮЗ" for the label suffix; empty when there is nothing to show.</summary>
        internal static string Suffix(float east, float north, bool russian)
        {
            if (IsCalm(east, north))
                return string.Empty;
            float bearing = BearingFrom(east, north);
            int degrees = (int)Math.Round(bearing, MidpointRounding.AwayFromZero) % 360;
            return degrees + "° " + PointName(bearing, russian);
        }

        /// <summary>
        /// The hull top at or above the waterline reads negative, so a small
        /// positive tolerance still counts as surfaced; periscope depth does not.
        /// </summary>
        internal static bool IsSurfaced(float depthMeters,
            float tolerance = DefaultSurfaceToleranceMeters) =>
            !float.IsNaN(depthMeters) && depthMeters <= tolerance;

        /// <summary>The game label is localised; keep the letters the player already reads.</summary>
        internal static bool PrefersRussian(string labelText)
        {
            if (string.IsNullOrEmpty(labelText))
                return true;
            foreach (char letter in labelText)
                if (letter >= 'А' && letter <= 'я')
                    return true;
            return false;
        }
    }
}
