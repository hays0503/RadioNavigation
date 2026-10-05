using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// WarnOnce contract: first call per key returns true, repeats return
    /// false, distinct keys are independent, Reset rearms. Unique keys per
    /// test — the gate state is static and shared across tests in one run.
    /// </summary>
    public sealed class WarnOnceTests
    {
        [Fact]
        public void ShouldLog_FirstTrueThenFalse()
        {
            Assert.True(WarnOnce.ShouldLog("WarnOnceTests.First"));
            Assert.False(WarnOnce.ShouldLog("WarnOnceTests.First"));
        }

        [Fact]
        public void ShouldLog_DistinctKeys_Independent()
        {
            Assert.True(WarnOnce.ShouldLog("WarnOnceTests.Alpha"));
            Assert.True(WarnOnce.ShouldLog("WarnOnceTests.Beta"));
            Assert.False(WarnOnce.ShouldLog("WarnOnceTests.Alpha"));
            Assert.False(WarnOnce.ShouldLog("WarnOnceTests.Beta"));
        }

        [Fact]
        public void Reset_RearmsFiredKeys()
        {
            WarnOnce.ShouldLog("WarnOnceTests.Rearm");
            Assert.False(WarnOnce.ShouldLog("WarnOnceTests.Rearm"));

            WarnOnce.Reset();

            Assert.True(WarnOnce.ShouldLog("WarnOnceTests.Rearm"));
        }
    }
}
