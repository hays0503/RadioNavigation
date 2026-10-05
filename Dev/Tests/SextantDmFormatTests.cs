using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantDmFormatTests
    {
        [Fact]
        public void FormatDm_WholeDegrees()
        {
            Assert.Equal("45\u00B000.0\u2032", SextantDmFormat.FormatDm(45.0));
        }

        [Fact]
        public void FormatDm_MinutesOneDecimal()
        {
            // 23.4' = 0.39 deg.
            Assert.Equal("45\u00B023.4\u2032", SextantDmFormat.FormatDm(45.39));
        }

        [Fact]
        public void FormatDm_RoundingWrapsToNextDegree()
        {
            // 59.96' would format as 60.0' — must wrap instead.
            Assert.Equal("46\u00B000.0\u2032", SextantDmFormat.FormatDm(45.9994));
        }

        [Fact]
        public void FormatDm_JustBelowWrap_Stays()
        {
            Assert.Equal("45\u00B059.9\u2032", SextantDmFormat.FormatDm(45.9983));
        }

        [Fact]
        public void FormatSignedDm_NegativeUsesMinusSign()
        {
            Assert.Equal("\u221245\u00B023.4\u2032", SextantDmFormat.FormatSignedDm(-45.39));
        }

        [Fact]
        public void FormatSignedDm_PositiveUsesPlus()
        {
            Assert.Equal("+45\u00B023.4\u2032", SextantDmFormat.FormatSignedDm(45.39));
        }

        [Fact]
        public void FormatLatLon_Hemispheres()
        {
            Assert.Equal("45\u00B023.4\u2032N", SextantDmFormat.FormatLatLon(45.39, true));
            Assert.Equal("45\u00B023.4\u2032S", SextantDmFormat.FormatLatLon(-45.39, true));
            Assert.Equal("45\u00B023.4\u2032E", SextantDmFormat.FormatLatLon(45.39, false));
            Assert.Equal("45\u00B023.4\u2032W", SextantDmFormat.FormatLatLon(-45.39, false));
        }

        [Fact]
        public void FormatLatLonAscii_UsesApostrophe()
        {
            string text = SextantDmFormat.FormatLatLonAscii(45.39, true);

            Assert.Equal("45\u00B023.4'N", text);
        }
    }
}
