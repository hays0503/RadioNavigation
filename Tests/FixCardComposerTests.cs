using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// FixCardComposer contract: the F-Tafel worksheet card text behind
    /// SextantWorksheet.BuildCard. Extracted so card assembly runs offline
    /// on synthetic observations.
    /// </summary>
    public sealed class FixCardComposerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

        private static SextantObservation Obs(double hoDeg)
        {
            return RunningFixCalculator.BuildObservation(T0, hoDeg, 50.0, 0.0);
        }

        [Fact]
        public void Compose_NoSights_ShowsTitleAndHint()
        {
            string card = FixCardComposer.Compose(null, null, null, null, 0.0);

            Assert.StartsWith("\u0410\u0421\u0422\u0420\u041E\u041D\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042F", card);
            Assert.DoesNotContain("\u21161", card);
            Assert.DoesNotContain("\u0394t", card);
        }

        [Fact]
        public void Compose_FirstSight_AwaitsSecond()
        {
            string card = FixCardComposer.Compose(Obs(30.0), null, null, null, 0.0);

            Assert.Contains("\u21161", card);
            Assert.DoesNotContain("\u21162", card);
            Assert.Contains("(I)", card);
        }

        [Fact]
        public void Compose_BothSights_PassesThroughTail()
        {
            string card = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", "STAT", 0.0);

            Assert.Contains("\u21161", card);
            Assert.Contains("\u21162", card);
            Assert.Contains("SEC", card);
            Assert.Contains("STAT", card);
        }

        [Fact]
        public void Compose_Calibration_ShowsDelta()
        {
            string card = FixCardComposer.Compose(Obs(30.0), null, null, null, 2.5);

            Assert.Contains("\u0394t", card);
            Assert.Contains("2.5", card);
        }
    }
}
