using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Shared, measurable limb for the Sun and Moon textures.
    /// Half coverage is AT the physical radius, with only one texel of AA.
    /// Padding allows the outer half of the ramp without clipping the quad.</summary>
    public static class CelestialDiscRaster
    {
        public const double LimbRadius = 0.96;

        public static double Coverage(double radius, int textureSize)
        {
            if (double.IsNaN(radius) || double.IsInfinity(radius) || textureSize < 4)
                return 0.0;
            return Math.Max(0.0, Math.Min(1.0,
                0.5 + (LimbRadius - radius) * textureSize * 0.5));
        }

        public static double QuadSide(double distance, double angularDiameterDeg)
        {
            return 2.0 * distance * Math.Tan(angularDiameterDeg * Math.PI / 360.0)
                / LimbRadius;
        }
    }
}
