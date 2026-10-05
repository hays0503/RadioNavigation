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
            string card = FixCardComposer.Compose(null, null, null, null, 0.0, false);

            Assert.StartsWith("\u0410\u0421\u0422\u0420\u041E\u041D\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042F", card);
            Assert.DoesNotContain("\u21161", card);
            Assert.DoesNotContain("\u0394t", card);
        }

        [Fact]
        public void Compose_FirstSight_AwaitsSecond()
        {
            string card = FixCardComposer.Compose(Obs(30.0), null, null, null, 0.0, false);

            Assert.Contains("\u21161", card);
            Assert.DoesNotContain("\u21162", card);
            Assert.Contains("(I)", card);
        }

        [Fact]
        public void Compose_BothSights_PassesThroughTail()
        {
            string card = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", "STAT", 0.0, false);

            Assert.Contains("\u21161", card);
            Assert.Contains("\u21162", card);
            Assert.Contains("SEC", card);
            Assert.Contains("STAT", card);
        }

        [Fact]
        public void Compose_Calibration_ShowsDelta()
        {
            string card = FixCardComposer.Compose(Obs(30.0), null, null, null, 2.5, false);

            Assert.Contains("\u0394t", card);
            Assert.Contains("2.5", card);
        }

        [Fact]
        public void Compose_AlwaysShowsAssumptionsFooter()
        {
            // v2.2: the game-assumptions note (refraction/parallax/dip) is
            // always on the card so the player knows the model's limits.
            string empty = FixCardComposer.Compose(null, null, null, null, 0.0, false);
            string full = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", null, 0.0, false);

            Assert.Contains("dip", empty);
            Assert.Contains("dip", full);
            Assert.Contains("P: ", full);
        }

        [Fact]
        public void Compose_StarSight_ShowsBodyName()
        {
            SextantObservation vega =
                ObsBody(CelestialBody.Star(91262, "Vega"), 30.0);

            string card = FixCardComposer.Compose(vega, null, null, null, 0.0, false);

            Assert.Contains("Vega", card);
            Assert.Contains("\u21161", card);
        }

        [Fact]
        public void Compose_MoonSight_ShowsAccuracyFlag()
        {
            SextantObservation moon = ObsBody(CelestialBody.Moon(), 30.0);

            string card = FixCardComposer.Compose(moon, null, null, null, 0.0, false);

            Assert.Contains("\u041B\u0443\u043D\u0430 \u00B10.1\u00B0", card);
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

        [Fact]
        public void ComposeThirdSuggestion_WithName_ShowsNameAndMin()
        {
            string line = FixCardComposer.ComposeThirdSuggestion("Vega", 44.6);

            Assert.StartsWith(FixCardComposer.FixCardRecommendThird, line);
            Assert.Contains("Vega", line);
            Assert.Contains("\u043C\u0438\u043D 45\u00B0", line);
        }

        [Fact]
        public void ComposeThirdSuggestion_NoName_GenericFallback()
        {
            Assert.Equal(FixCardComposer.FixCardRecommendThird,
                FixCardComposer.ComposeThirdSuggestion(null, 0.0));
            Assert.Equal(FixCardComposer.FixCardRecommendThird,
                FixCardComposer.ComposeThirdSuggestion("", 12.0));
        }

        [Fact]
        public void CompactObservation_OneLineWithTheEssentials()
        {
            // 2026-09-22: the third sight is a single line — the card has to
            // fit the stacked column. The full F-Tafel chain stays on №1/№2.
            SextantObservation vega = ObsBody(CelestialBody.Star(91262, "Vega"), 30.0);

            string line = FixCardComposer.ComposeObservationCompact(vega, 3, false);

            Assert.DoesNotContain("\n", line);
            Assert.Contains("\u21163", line);
            Assert.Contains("Vega", line);
            Assert.Contains("hb ", line);
            Assert.Contains("h1 ", line);
            Assert.Contains("Az ", line);
            Assert.Contains(" a ", line);
            Assert.Contains("NM", line);
            Assert.DoesNotContain("SHA", line);
            Assert.DoesNotContain("UT", line);
        }

        [Fact]
        public void CompactObservation_IncludesUtWhenAsked()
        {
            string line = FixCardComposer.ComposeObservationCompact(Obs(30.0), 3, true);
            Assert.Contains("UT 12:00:00", line);
        }

        [Fact]
        public void CompactObservation_FitsOneCardRow()
        {
            // A long navigational name must still fit a single 380-unit row
            // (about 63 characters at the base font size).
            SextantObservation obs =
                ObsBody(CelestialBody.Star(677, "Alpheratz"), 30.0);
            string line = FixCardComposer.ComposeObservationCompact(obs, 3, false);
            Assert.True(line.Length <= 60, "compact line is " + line.Length + " chars: " + line);
        }

        [Fact]
        public void Compose_CompactMode_OneLinePerObservation()
        {
            // 2026-09-22: when the column cannot fit the full blocks the
            // observations collapse to one line each (the section keeps the
            // geometry/FIX detail).
            string full = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", null, 0.0, false);
            string compact = FixCardComposer.Compose(Obs(30.0), Obs(32.0), "SEC", null, 0.0, true);

            Assert.Contains("SHA", full);
            Assert.DoesNotContain("SHA", compact);
            Assert.Contains("\u21161", compact);
            Assert.Contains("\u21162", compact);
            Assert.Contains("SEC", compact);
            Assert.True(compact.Length < full.Length,
                "compact card must be shorter: " + compact.Length + " vs " + full.Length);
        }
    }
}
