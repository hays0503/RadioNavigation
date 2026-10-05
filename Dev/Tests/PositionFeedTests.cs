using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// PositionFeed contract: the pure position-poll decisions behind
    /// SonneController.Update (significant-movement threshold + the 10 s
    /// position log line). Extracted so the threshold and line format are
    /// pinned offline; the controller keeps the provider read and gating.
    /// </summary>
    public sealed class PositionFeedTests
    {
        [Fact]
        public void HasMoved_IdenticalPosition_False()
        {
            Assert.False(PositionFeed.HasMoved(50f, 0f, 50f, 0f));
        }

        [Fact]
        public void HasMoved_AboveThreshold_True()
        {
            Assert.True(PositionFeed.HasMoved(50.0002f, 0f, 50f, 0f));
            Assert.True(PositionFeed.HasMoved(50f, 0.0002f, 50f, 0f));
        }

        [Fact]
        public void HasMoved_BelowThreshold_False()
        {
            Assert.False(PositionFeed.HasMoved(50.00005f, 0.00005f, 50f, 0f));
        }

        [Fact]
        public void FormatLogLine_ContainsCoordinatesAndTimestamp()
        {
            var utc = new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc);

            string line = PositionFeed.FormatLogLine(50.123456f, 1.5f, utc);

            Assert.Contains("50.12346", line);
            Assert.Contains("1.50000", line);
            Assert.Contains("2026-08-30 12-34-56", line);
        }
    }
}
