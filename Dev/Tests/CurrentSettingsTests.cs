using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CurrentSettingsTests
    {
        private static void WithCleanState(Action body)
        {
            string[] vars =
            {
                CurrentSettings.MasterVariable,
                CurrentSettings.AtlasVariable,
                CurrentSettings.ProfileVariable,
                CurrentSettings.FieldVariable,
                CurrentSettings.WindVariable,
                CurrentSettings.NearbyVariable,
                CurrentSettings.RudderVariable,
                CurrentSettings.FarVariable,
                CurrentSettings.RouteVariable,
                CurrentSettings.BallisticWindVariable,
                CurrentSettings.TidalVariable,
                CurrentSettings.VectorsVariable
            };
            var saved = new string[vars.Length];
            for (int i = 0; i < vars.Length; i++)
            {
                saved[i] = Environment.GetEnvironmentVariable(vars[i]);
                Environment.SetEnvironmentVariable(vars[i], null);
            }
            PlayerPrefs.DeleteKey(CurrentSettings.MasterPrefsKey);
            PlayerPrefs.DeleteKey(CurrentSettings.VectorsPrefsKey);
            PlayerPrefs.DeleteKey(CurrentSettings.VectorDepthPrefsKey);
            try
            {
                body();
            }
            finally
            {
                PlayerPrefs.DeleteKey(CurrentSettings.MasterPrefsKey);
                PlayerPrefs.DeleteKey(CurrentSettings.VectorsPrefsKey);
                PlayerPrefs.DeleteKey(CurrentSettings.VectorDepthPrefsKey);
                for (int i = 0; i < vars.Length; i++)
                    Environment.SetEnvironmentVariable(vars[i], saved[i]);
            }
        }

        [Fact]
        public void DefaultsEnableBundledAtlasAndAllComponents()
        {
            WithCleanState(() =>
            {
                Assert.True(CurrentSettings.IsMasterEnabled());
                Assert.Equal(CurrentSettings.BundledAtlasValue, CurrentSettings.ResolveRequestedAtlas());
                Assert.True(CurrentSettings.DirectWindEnabled());
                Assert.True(CurrentSettings.NearbyDriftEnabled());
                Assert.True(CurrentSettings.RelativeRudderEnabled(true));
                Assert.False(CurrentSettings.RelativeRudderEnabled(false));
                Assert.True(CurrentSettings.FarDriftEnabled());
                Assert.True(CurrentSettings.RouteCurrentEnabled());
                Assert.True(CurrentSettings.BallisticWindEnabled());
                Assert.True(CurrentSettings.TidalCurrentsEnabled());
                Assert.True(CurrentSettings.MapVectorsEnabled());
                Assert.Equal(-1f, CurrentSettings.GetVectorSampleDepthMeters());
                CurrentSettings.SaveMapVectorsPreference(false);
                Assert.False(CurrentSettings.MapVectorsEnabled());
                CurrentSettings.SaveVectorSampleDepthMeters(50f);
                Assert.Equal(50f, CurrentSettings.GetVectorSampleDepthMeters());
            });
        }

        [Fact]
        public void MasterEnvZeroDisablesEverythingEvenIfPrefsOn()
        {
            WithCleanState(() =>
            {
                CurrentSettings.SaveMasterPreference(true);
                Environment.SetEnvironmentVariable(CurrentSettings.MasterVariable, "0");
                Assert.False(CurrentSettings.IsMasterEnabled());
                Assert.Equal("", CurrentSettings.ResolveRequestedAtlas());
                Assert.False(CurrentSettings.DirectWindEnabled());
                Assert.False(CurrentSettings.NearbyDriftEnabled());
                Assert.False(CurrentSettings.RelativeRudderEnabled(true));
                Assert.False(CurrentSettings.FarDriftEnabled());
                Assert.False(CurrentSettings.RouteCurrentEnabled());
                Assert.False(CurrentSettings.BallisticWindEnabled());
                Assert.False(CurrentSettings.TidalCurrentsEnabled());
            });
        }

        [Fact]
        public void PlayerPrefsOptOutDisablesEverythingUntilClearedOrOverridden()
        {
            WithCleanState(() =>
            {
                CurrentSettings.SaveMasterPreference(false);
                Assert.False(CurrentSettings.IsMasterEnabled());
                Assert.Equal("", CurrentSettings.ResolveRequestedAtlas());
                Environment.SetEnvironmentVariable(CurrentSettings.MasterVariable, "1");
                Assert.True(CurrentSettings.IsMasterEnabled());
                Assert.Equal(CurrentSettings.BundledAtlasValue, CurrentSettings.ResolveRequestedAtlas());
            });
        }

        [Fact]
        public void ExplicitFieldOrProfilePreventsImplicitBundledAtlas()
        {
            Assert.Equal("", CurrentSettings.ResolveRequestedAtlas(null, null, "0,0"));
            Assert.Equal("", CurrentSettings.ResolveRequestedAtlas(null, "0:0,0;5:0,0", null));
            Assert.Equal("", CurrentSettings.ResolveRequestedAtlas("0", null, null));
            Assert.Equal("", CurrentSettings.ResolveRequestedAtlas("off", null, null));
            Assert.Equal("custom/dir", CurrentSettings.ResolveRequestedAtlas("custom/dir", null, "1,0"));
            Assert.Equal(CurrentSettings.BundledAtlasValue, CurrentSettings.ResolveRequestedAtlas(null, null, null));
        }

        [Fact]
        public void IndividualZeroFlagsDisableOnlyTargetedComponents()
        {
            WithCleanState(() =>
            {
                Environment.SetEnvironmentVariable(CurrentSettings.WindVariable, "0");
                Environment.SetEnvironmentVariable(CurrentSettings.RouteVariable, "off");
                Environment.SetEnvironmentVariable(CurrentSettings.FarVariable, "0");
                Environment.SetEnvironmentVariable(CurrentSettings.BallisticWindVariable, "off");
                Environment.SetEnvironmentVariable(CurrentSettings.TidalVariable, "0");
                Assert.False(CurrentSettings.DirectWindEnabled());
                Assert.False(CurrentSettings.RouteCurrentEnabled());
                Assert.False(CurrentSettings.FarDriftEnabled());
                Assert.False(CurrentSettings.BallisticWindEnabled());
                Assert.False(CurrentSettings.TidalCurrentsEnabled());
                Assert.True(CurrentSettings.NearbyDriftEnabled());
                Assert.True(CurrentSettings.RelativeRudderEnabled(true));
            });
        }
    }
}
