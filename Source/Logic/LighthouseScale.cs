using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-lighthouse model scale (pure C#, offline-tested). The mod ships a
    /// single "lighthouse" prefab whose lamp pivot sits H0 = 114.47 m above
    /// the base (measured in game by rn_lighthouse_height); the Leuchtfeuer
    /// DB carries real fire heights from 5 m pier lights to 63 m towers, so
    /// each spawned instance is uniformly scaled to
    ///   scale = clamp(fireHeightM / lampHeightM, MinScale, MaxScale)
    /// which lands the lamp exactly at the real height — unless the real
    /// height is tiny: a 6-m pier light would scale the 114-m prefab body to
    /// a sub-pixel sliver invisible beyond ~50 m. MinScale floors the tower
    /// at ~40 m so it stays a visible seamark; the lamp then rides higher
    /// than the charted fire height, which only slightly shifts the glow
    /// elevation (bearing math is horizontal and unaffected).
    /// </summary>
    public static class LighthouseScale
    {
        /// <summary>Measured lamp-pivot height of the unscaled prefab (m).</summary>
        public const float DefaultLampHeightM = 114.47f;

        public const float MinScale = 0.35f;
        public const float MaxScale = 1.5f;

        public static float Compute(float fireHeightM, float lampHeightM)
        {
            if (IsBad(fireHeightM) || IsBad(lampHeightM))
            {
                return 1f;
            }

            float scale = fireHeightM / lampHeightM;
            if (scale < MinScale)
            {
                return MinScale;
            }

            if (scale > MaxScale)
            {
                return MaxScale;
            }

            return scale;
        }

        private static bool IsBad(float value)
        {
            return value <= 0f || float.IsNaN(value) || float.IsInfinity(value);
        }
    }
}
