namespace UBOAT.Mods.RadioNavigation
{
    // F200: world units <-> degrees, exactly mirrored by the game's
    // WorldPosition (x = longitude, y = latitude; 1 unit = 1/111.3198888888889 degree).
    // Pure static so the fix pipeline stays testable without the game installed.
    public static class GeoPositionConverter
    {
        // 1 / 111.3198888888889 — matches WorldPosition.Latitude/Longitude.
        public const double DegreesPerWorldUnit = 0.008983120716174308;

        public static void ToLatLong(float worldX, float worldY, out float latitude, out float longitude)
        {
            latitude = (float)(worldY * DegreesPerWorldUnit);
            longitude = (float)(worldX * DegreesPerWorldUnit);
        }
    }
}
