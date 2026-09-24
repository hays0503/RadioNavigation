using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Ocular stabilization policy (2026-11-04): the aim is RAW + a washout
    /// offset (no yaw low-pass — the mouse must stay 1:1), the freeze offset
    /// bleeds out slowly, and the exponential step behaves like a first-order
    /// low-pass (used by the heave). Pins the constants, the guards and the
    /// convergence.
    /// </summary>
    public sealed class SextantStabilizationTests
    {
        [Fact]
        public void WashoutTau_IsSlowAndPositive()
        {
            // The offset bleed must be imperceptible: ~3%/s, never a slide.
            Assert.True(SextantStabilization.WashoutTauSeconds >= 20f,
                "the washout must be slow enough to be invisible");
        }

        [Fact]
        public void DecayOffset_Reaches63PercentAfterOneTau()
        {
            float value = SextantStabilization.DecayOffset(1f,
                SextantStabilization.WashoutTauSeconds);
            Assert.InRange(value, 0.36f, 0.38f);
        }

        [Fact]
        public void DecayOffset_KeepsTheSign()
        {
            // A negative offset decays toward zero from below (no flip).
            float value = SextantStabilization.DecayOffset(-1f,
                SextantStabilization.WashoutTauSeconds);
            Assert.InRange(value, -0.38f, -0.36f);
        }

        [Fact]
        public void DecayOffset_Guards()
        {
            Assert.Equal(0f, SextantStabilization.DecayOffset(0f, 1f));
            Assert.Equal(0f, SextantStabilization.DecayOffset(float.NaN, 1f));
            Assert.Equal(0.5f, SextantStabilization.DecayOffset(0.5f, 0f), 5);
            Assert.Equal(0.5f, SextantStabilization.DecayOffset(0.5f, float.NaN), 5);
            Assert.Equal(0.5f, SextantStabilization.DecayOffset(0.5f, -1f), 5);
        }

        [Fact]
        public void DecayOffset_ConvergesToZero()
        {
            // 300 s = 10 washout time constants -> the offset is gone.
            float offset = 5f;
            for (int i = 0; i < 3000; i++)
                offset = SextantStabilization.DecayOffset(offset, 0.1f);
            Assert.InRange(offset, -0.01f, 0.01f);
        }

        [Fact]
        public void OffsetUsable_WithinTheLimits()
        {
            Assert.True(SextantStabilization.OffsetUsable(0f, 20f));
            Assert.True(SextantStabilization.OffsetUsable(-19.9f, 20f));
            Assert.True(SextantStabilization.OffsetUsable(20f, 20f));
            Assert.False(SextantStabilization.OffsetUsable(20.1f, 20f));
            Assert.False(SextantStabilization.OffsetUsable(-25f, 20f));
            Assert.False(SextantStabilization.OffsetUsable(float.NaN, 20f));
            Assert.False(SextantStabilization.OffsetUsable(1f, 0f));
        }

        [Fact]
        public void OffsetLimits_CoverTheBoatMotion()
        {
            // The wobble is a few degrees; the limit must be well above that
            // and well below a teleport-sized jump.
            Assert.True(SextantStabilization.MaxYawOffsetDeg >= 10f);
            Assert.True(SextantStabilization.MaxYawOffsetDeg <= 45f);
        }

        [Fact]
        public void AdvanceIdle_AccumulatesWhileTheMouseIsStill()
        {
            float idle = SextantStabilization.AdvanceIdle(0f, 0f, 0.1f);
            Assert.Equal(0.1f, idle, 5);
            idle = SextantStabilization.AdvanceIdle(idle,
                SextantStabilization.MouseIdleEpsilon, 0.1f);
            Assert.Equal(0.2f, idle, 5);
        }

        [Fact]
        public void AdvanceIdle_ResetsOnRealMotion()
        {
            float idle = SextantStabilization.AdvanceIdle(1f,
                SextantStabilization.MouseIdleEpsilon * 2f, 0.1f);
            Assert.Equal(0f, idle);
        }

        [Fact]
        public void AdvanceIdle_IsCapped()
        {
            float idle = SextantStabilization.AdvanceIdle(
                SextantStabilization.MaxIdleSeconds, 0f, 1f);
            Assert.Equal(SextantStabilization.MaxIdleSeconds, idle);
        }

        [Fact]
        public void AdvanceIdle_InvalidInputs_KeepTheCurrent()
        {
            Assert.Equal(0.5f, SextantStabilization.AdvanceIdle(0.5f, 0f, 0f), 5);
            Assert.Equal(0.5f, SextantStabilization.AdvanceIdle(0.5f, 0f, float.NaN), 5);
            // NaN/negative idle starts from zero and keeps counting.
            Assert.Equal(0.1f, SextantStabilization.AdvanceIdle(float.NaN, 0f, 0.1f), 5);
            Assert.Equal(0.1f, SextantStabilization.AdvanceIdle(-1f, 0f, 0.1f), 5);
        }

        [Fact]
        public void ShouldLock_AfterTheIdleWindow()
        {
            Assert.False(SextantStabilization.ShouldLock(0f));
            Assert.False(SextantStabilization.ShouldLock(
                SextantStabilization.LockSeconds - 0.01f));
            Assert.True(SextantStabilization.ShouldLock(
                SextantStabilization.LockSeconds));
            Assert.True(SextantStabilization.ShouldLock(
                SextantStabilization.LockSeconds + 1f));
            Assert.False(SextantStabilization.ShouldLock(float.NaN));
        }

        [Fact]
        public void LockPrefsKey_IsStable()
        {
            Assert.Equal("RadioNav.StabLock", SextantStabilization.LockPrefsKey);
        }

        [Fact]
        public void Smooth_ZeroTau_SnapsToTheTarget()
        {
            Assert.Equal(10f, SextantStabilization.Smooth(0f, 10f, 0f, 0.016f));
            Assert.Equal(10f, SextantStabilization.Smooth(0f, 10f, -1f, 0.016f));
            Assert.Equal(10f, SextantStabilization.Smooth(0f, 10f, float.NaN, 0.016f));
        }

        [Fact]
        public void Smooth_NonPositiveDt_KeepsTheCurrent()
        {
            Assert.Equal(3f, SextantStabilization.Smooth(3f, 10f, 0.2f, 0f));
            Assert.Equal(3f, SextantStabilization.Smooth(3f, 10f, 0.2f, -0.1f));
            Assert.Equal(3f, SextantStabilization.Smooth(3f, 10f, 0.2f, float.NaN));
        }

        [Fact]
        public void Smooth_Reaches63PercentAfterOneTau()
        {
            // First-order response: after one time constant ~63.2% of the step.
            float value = SextantStabilization.Smooth(0f, 100f, 0.5f, 0.5f);
            Assert.InRange(value, 62.0f, 64.0f);
        }

        [Fact]
        public void Smooth_ConvergesToTheTarget()
        {
            float value = 0f;
            for (int i = 0; i < 200; i++)
                value = SextantStabilization.Smooth(value, 42f, 0.2f, 0.016f);
            Assert.InRange(value, 41.9f, 42.0f);
        }

        [Fact]
        public void Smooth_IsMonotoneTowardTheTarget()
        {
            float value = 0f;
            for (int i = 0; i < 50; i++)
            {
                float next = SextantStabilization.Smooth(value, 10f, 0.2f, 0.016f);
                Assert.True(next >= value && next <= 10f, "must approach the target monotonically");
                value = next;
            }
        }

        [Fact]
        public void PrefsKey_IsStable()
        {
            Assert.Equal("RadioNav.Stabilization", SextantStabilization.PrefsKey);
        }
    }
}
