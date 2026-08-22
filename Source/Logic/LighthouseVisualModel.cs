using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure perceptual model of the lighthouse light as seen by the observer.
    /// Deliberately free of UnityEngine references so it can be unit-tested
    /// without Unity (xunit suite compiles it directly).
    ///
    /// Two independent channels:
    /// Brightness: I_visual = (I0 * A(r) * B_wide * B_beam * T(r)) ^ gamma,
    ///   where I0 is normalized to 1 — visual layers apply their own scale
    ///   constants (Core/Halo/Streak alphas, FlareIntensityScale).
    /// Size: S = Smin + (Smax - Smin) * A^0.3 — shrinks slower than brightness
    ///   (the eye keeps a distant source as a small bright point), followed by
    ///   a far clamp introduced smoothly between FarClampStartM..FarClampEndM.
    /// </summary>
    internal static class LighthouseVisualModel
    {
        // Distance attenuation A(r) = 1 / (1 + (r/R)^p) — playable curve,
        // NOT physical 1/r² (which would kill the light long before 10 km).
        public const double DistAttenRadiusM = 1000.0;
        public const double DistAttenPower = 1.5;

        // Atmosphere transmittance T(r) = exp(-sigma * r); sigma interpolated
        // by game Fogginess [0..1] (clear air .. dense fog).
        public const double SigmaClear = 0.00002;
        public const double SigmaDenseFog = 0.001;

        // Display gamma: compensates monitor/eye non-linear response so the
        // distant beacon fades into a faint point instead of vanishing early.
        public const double DisplayGamma = 0.45;

        // Beam profile: continuous intensity vs angle off the beam axis,
        // based on historical Fresnel optics (horizontal divergence ~5-10°
        // FWHM) plus a weak wide optical spill. A sum of two Gaussians —
        // never a binary inside/outside cone, so the light fades smoothly
        // instead of vanishing when the beam drifts a few degrees.
        public const double MainBeamSigmaDeg = 4.0;   // FWHM ≈ 9.4° — the bright flash
        public const double SpillPeak = 0.08;         // weak spill at 0° (~8% of peak)
        public const double SpillSigmaDeg = 20.0;     // spill reaches ~30-40°, then fades

        // Size channel constants (pixels).
        public const double SizeShrinkPower = 0.3;
        public const double SizeMinPx = 8.0;
        public const double SizeMaxPx = 80.0;

        // Far clamp zone: hard pixel caps fade in between these distances.
        public const double FarClampStartM = 800.0;
        public const double FarClampEndM = 2000.0;
        public const double FarDotMaxPx = 10.0;
        public const double FarStreakMaxPx = 13.0;
        public const double StreakNearMaxPx = 150.0;

        public static double DistanceAttenuation(double distanceM)
        {
            if (distanceM < 0.0)
            {
                distanceM = 0.0;
            }
            double t = Math.Pow(distanceM / DistAttenRadiusM, DistAttenPower);
            return 1.0 / (1.0 + t);
        }

        public static double Sigma(double fogginess01)
        {
            double f = Clamp01(fogginess01);
            return SigmaClear + (SigmaDenseFog - SigmaClear) * f;
        }

        public static double Transmittance(double distanceM, double fogginess01)
        {
            if (distanceM < 0.0)
            {
                distanceM = 0.0;
            }
            return Math.Exp(-Sigma(fogginess01) * distanceM);
        }

        /// <summary>
        /// Continuous beam intensity vs |dot(cameraDir, beamForward)|:
        /// a narrow bright main beam (Gaussian, FWHM ≈ 9.4°) plus a weak
        /// wide optical spill. Returns ~1.0 dead ahead, ~0.09 at 8°,
        /// fading to near zero beyond ~40° — the light never snaps off.
        /// </summary>
        public static double BeamIntensityFactor(double absDotCamBeam)
        {
            double d = absDotCamBeam < -1.0 ? -1.0 : (absDotCamBeam > 1.0 ? 1.0 : absDotCamBeam);
            double angleDeg = Math.Acos(d) * 180.0 / Math.PI;
            double main = Math.Exp(-(angleDeg / MainBeamSigmaDeg) * (angleDeg / MainBeamSigmaDeg));
            double spill = SpillPeak * Math.Exp(-(angleDeg / SpillSigmaDeg) * (angleDeg / SpillSigmaDeg));
            return Math.Min(1.0, main + spill);
        }

        /// <summary>
        /// Unified visual intensity feeding every render layer
        /// (flare intensity, core/halo/streak alphas).
        /// </summary>
        public static double VisualIntensity(double distanceM, double fogginess01, double wideGate01, double absDotCamBeam)
        {
            double i = DistanceAttenuation(distanceM)
                * Transmittance(distanceM, fogginess01)
                * Clamp01(wideGate01)
                * BeamIntensityFactor(absDotCamBeam);
            return Math.Pow(i, DisplayGamma);
        }

        /// <summary>
        /// Perceptual core dot size in pixels. Beyond FarClampEndM the result
        /// never exceeds FarDotMaxPx; before FarClampStartM there is no cap.
        /// </summary>
        public static double DotSizePx(double distanceM)
        {
            double a = DistanceAttenuation(distanceM);
            double perceptual = SizeMinPx + (SizeMaxPx - SizeMinPx) * Math.Pow(a, SizeShrinkPower);
            double cap = SizeMaxPx + (FarDotMaxPx - SizeMaxPx) * FarClampT(distanceM);
            return Math.Min(perceptual, cap);
        }

        /// <summary>Distance-dependent cap for the horizontal streak length.</summary>
        public static double StreakLengthCapPx(double distanceM)
        {
            return StreakNearMaxPx + (FarStreakMaxPx - StreakNearMaxPx) * FarClampT(distanceM);
        }

        /// <summary>
        /// World illumination channel for the Spot Light — what the beam
        /// actually lights on terrain/water around the lighthouse, as seen
        /// from the observer's distance. Physical attenuation only:
        /// A(r)·T(r), NO display gamma and NO cones (the rotating cone is
        /// Unity's own spotlight transform). Keeps a distant lighthouse from
        /// floodlighting the landscape like a stadium.
        /// </summary>
        public static double WorldLightIntensity(double distanceM, double fogginess01)
        {
            return DistanceAttenuation(distanceM) * Transmittance(distanceM, fogginess01);
        }

        private static double FarClampT(double distanceM)
        {
            return SmoothStep(FarClampStartM, FarClampEndM, distanceM);
        }

        private static double Clamp01(double v)
        {
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }

        /// <summary>Hermite smoothstep, same semantics as Mathf.SmoothStep.</summary>
        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }
    }
}