namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Computes the world-space XZ shift that places a spawned lighthouse at
    /// its charted lat/lon, anchored to the player ship. The game's
    /// scene-origin system compensates floating origin per spawn epoch, so a
    /// sandbox group spawned mid-session lands offset by an epoch-dependent
    /// amount (observed in-game: hundreds of meters, different on every
    /// respawn). The player ship always lives in the current compensated
    /// frame, so expressing the target relative to the ship makes the
    /// placement frame-independent. Pure float math — offline tested.
    /// </summary>
    public static class LighthouseAlignment
    {
        /// <summary>
        /// Shift (meters, world XZ) to add to the spawn container position so
        /// the lamp pivot lands at the charted position. Ship and rotator
        /// positions are world meters; ship/light positions are encounter
        /// kilometers (Coordinates.ToWorldPosition convention: x=lon·111.32,
        /// y=lat·111.32). Returns zero when already aligned.
        /// </summary>
        public static void ComputeShift(
            float shipWorldX, float shipWorldZ,
            float shipKmX, float shipKmY,
            float lightKmX, float lightKmY,
            float rotatorWorldX, float rotatorWorldZ,
            out float shiftX, out float shiftZ)
        {
            // Encounter space is a flat equirectangular projection where
            // 1 km of x/y maps to 1000 m of world x/z (the game multiplies
            // encounter kilometers by 1000 when placing scene content).
            float dx = (lightKmX - shipKmX) * 1000f;
            float dz = (lightKmY - shipKmY) * 1000f;
            shiftX = shipWorldX + dx - rotatorWorldX;
            shiftZ = shipWorldZ + dz - rotatorWorldZ;
        }
    }
}
