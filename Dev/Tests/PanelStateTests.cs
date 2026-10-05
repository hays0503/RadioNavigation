using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class PanelStateTests
    {
        [Fact]
        public void Restore_AppliesLoadedValues()
        {
            PanelState.Save(10f, 20f, 3, 40f);
            float vol = -1f, fb = -1f, cursor = -1f;
            int zone = -1;

            PanelState.Restore(v => vol = v, v => fb = v, v => zone = v, v => cursor = v);

            Assert.Equal(10f, vol);
            Assert.Equal(20f, fb);
            Assert.Equal(3, zone);
            Assert.Equal(40f, cursor);
        }

        [Fact]
        public void Restore_NullSinks_Skipped()
        {
            PanelState.Save(10f, 20f, 3, 40f);

            PanelState.Restore(null, null, null, null);
        }

        [Fact]
        public void Restore_PartialSinks_OnlyPresentApplied()
        {
            PanelState.Save(10f, 20f, 3, 40f);
            int zone = -1;

            PanelState.Restore(null, null, v => zone = v, null);

            Assert.Equal(3, zone);
        }

        [Fact]
        public void Save_RoundTripsThroughStore()
        {
            PanelState.Save(11f, 22f, 2, 33f);

            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);

            Assert.Equal(11f, vol);
            Assert.Equal(22f, fb);
            Assert.Equal(2, zone);
            Assert.Equal(33f, cursor);
        }
    }
}
