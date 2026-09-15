using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// PlayerPrefs-gated diagnostics switch (test-first for the log-gating
    /// refactor). Each test cleans up its key — the PlayerPrefs stub is
    /// static and survives across tests in one run.
    /// </summary>
    public sealed class RadioNavDiagnosticsTests
    {
        private const string Key = "RadioNav.TestDiag";

        [Fact]
        public void MissingKey_ReturnsDefaultTrue()
        {
            PlayerPrefs.DeleteKey(Key);
            Assert.True(RadioNavDiagnostics.IsEnabled(Key, true));
            PlayerPrefs.DeleteKey(Key);
        }

        [Fact]
        public void MissingKey_ReturnsDefaultFalse()
        {
            PlayerPrefs.DeleteKey(Key);
            Assert.False(RadioNavDiagnostics.IsEnabled(Key, false));
            PlayerPrefs.DeleteKey(Key);
        }

        [Fact]
        public void SetZero_Disabled()
        {
            PlayerPrefs.SetInt(Key, 0);
            Assert.False(RadioNavDiagnostics.IsEnabled(Key, true));
            PlayerPrefs.DeleteKey(Key);
        }

        [Fact]
        public void SetOne_Enabled()
        {
            PlayerPrefs.SetInt(Key, 1);
            Assert.True(RadioNavDiagnostics.IsEnabled(Key, false));
            PlayerPrefs.DeleteKey(Key);
        }

        [Fact]
        public void DeleteKey_BackToDefault()
        {
            PlayerPrefs.SetInt(Key, 0);
            PlayerPrefs.DeleteKey(Key);
            Assert.True(RadioNavDiagnostics.IsEnabled(Key, true));
        }

        [Fact]
        public void Cached_UnknownKey_FallsBackLive()
        {
            PlayerPrefs.DeleteKey(Key);
            Assert.True(RadioNavDiagnostics.IsEnabledCached(Key, true));
            Assert.False(RadioNavDiagnostics.IsEnabledCached(Key, false));
            PlayerPrefs.DeleteKey(Key);
        }

        [Fact]
        public void Cached_KnownKey_CachesWithinWindow()
        {
            const string glowKey = RadioNavDiagnostics.LighthouseGlowVerboseKey;
            const string markerKey = RadioNavDiagnostics.LighthouseMapMarkerPositionKey;
            PlayerPrefs.DeleteKey(glowKey);
            PlayerPrefs.DeleteKey(markerKey);

            Assert.True(RadioNavDiagnostics.IsEnabledCached(glowKey, true));

            PlayerPrefs.SetInt(glowKey, 0);
            Assert.True(RadioNavDiagnostics.IsEnabledCached(glowKey, true));

            PlayerPrefs.DeleteKey(glowKey);
            PlayerPrefs.DeleteKey(markerKey);
        }

        [Fact]
        public void Cached_KnownKey_RefreshesAfterWindow()
        {
            const string glowKey = RadioNavDiagnostics.LighthouseGlowVerboseKey;
            const string markerKey = RadioNavDiagnostics.LighthouseMapMarkerPositionKey;
            PlayerPrefs.DeleteKey(glowKey);
            PlayerPrefs.DeleteKey(markerKey);

            Assert.True(RadioNavDiagnostics.IsEnabledCached(glowKey, true));

            PlayerPrefs.SetInt(glowKey, 0);
            System.Threading.Thread.Sleep(300);
            Assert.False(RadioNavDiagnostics.IsEnabledCached(glowKey, true));

            PlayerPrefs.DeleteKey(glowKey);
            PlayerPrefs.DeleteKey(markerKey);
        }

        [Fact]
        public void VerboseLog_DefaultOff_AndToggleOn()
        {
            // F221: the shared informational-log switch is off by default.
            PlayerPrefs.DeleteKey(RadioNavDiagnostics.VerboseLogKey);
            System.Threading.Thread.Sleep(300);
            Assert.False(RadioNavDiagnostics.IsVerboseLogEnabled());

            PlayerPrefs.SetInt(RadioNavDiagnostics.VerboseLogKey, 1);
            System.Threading.Thread.Sleep(300);
            Assert.True(RadioNavDiagnostics.IsVerboseLogEnabled());

            PlayerPrefs.DeleteKey(RadioNavDiagnostics.VerboseLogKey);
            System.Threading.Thread.Sleep(300);
            Assert.False(RadioNavDiagnostics.IsVerboseLogEnabled());
        }
    }
}
