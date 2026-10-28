using System;
using System.Globalization;
using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class ClockSecondsTextTests
    {
        [Theory]
        [InlineData("{1:D2} {0:MMMM} {2}, {3:D2}:{4:D2}", "{1:D2} {0:MMMM} {2}, {3:D2}:{4:D2}:{5:D2}")]
        [InlineData("{3:D2}:{4:D2} {1:D2} {0:MMMM} {2}", "{3:D2}:{4:D2}:{5:D2} {1:D2} {0:MMMM} {2}")]
        [InlineData("{2}年{0:MM}月{1:D2}日 {3:D2}:{4:D2}", "{2}年{0:MM}月{1:D2}日 {3:D2}:{4:D2}:{5:D2}")]
        public void LocalizedFormatAddsSecondsBesideMinutes(string source, string expected)
        {
            Assert.True(ClockSecondsText.TryAddSecondsToDateFormat(source, out string actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(3, 4, 5, "03:04:05")]
        [InlineData(23, 59, 59, "23:59:59")]
        public void JournalClockHasPaddedHoursMinutesAndSeconds(int hour, int minute, int second, string expected)
        {
            DateTime value = new DateTime(1943, 6, 1, hour, minute, second);
            Assert.Equal(expected, ClockSecondsText.FormatClock(value));
        }

        [Fact]
        public void DateFormatWithoutUniqueMinuteTokenIsRejected()
        {
            Assert.False(ClockSecondsText.TryAddSecondsToDateFormat("{1:D2} {0:MMMM} {2}, {3:D2}:{4:D2} {4:D2}", out _));
            Assert.False(ClockSecondsText.TryAddSecondsToDateFormat("{1:D2} {0:MMMM} {2}", out _));
        }

        [Fact]
        public void LocalizedFormatUsesGameDateArgumentsAndCurrentCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                DateTime value = new DateTime(1943, 6, 1, 14, 32, 5);
                Assert.True(ClockSecondsText.TryAddSecondsToDateFormat(
                    "{1:D2} {0:MMMM} {2}, {3:D2}:{4:D2}", out string format));
                Assert.Equal("01 June 1943, 14:32:05", ClockSecondsText.FormatLocalizedDate(format, value));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void SecondKeyIncludesTheFullDateAndCanMoveBackward()
        {
            DateTime earlier = new DateTime(1943, 6, 1, 14, 32, 5);
            DateTime later = new DateTime(1943, 6, 1, 15, 32, 5);
            Assert.NotEqual(ClockSecondsText.SecondKey(earlier), ClockSecondsText.SecondKey(later));
            Assert.True(ClockSecondsText.SecondKey(earlier) > ClockSecondsText.SecondKey(earlier.AddSeconds(-1)));
        }
    }
}
