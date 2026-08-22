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
    }
}
