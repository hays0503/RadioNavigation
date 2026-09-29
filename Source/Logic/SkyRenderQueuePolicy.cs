using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sky render-queue policy. UniStorm's volumetric cloud dome (~5.5 km)
    /// is farther than the mod's ~3.7 km celestial shell, so Unity draws it
    /// first (transparent sorting is back-to-front) and the clouds write no
    /// depth — our quads then paint over them. Lowering our material queue
    /// below the cloud queue makes the cloud pass blend OVER the sky. The
    /// queue is a live-switchable override (<see cref="SkyRenderQueueSwitch"/>)
    /// so it can be tuned without a game restart.
    ///
    /// The decision math (clamp/off) is kept in the pure <see cref="Clamp"/>
    /// method so it is unit-testable; <see cref="TryApply"/> is the Unity
    /// write.
    /// </summary>
    internal static class SkyRenderQueuePolicy
    {
        /// <summary>Override off: leave the material's own render queue untouched.</summary>
        internal const int Off = 0;

        /// <summary>Geometry queue floor — below this lies background/skybox territory.</summary>
        internal const int MinQueue = 1000;

        /// <summary>Upper bound stays inside the Transparent queue band (3000+).</summary>
        internal const int MaxQueue = 3500;

        /// <summary>
        /// Pure clamp. <see cref="Off"/> (0) and every negative value stay 0
        /// (the override is disabled); everything else lands in
        /// [MinQueue, MaxQueue].
        /// </summary>
        public static int Clamp(int queue)
        {
            if (queue <= Off)
                return Off;
            if (queue < MinQueue)
                return MinQueue;
            if (queue > MaxQueue)
                return MaxQueue;
            return queue;
        }

        /// <summary>
        /// Applies the override to a material. Returns false and does nothing
        /// when the material is missing or the override is off; otherwise
        /// writes renderQueue only when the clamped value differs.
        /// </summary>
        public static bool TryApply(Material material, int queue)
        {
            if (material == null || queue <= Off)
                return false;

            int clamped = Clamp(queue);
            if (material.renderQueue != clamped)
                material.renderQueue = clamped;
            return true;
        }
    }
}
