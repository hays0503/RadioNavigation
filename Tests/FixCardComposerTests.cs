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

        private static SextantObservation ObsBody(CelestialBody body, double hoDeg)
        {
            return RunningFixCalculator.BuildObservation(T0, hoDeg, 50.0, 0.0, body);
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

        [Fact]
        public void Compose_AlwaysShowsAssumptionsFooter()
        {
            // v2.2: the game-assumptions note (refraction/parallax/dip) is
            // always on the card so the player knows the model's limits.
            string empty = FixCardComposer.Compose(null, null, null, null, 0.0);
            string full = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", null, 0.0);

            Assert.Contains("dip", empty);
            Assert.Contains("dip", full);
            Assert.Contains("P: ", full);
        }

        [Fact]
        public void Compose_StarSight_ShowsBodyName()
        {
            SextantObservation vega =
                ObsBody(CelestialBody.Star(91262, "Vega"), 30.0);

            string card = FixCardComposer.Compose(vega, null, null, null, 0.0);

            Assert.Contains("Vega", card);
            Assert.Contains("\u21161", card);
        }

        [Fact]
        public void Compose_MoonSight_ShowsAccuracyFlag()
        {
            SextantObservation moon = ObsBody(CelestialBody.Moon(), 30.0);

            string card = FixCardComposer.Compose(moon, null, null, null, 0.0);

            Assert.Contains("\u041B\u0443\u043D\u0430 \u00B10.3\u00B0", card);
        }

        [Fact]
        public void GeometryAdvice_ContextAware()
        {
            // Two different bodies: pick another azimuth. Same body twice:
            // separate the sights in time. Not shallow -> no advice.
            Assert.Contains("\u043F\u043E \u0430\u0437\u0438\u043C\u0443\u0442\u0443",
                FixCardComposer.GeometryAdvice(10.0, false));
            Assert.Contains("\u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438",
                FixCardComposer.GeometryAdvice(10.0, true));
            Assert.Null(FixCardComposer.GeometryAdvice(15.0, true));
        }

        [Fact]
        public void CoincidentMessage_ContextAware()
        {
            Assert.Contains("\u043F\u043E \u0430\u0437\u0438\u043C\u0443\u0442\u0443",
                FixCardComposer.CoincidentMessage(false));
            Assert.Contains("\u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438",
                FixCardComposer.CoincidentMessage(true));
        }
    }
}
