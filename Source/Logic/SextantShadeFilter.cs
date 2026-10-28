using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Optical transmission, before HDR tone mapping, not a black
    /// overlay on an already clipped image. Stacked amber glasses multiply
    /// light; zero glasses is the unchanged camera. No geometry or ephemeris.</summary>
    public static class SextantShadeFilter
    {
        public const double GlassTransmission = 0.35;
        // Fixed source radiance, NOT per-filter gain or eye adaptation. The
        // strongest (three-glass) reference transmits the texture's unit centre
        // radiance. Without this HDR calibration the unit-LDR solar texture is
        // darker than the game's >1 HDR atmosphere (GPU 2026-10-13).
        public const double SolarSceneRadiance = 1.0 /
            (GlassTransmission * GlassTransmission * GlassTransmission);
        private const double GreenPerGlass = 0.85;
        private const double BluePerGlass = 0.50;

        public static bool IsProbeRadiance(double value)
        {
            // Diagnostic range only; no zero/NaN source may enter a camera.
            return !double.IsNaN(value) && !double.IsInfinity(value)
                && value >= 0.125 && value <= 64.0;
        }

        public static int ClampCount(int count)
        {
            return Math.Max(0, Math.Min(3, count));
        }

        public static double Transmission(int count)
        {
            return Math.Pow(GlassTransmission, ClampCount(count));
        }

        public static double ExposureEv(int count)
        {
            return Math.Log(Transmission(count), 2.0);
        }

        public static double Green(int count)
        {
            return Math.Pow(GreenPerGlass, ClampCount(count));
        }

        public static double Blue(int count)
        {
            return Math.Pow(BluePerGlass, ClampCount(count));
        }
    }
}
