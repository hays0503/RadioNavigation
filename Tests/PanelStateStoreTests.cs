using Xunit;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class PanelStateStoreTests
    {
        [Fact]
        public void SaveLoad_RoundTripsValues()
        {
            PlayerPrefs.DeleteAll();
            PanelStateStore.Save(55.5f, 33.25f, 3, -40f);

            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);
            Assert.Equal(55.5f, vol, 2);
            Assert.Equal(33.25f, fb, 2);
            Assert.Equal(3, zone);
            Assert.Equal(-40f, cursor, 2);
        }

        [Fact]
        public void Load_NoSavedState_ReturnsDefaults()
        {
            PlayerPrefs.DeleteAll();
            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);
            Assert.Equal(0f, vol, 2);
            Assert.Equal(0f, fb, 2);
            Assert.Equal(0, zone);
            Assert.Equal(0f, cursor, 2);
        }

        [Fact]
        public void Clear_RemovesSavedState()
        {
            PlayerPrefs.DeleteAll();
            PanelStateStore.Save(10f, 20f, 1, 30f);
            PanelStateStore.Clear();
            PanelStateStore.Load(out float vol, out float fb, out int zone, out float cursor);
            Assert.Equal(0f, vol, 2);
            Assert.Equal(0f, fb, 2);
            Assert.Equal(0, zone);
            Assert.Equal(0f, cursor, 2);
        }
    }
}
