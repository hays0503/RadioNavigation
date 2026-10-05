using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class StationManagerTests
    {
        private static StationManager Create()
        {
            var fleet = new StationFleet((i, e, c) => { }, i => { });
            return new StationManager(fleet);
        }

        private static float[] TwoDistinctSonneFreqs()
        {
            var freqs = new System.Collections.Generic.List<float>();
            foreach (var station in BeaconData.Stations)
            {
                if (station.Role != BeaconStationRole.DirectionalSonne
                    || station.History == null
                    || station.History.Length == 0
                    || !station.History[0].FrequencyKHz.HasValue)
                    continue;
                float f = station.History[0].FrequencyKHz.Value;
                if (!freqs.Contains(f))
                    freqs.Add(f);
                if (freqs.Count == 2)
                    break;
            }
            if (freqs.Count < 2)
                throw new InvalidOperationException("need two distinct Sonne frequencies");
            return freqs.ToArray();
        }

        [Fact]
        public void UpdateGains_OutOfZone_ZeroesGains()
        {
            StationManager manager = Create();
            float freq = TwoDistinctSonneFreqs()[0];
            manager.UpdateGains(true, freq, 20f, null);

            manager.UpdateGains(false, freq, 20f, null);

            Assert.Equal(-1, manager.DominantStationIndex);
            foreach (StationState state in manager.Stations)
                Assert.Equal(0f, state.Gain);
        }

        [Fact]
        public void UpdateGains_Tuned_DominantSelected()
        {
            StationManager manager = Create();

            manager.UpdateGains(true, TwoDistinctSonneFreqs()[0], 20f, null);

            Assert.True(manager.DominantStationIndex >= 0);
            Assert.True(manager.Stations[manager.DominantStationIndex].Gain > 0f);
        }

        [Fact]
        public void UpdateGains_DominantChange_FiresCallback()
        {
            StationManager manager = Create();
            float[] freqs = TwoDistinctSonneFreqs();
            int fires = 0;
            Action onChange = () => fires++;

            manager.UpdateGains(true, freqs[0], 20f, onChange);
            int firstDom = manager.DominantStationIndex;
            Assert.Equal(1, fires);
            manager.UpdateGains(true, freqs[0], 20f, onChange);
            Assert.Equal(1, fires);
            manager.UpdateGains(true, freqs[1], 20f, onChange);
            Assert.NotEqual(firstDom, manager.DominantStationIndex);
            Assert.Equal(2, fires);
        }

        [Fact]
        public void IsDominantAudible_Gating()
        {
            StationManager manager = Create();
            float freq = TwoDistinctSonneFreqs()[0];

            Assert.False(manager.IsDominantAudible(0, true));

            manager.UpdateGains(true, freq, 20f, null);
            int dom = manager.DominantStationIndex;

            Assert.True(manager.IsDominantAudible(dom, true));
            Assert.False(manager.IsDominantAudible(dom, false));
        }

        [Fact]
        public void GetStationDiagnostics_InvalidIndex_Default()
        {
            StationManager manager = Create();

            Assert.Equal(default(StationDiagnostics), manager.GetStationDiagnostics(-1));
            Assert.Equal(default(StationDiagnostics), manager.GetStationDiagnostics(999));
        }

        [Fact]
        public void UpdatePosition_NaN_Rejected()
        {
            StationManager manager = Create();

            manager.UpdatePosition(float.NaN, 0f);

            Assert.False(manager.HasPosition);

            manager.UpdatePosition(50f, 0f);

            Assert.True(manager.HasPosition);
            Assert.Equal(50f, manager.PlayerLatitude);
        }
    }
}
