using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P5 readout row formatting: magnitude, percent, steering arrows.</summary>
    public sealed class BodyRowFormatterTests
    {
        [Fact]
        public void Magnitude_IsOneDecimal()
        {
            Assert.Equal("0.0m", BodyRowFormatter.FormatMagnitude(0.03));
            Assert.Equal("2.4m", BodyRowFormatter.FormatMagnitude(2.42));
        }

        [Fact]
        public void Percent_RoundsAndClamps()
        {
            Assert.Equal("78%", BodyRowFormatter.FormatPercent(0.78));
            Assert.Equal("0%", BodyRowFormatter.FormatPercent(-0.1));
            Assert.Equal("100%", BodyRowFormatter.FormatPercent(1.4));
        }

        [Fact]
        public void Arrows_PointRightAndUp()
        {
            Assert.Equal("\u219212\u00B0 \u21918\u00B0", BodyRowFormatter.FormatArrows(12.4, 8.2));
        }

        [Fact]
        public void Arrows_PointLeftAndDown()
        {
            Assert.Equal("\u21905\u00B0 \u21933\u00B0", BodyRowFormatter.FormatArrows(-5.0, -3.0));
        }

        [Fact]
        public void Arrows_DeadZone_IsABullet()
        {
            Assert.Equal("\u2022", BodyRowFormatter.FormatArrows(0.5, -1.9));
            Assert.Equal("\u21922\u00B0", BodyRowFormatter.FormatArrows(2.0, 0.0));
        }

        [Fact]
        public void Wrap180_HandlesTheAntimeridian()
        {
            Assert.Equal(-1.0, BodyRowFormatter.Wrap180(359.0));
            Assert.Equal(1.0, BodyRowFormatter.Wrap180(-359.0));
            Assert.Equal(-179.0, BodyRowFormatter.Wrap180(181.0));
            Assert.Equal(180.0, BodyRowFormatter.Wrap180(180.0));
        }

        [Fact]
        public void Row_ContainsNameMagnitudeAltitudeAzimuthAndArrows()
        {
            string row = BodyRowFormatter.FormatRow("Vega", "0.0m", 42.1, 281.4, 12.0, 8.0);
            Assert.Contains("Vega", row);
            Assert.Contains("0.0m", row);
            Assert.Contains("42.1\u00B0", row);
            Assert.Contains("Az 281\u00B0", row);
            Assert.Contains("\u219212\u00B0", row);
        }

        [Fact]
        public void Row_WithoutArrows_HasNoGlyphs()
        {
            string row = BodyRowFormatter.FormatRow("Vega", "0.0m", 42.1, 281.4);
            Assert.Contains("Vega 0.0m 42.1\u00B0 Az 281\u00B0", row);
            Assert.DoesNotContain("\u2192", row);
            Assert.DoesNotContain("\u2022", row);
        }

        [Fact]
        public void Row_EmptyTag_SkipsItCleanly()
        {
            // The Sun carries no magnitude/percentage tag (2026-11-04): the
            // row must not show a double space where the tag would be.
            string row = BodyRowFormatter.FormatRow("\u0421\u043E\u043B\u043D\u0446\u0435",
                "", 33.6, 109.4);
            Assert.Equal("\u0421\u043E\u043B\u043D\u0446\u0435 33.6\u00B0 Az 109\u00B0", row);
            Assert.DoesNotContain("  ", row);
        }

        [Fact]
        public void Row_EmptyTag_KeepsTheArrows()
        {
            string row = BodyRowFormatter.FormatRow("\u0421\u043E\u043B\u043D\u0446\u0435",
                "", 33.6, 109.4, 12.0, 8.0);
            Assert.Contains("\u0421\u043E\u043B\u043D\u0446\u0435 33.6\u00B0 Az 109\u00B0 \u219212\u00B0 \u21918\u00B0", row);
        }

        [Fact]
        public void AngularSeparation_MatchesSphericalLaw()
        {
            Assert.Equal(0.0, BodyRowFormatter.AngularSeparationDeg(45.0, 100.0, 45.0, 100.0), 9);
            Assert.InRange(BodyRowFormatter.AngularSeparationDeg(45.0, 0.0, 45.0, 180.0),
                89.9, 90.1);
            Assert.InRange(BodyRowFormatter.AngularSeparationDeg(0.0, 0.0, 0.0, 10.0),
                9.9, 10.1);
            Assert.InRange(BodyRowFormatter.AngularSeparationDeg(40.0, 200.0, 41.0, 202.0),
                0.0, 3.0);
        }
    }
}
