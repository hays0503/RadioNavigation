using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Spawn/despawn hysteresis for the 3D lighthouse fleet (pure C#,
    /// offline-tested). The map marker layer always draws every registry
    /// profile; the expensive 3D prefab + rotator + glow only exist while
    /// the lighthouse is close to the submarine:
    ///   - spawn when distance <= SpawnRadiusKm (30 km),
    ///   - despawn when distance > DespawnRadiusKm (33 km),
    ///   - the 3 km band between the two prevents spawn/despawn thrash
    ///     while sailing along the radius boundary.
    /// </summary>
    public enum LighthouseGateAction
    {
        Keep,
        Spawn,
        Despawn
    }

    public static class LighthouseDistanceGate
    {
        public const float SpawnRadiusKm = 30f;
        public const float DespawnRadiusKm = 33f;

        public static LighthouseGateAction Decide(bool spawned, double distanceKm)
        {
            if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm))
            {
                return spawned ? LighthouseGateAction.Despawn : LighthouseGateAction.Keep;
            }

            if (!spawned && distanceKm <= SpawnRadiusKm)
            {
                return LighthouseGateAction.Spawn;
            }

            if (spawned && distanceKm > DespawnRadiusKm)
            {
                return LighthouseGateAction.Despawn;
            }

            return LighthouseGateAction.Keep;
        }

        /// <summary>
        /// Flat (encounter-space) distance in km between two encounter-space
        /// kilometer positions. Encounter space is a flat equirectangular
        /// projection, so plain Pythagoras is exact here.
        /// </summary>
        public static double DistanceKm(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
