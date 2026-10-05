using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Reference points agreed in the lighthouse visual model spec:
    /// A(1000)=0.5, A(10000)≈0.0307, T_clear(10km)≈0.819,
    /// I_vis(10km, clear)≈0.19, dense fog kills far light,
    /// dot ≤10px and streak ≤13px beyond 2 km.
    /// </summary>
    public sealed class LighthouseVisualModelTests
    {
        [Fact]
        public void DistanceAttenuation_ReferencePoints()
        {
            Assert.InRange(LighthouseVisualModel.DistanceAttenuation(0), 0.999, 1.001);
            Assert.InRange(LighthouseVisualModel.DistanceAttenuation(1000), 0.4995, 0.5005);
            Assert.InRange(LighthouseVisualModel.DistanceAttenuation(10000), 0.0306, 0.0307);
        }

        [Fact]
        public void Transmittance_ClearAir_10km_IsExpMinus02()
        {
            double t = LighthouseVisualModel.Transmittance(10000, 0.0);
            Assert.InRange(t, 0.818, 0.819);
        }

        [Fact]
        public void Transmittance_DenseFog_5km_NearlyZero()
        {
            double t = LighthouseVisualModel.Transmittance(5000, 1.0);
            Assert.InRange(t, 0.0067, 0.0068);
        }

        [Fact]
        public void VisualIntensity_10kmClearNight_About019()
        {
            double i = LighthouseVisualModel.VisualIntensity(10000, 0.0, 1.0, 1.0);
            Assert.InRange(i, 0.185, 0.195);
        }

        [Fact]
        public void VisualIntensity_MonotonicDecreasingWithDistance()
        {
            double prev = double.MaxValue;
            foreach (double d in new[] { 100.0, 500.0, 1000.0, 2000.0, 5000.0, 10000.0 })
            {
                double i = LighthouseVisualModel.VisualIntensity(d, 0.0, 1.0, 1.0);
                Assert.True(i < prev, "intensity not decreasing at " + d + "m");
                prev = i;
            }
        }

        [Fact]
        public void VisualIntensity_DenseFog_SuppressesFarLight()
        {
            double clear = LighthouseVisualModel.VisualIntensity(10000, 0.0, 1.0, 1.0);
            double fog = LighthouseVisualModel.VisualIntensity(10000, 1.0, 1.0, 1.0);
            Assert.True(fog < clear * 0.05, "dense fog should suppress far light");
        }

        [Fact]
        public void BeamProfile_PeakDeadAhead_FadesSmoothly()
        {
            // Gaussian main beam (FWHM ≈ 9.4°) + weak spill: continuous,
            // never a binary inside/outside cone.
            double deadAhead = LighthouseVisualModel.BeamIntensityFactor(1.0);
            double at1 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(1.0 * Math.PI / 180.0));
            double at5 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(5.0 * Math.PI / 180.0));
            double at8 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(8.0 * Math.PI / 180.0));
            double at15 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(15.0 * Math.PI / 180.0));
            double at30 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(30.0 * Math.PI / 180.0));
            double at45 = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(45.0 * Math.PI / 180.0));
            Assert.InRange(deadAhead, 0.999, 1.0);
            Assert.InRange(at1, 0.999, 1.0);
            Assert.InRange(at5, 0.25, 0.35);
            // Regression: the log case (beam 8° off) must stay visible.
            Assert.True(at8 > 0.05, "beam 8° off should remain visible, got " + at8);
            Assert.InRange(at15, 0.03, 0.06);
            Assert.True(at30 < 0.01, "beam 30° off should be nearly gone, got " + at30);
            Assert.True(at45 < 0.005, "beam 45° off should be gone, got " + at45);
        }

        [Fact]
        public void BeamProfile_MonotonicDecreasingWithAngle()
        {
            double prev = double.MaxValue;
            for (int deg = 0; deg <= 60; deg += 5)
            {
                double b = LighthouseVisualModel.BeamIntensityFactor(Math.Cos(deg * Math.PI / 180.0));
                Assert.True(b <= prev + 1e-9, "beam not decreasing at " + deg + "°");
                prev = b;
            }
        }

        [Fact]
        public void DotSizePx_FarClamp_Le10px_Beyond2km()
        {
            Assert.InRange(LighthouseVisualModel.DotSizePx(2000), 9.5, 10.0);
            Assert.InRange(LighthouseVisualModel.DotSizePx(10000), 9.5, 10.0);
        }

        [Fact]
        public void DotSizePx_NearField_LargeGlare_NoGlobalCap()
        {
            double near = LighthouseVisualModel.DotSizePx(100);
            Assert.True(near > 50.0, "near glare too small: " + near);
            double mid = LighthouseVisualModel.DotSizePx(600);
            Assert.True(mid > near * 0.5 && mid < near, "size should shrink gradually");
        }

        [Fact]
        public void DotSizePx_MonotonicDecreasing()
        {
            double prev = double.MaxValue;
            foreach (double d in new[] { 100.0, 400.0, 800.0, 1200.0, 1600.0, 2000.0 })
            {
                double s = LighthouseVisualModel.DotSizePx(d);
                Assert.True(s <= prev + 1e-9, "size not decreasing at " + d + "m");
                prev = s;
            }
        }

        [Fact]
        public void StreakLengthCapPx_13px_Beyond2km_150px_Near()
        {
            Assert.InRange(LighthouseVisualModel.StreakLengthCapPx(2000), 12.5, 13.0);
            Assert.InRange(LighthouseVisualModel.StreakLengthCapPx(10000), 12.5, 13.0);
            Assert.InRange(LighthouseVisualModel.StreakLengthCapPx(100), 149.5, 150.0);
        }

        [Fact]
        public void WorldLightIntensity_ReferencePoints_NoGamma()
        {
            // 100m clear: near-full ground pool.
            Assert.InRange(LighthouseVisualModel.WorldLightIntensity(100, 0.0), 0.960, 0.975);
            // 10km clear: A·T = 0.0307·0.819 ≈ 0.0251 — no stadium lighting.
            Assert.InRange(LighthouseVisualModel.WorldLightIntensity(10000, 0.0), 0.0250, 0.0252);
            // 2km dense fog: A·T = 0.261·e⁻² ≈ 0.0354.
            Assert.InRange(LighthouseVisualModel.WorldLightIntensity(2000, 1.0), 0.0353, 0.0355);
        }

        [Fact]
        public void WorldLightIntensity_MonotonicDecreasingWithDistance()
        {
            double prev = double.MaxValue;
            foreach (double d in new[] { 100.0, 500.0, 1000.0, 2000.0, 5000.0, 10000.0 })
            {
                double w = LighthouseVisualModel.WorldLightIntensity(d, 0.0);
                Assert.True(w < prev, "world light not decreasing at " + d + "m");
                prev = w;
            }
        }

        [Fact]
        public void GlowIntensity_ReferencePoints_SteadyLampFloor()
        {
            // Near: (1·1·0.06)^0.45 ≈ 0.282 — a clear steady point.
            Assert.InRange(LighthouseVisualModel.GlowIntensity(100, 0.0), 0.275, 0.290);
            // 1.75 km clear (Harlesiel regression): A=0.301·T=0.966 → ≈0.162.
            Assert.InRange(LighthouseVisualModel.GlowIntensity(1753, 0.0), 0.155, 0.170);
            // 25 km clear: still faintly visible (flareI ≈ 2·0.025).
            Assert.InRange(LighthouseVisualModel.GlowIntensity(25000, 0.0), 0.020, 0.032);
        }

        [Fact]
        public void GlowIntensity_MonotonicDecreasingWithDistance()
        {
            double prev = double.MaxValue;
            foreach (double d in new[] { 100.0, 500.0, 1000.0, 2000.0, 5000.0, 10000.0, 25000.0 })
            {
                double g = LighthouseVisualModel.GlowIntensity(d, 0.0);
                Assert.True(g < prev, "glow not decreasing at " + d + "m");
                prev = g;
            }
        }

        [Fact]
        public void GlowIntensity_DenseFog_SuppressesFarGlow()
        {
            double clear = LighthouseVisualModel.GlowIntensity(10000, 0.0);
            double fog = LighthouseVisualModel.GlowIntensity(10000, 1.0);
            Assert.True(fog < clear * 0.05, "dense fog should suppress far glow");
        }

        [Fact]
        public void GlowIntensity_BelowFlashBetweenBeams()
        {
            // The floor must not exceed a mid-strength flash so the beam
            // sweep stays the dominant signal.
            double flash = LighthouseVisualModel.VisualIntensity(1753, 0.0, 1.0, 1.0);
            double glow = LighthouseVisualModel.GlowIntensity(1753, 0.0);
            Assert.True(glow < flash, "glow floor must stay below the beam flash");
        }
    }
}