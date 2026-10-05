using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P6 burst averaging of the sextant arc.</summary>
    public sealed class SextantBurstSamplerTests
    {
        [Fact]
        public void Empty_DoesNotCommit()
        {
            var sampler = new SextantBurstSampler();
            Assert.False(sampler.TryCommit(out _, out _, out _));
        }

        [Fact]
        public void SingleSample_CommitsItself_WithZeroSigma()
        {
            var sampler = new SextantBurstSampler();
            sampler.Add(45.0);
            Assert.True(sampler.TryCommit(out double mean, out double sigma, out int rejected));
            Assert.Equal(45.0, mean, 9);
            Assert.Equal(0.0, sigma, 9);
            Assert.Equal(0, rejected);
        }

        [Fact]
        public void TightBurst_MeansAndReportsSigma()
        {
            var sampler = new SextantBurstSampler();
            sampler.Add(45.00);
            sampler.Add(45.01);
            sampler.Add(45.02);

            Assert.True(sampler.TryCommit(out double mean, out double sigma, out int rejected));
            Assert.InRange(mean, 45.0099, 45.0101);
            Assert.InRange(sigma, 0.55, 0.65); // 0.01° sample σ = 0.6′
            Assert.Equal(0, rejected);
            Assert.False(SextantBurstSampler.IsRough(sigma));
        }

        [Fact]
        public void WildSample_IsRejectedOnce()
        {
            var sampler = new SextantBurstSampler();
            sampler.Add(45.00);
            sampler.Add(45.01);
            sampler.Add(46.00); // a full degree off: far beyond 2σ

            Assert.True(sampler.TryCommit(out double mean, out double sigma, out int rejected));
            Assert.Equal(1, rejected);
            Assert.InRange(mean, 45.004, 45.006);
            Assert.True(sigma < 0.5);
        }

        [Fact]
        public void SwellSpread_FlagsRough()
        {
            var sampler = new SextantBurstSampler();
            sampler.Add(45.00);
            sampler.Add(45.05);
            sampler.Add(45.00);
            sampler.Add(45.05);
            sampler.Add(45.00);

            Assert.True(sampler.TryCommit(out _, out double sigma, out _));
            Assert.True(SextantBurstSampler.IsRough(sigma),
                "a 3′ spread must be flagged rough, sigma=" + sigma);
        }

        [Fact]
        public void Burst_IsCappedAtMaxSamples()
        {
            var sampler = new SextantBurstSampler();
            for (int i = 0; i < 10; i++)
                sampler.Add(45.0 + i * 0.001);
            Assert.Equal(SextantBurstSampler.MaxSamples, sampler.Count);
            Assert.True(sampler.IsFull);
        }

        [Fact]
        public void Clear_DropsTheBurst()
        {
            var sampler = new SextantBurstSampler();
            sampler.Add(45.0);
            sampler.Clear();
            Assert.Equal(0, sampler.Count);
            Assert.False(sampler.TryCommit(out _, out _, out _));
        }

        [Fact]
        public void MeanThenQuantize_StaysOnTheGrid()
        {
            // The plan's ordering: the sampler's committed mean is
            // Quantize(mean), not the mean of quantised taps. (Since
            // 2026-09-22 the recorded Ho comes from the drum; the mean is the
            // quality metric, and this ordering still holds for it.)
            var sampler = new SextantBurstSampler();
            sampler.Add(SextantArc.Quantize(45.001));
            sampler.Add(SextantArc.Quantize(45.004));
            sampler.Add(SextantArc.Quantize(45.007));

            Assert.True(sampler.TryCommit(out double mean, out _, out _));
            double committed = SextantArc.Quantize(mean);
            Assert.Equal(committed, SextantArc.Quantize(committed), 9);
            Assert.InRange(committed, 45.0, 45.01);
        }

        [Fact]
        public void Tap_SameSetting_Accumulates()
        {
            var sampler = new SextantBurstSampler();
            Assert.False(sampler.Tap(45.0));
            Assert.False(sampler.Tap(45.0));
            Assert.Equal(2, sampler.Count);
        }

        [Fact]
        public void Tap_DrumMove_RestartsBurst()
        {
            var sampler = new SextantBurstSampler();
            sampler.Tap(45.00);
            sampler.Tap(45.00);
            sampler.Tap(45.00);
            Assert.Equal(3, sampler.Count);

            Assert.True(sampler.Tap(45.10)); // 6' away: the drum moved
            Assert.Equal(1, sampler.Count);  // the old setting is dropped
        }

        [Fact]
        public void Tap_OneQuantumMove_RestartsBurst()
        {
            var sampler = new SextantBurstSampler();
            sampler.Tap(45.0);
            Assert.True(sampler.Tap(45.0 + 1.0 / 600.0));
            Assert.Equal(1, sampler.Count);
        }

        [Fact]
        public void Tap_SubQuantumJitter_KeepsBurst()
        {
            var sampler = new SextantBurstSampler();
            sampler.Tap(45.0);
            // 0.03': hand jitter on the same setting, not a drum move.
            Assert.False(sampler.Tap(45.0 + 0.0005));
            Assert.Equal(2, sampler.Count);
        }

        [Fact]
        public void Tap_Restart_DropsTheMixedSpread()
        {
            // A burst mixing two settings would report a huge sigma; after the
            // restart only the new setting is measured.
            var sampler = new SextantBurstSampler();
            sampler.Tap(45.00);
            sampler.Tap(45.00);
            sampler.Tap(46.00); // restart
            Assert.True(sampler.TryCommit(out _, out double sigma, out _));
            Assert.Equal(0.0, sigma, 9);
        }
    }
}
