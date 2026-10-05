using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P6 sextant index error: clamp, apply, solve, persist.</summary>
    public sealed class IndexErrorCorrectionTests : System.IDisposable
    {
        public void Dispose()
        {
            IndexErrorCorrection.Clear();
        }

        [Fact]
        public void Clamp_LimitsToFiveArcMinutes()
        {
            Assert.Equal(5.0, IndexErrorCorrection.Clamp(9.0));
            Assert.Equal(-5.0, IndexErrorCorrection.Clamp(-9.0));
            Assert.Equal(0.0, IndexErrorCorrection.Clamp(double.NaN));
            Assert.Equal(0.4, IndexErrorCorrection.Clamp(0.4));
        }

        [Fact]
        public void Apply_AddsArcMinutesToDegrees()
        {
            Assert.Equal(45.0 + 0.4 / 60.0, IndexErrorCorrection.Apply(45.0, 0.4), 12);
            Assert.Equal(45.0 - 0.5 / 60.0, IndexErrorCorrection.Apply(45.0, -0.5), 12);
            // Clamped before use.
            Assert.Equal(45.0 + 5.0 / 60.0, IndexErrorCorrection.Apply(45.0, 99.0), 12);
        }

        [Fact]
        public void SolveFromKnownAltitude_RecoversTheOffset()
        {
            // Drum reads 45.00 while the body really is at 45.10 → +6' → clamped +5'.
            Assert.Equal(5.0, IndexErrorCorrection.SolveFromKnownAltitude(45.0, 45.1), 9);
            Assert.Equal(-2.0, IndexErrorCorrection.SolveFromKnownAltitude(45.05, 45.0166666667), 6);
            Assert.Equal(0.0, IndexErrorCorrection.SolveFromKnownAltitude(30.0, 30.0), 9);
        }

        [Fact]
        public void SaveLoad_Roundtrip_AndClear()
        {
            IndexErrorCorrection.Save(0.4);
            Assert.Equal(0.4, IndexErrorCorrection.Load(), 5);

            IndexErrorCorrection.Clear();
            Assert.Equal(0.0, IndexErrorCorrection.Load(), 9);
        }

        [Fact]
        public void Save_ClampsTheStoredValue()
        {
            IndexErrorCorrection.Save(42.0);
            Assert.Equal(5.0, IndexErrorCorrection.Load(), 5);
        }
    }
}
