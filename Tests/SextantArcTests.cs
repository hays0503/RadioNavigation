using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // SEXTANT_PLAN §8: clamp both sides, quantum 12″, marine format string.
    public class SextantArcTests
    {
        [Fact]
        public void Clamp_Negative_ToZero()
        {
            Assert.Equal(0.0, SextantArc.Clamp(-5.0));
        }

        [Fact]
        public void Clamp_OverMax_To130()
        {
            Assert.Equal(130.0, SextantArc.Clamp(200.0));
        }

        [Fact]
        public void Clamp_Middle_Unchanged()
        {
            Assert.Equal(45.0, SextantArc.Clamp(45.0));
        }

        [Fact]
        public void Quantize_HalfStep_RoundsToTwelveArcSeconds()
        {
            Assert.Equal(1.0 / 300.0, SextantArc.Quantize(0.005), 12);
        }

        [Fact]
        public void Quantize_WholeDegree_Unchanged()
        {
            Assert.Equal(1.0, SextantArc.Quantize(1.0), 12);
        }

        [Fact]
        public void Quantize_NearMax_ClampsTo130()
        {
            Assert.Equal(130.0, SextantArc.Quantize(129.9999), 12);
        }

        [Fact]
        public void Format_MarineReading_InvariantCulture()
        {
            string s = SextantArc.Format(45.0 + 23.4 / 60.0);
            Assert.Equal("45\u00B023.4\u2032", s);
        }

        // Fine adjustment (Ctrl+arrows): one drum tick = 0.2' = 1/300° —
        // exactly one display quantum, so steps never drift off-grid.
        [Fact]
        public void FineStep_OneTick_IsOneQuantum()
        {
            double tick = 1.0 / SextantArc.StepsPerDeg;
            Assert.Equal(1.0 / 300.0, tick, 12);
        }

        [Fact]
        public void FineStep_ThreeTicks_FromZero_ShowsPointSixMinutes()
        {
            double q = 0.0;
            for (int i = 0; i < 3; i++)
                q = SextantArc.Quantize(q + 1.0 / SextantArc.StepsPerDeg);
            Assert.Equal(0.6 / 60.0, q, 12);
            Assert.Equal("0\u00B000.6\u2032", SextantArc.Format(q));
        }

        [Fact]
        public void FineStep_TickFromReading_StaysOnGrid()
        {
            double q = SextantArc.Quantize(45.0 + 23.4 / 60.0);
            q = SextantArc.Quantize(q + 1.0 / SextantArc.StepsPerDeg);
            Assert.Equal(45.0 + 23.6 / 60.0, q, 12);
            Assert.Equal("45\u00B023.6\u2032", SextantArc.Format(q));
        }
    }
}
