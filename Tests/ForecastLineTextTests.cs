using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Forecast line assembly (2026-09-22): the triple replaces the pair and
    /// goes on its own line; the column is 440 units ≈ 70 characters.
    /// </summary>
    public sealed class ForecastLineTextTests
    {
        private static readonly DateTime When = new DateTime(1942, 6, 15, 21, 10, 0, DateTimeKind.Utc);

        [Fact]
        public void PairSegment_MatchesLegacyFormat()
        {
            Assert.Equal("\u041F\u0430\u0440\u0430: Deneb+Kochab (\u0394Az 87\u00B0) \u0432 21:10",
                ForecastLineText.PairSegment("Deneb", "Kochab", 87.4, When));
        }

        [Fact]
        public void TripleSegment_ShowsThreeNamesMinAndTime()
        {
            string s = ForecastLineText.TripleSegment("Deneb", "Kochab", "Schedar", 60.4, When);
            Assert.Contains("Deneb+Kochab+Schedar", s);
            Assert.Contains("\u043C\u0438\u043D 60\u00B0", s);
            Assert.Contains("21:10", s);
        }

        [Fact]
        public void TripleSegment_WorstCaseNames_FitBudget()
        {
            // Three longest navigation-star names; the column is ~70 chars.
            string s = ForecastLineText.TripleSegment("Rigil Kentaurus", "Kaus Australis",
                "Miaplacidus", 60.0, When);
            Assert.True(s.Length <= 70, "triple line is " + s.Length + " chars: " + s);
        }

        [Fact]
        public void Compose_TripleReplacesPair_AndIsOwnLine()
        {
            ForecastLine line = ForecastLineText.Compose("\u041D\u043E\u0447\u044C \u0434\u043E 18:56",
                "T", "P", "A", "B", "C");

            Assert.True(line.HasTriple);
            Assert.Contains("T", line.Text);
            Assert.DoesNotContain("P", line.Text);
            Assert.Contains("\n", line.Text);
            Assert.Equal("A", line.TripleA);
        }

        [Fact]
        public void Compose_PairSharesTheTwilightLine()
        {
            ForecastLine line = ForecastLineText.Compose("\u041D\u043E\u0447\u044C \u0434\u043E 18:56",
                null, "\u041F\u0430\u0440\u0430: A+B", null, null, null);

            Assert.False(line.HasTriple);
            Assert.Equal("\u041D\u043E\u0447\u044C \u0434\u043E 18:56 \u00B7 \u041F\u0430\u0440\u0430: A+B",
                line.Text);
        }

        [Fact]
        public void Compose_NothingFound_IsEmpty()
        {
            ForecastLine line = ForecastLineText.Compose("", null, null, null, null, null);
            Assert.False(line.HasTriple);
            Assert.Equal("", line.Text);
        }

        [Fact]
        public void TwilightSegment_FormatsOrOmits()
        {
            Assert.Equal("\u041D\u043E\u0447\u044C \u0434\u043E 21:10",
                ForecastLineText.TwilightSegment(true, true, When));
            Assert.Equal("", ForecastLineText.TwilightSegment(true, false, When));
        }
    }
}
