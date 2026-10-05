using System.Linq;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class LighthouseProfileTests
    {
        [Fact]
        public void Profiles_CodesAreUnique()
        {
            var codes = LighthouseProfiles.All.Select(p => p.Code);
            Assert.Equal(codes.Distinct().Count(), codes.Count());
        }

        [Fact]
        public void Profiles_AllHaveNamePrefabAndValidCoordinates()
        {
            foreach (var p in LighthouseProfiles.All)
            {
                Assert.False(string.IsNullOrEmpty(p.Name));
                Assert.False(string.IsNullOrEmpty(p.PrefabPath));
                Assert.False(string.IsNullOrEmpty(p.RotatorObjectName));
                Assert.InRange(p.Latitude, -90.0, 90.0);
                Assert.InRange(p.Longitude, -180.0, 180.0);
                Assert.True(p.RotationDegreesPerSecond >= 0f);
            }
        }

        [Fact]
        public void Profiles_WangeroogeIsEnabledWith20sRotation()
        {
            var wangerooge = LighthouseProfiles.All.First(p => p.Code == "MOD-WANGEROOGE");
            Assert.True(wangerooge.Enabled);
            Assert.Equal("lighthouse", wangerooge.PrefabPath);
            Assert.Equal("LighthouseRotator", wangerooge.RotatorObjectName);
            Assert.Equal(18f, wangerooge.RotationDegreesPerSecond);
        }

        [Fact]
        public void Profiles_OnlyWangeroogeIsActive()
        {
            var active = LighthouseProfiles.Active().ToList();
            Assert.Single(active);
            Assert.Equal("Wangerooge", active[0].Name);
        }

        [Fact]
        public void Profiles_CuratedEntriesCarryRegistryData()
        {
            var norderney = LighthouseProfiles.All.First(p => p.Code == "B1054");
            Assert.Equal("Leuchtturm Norderney", norderney.Name);
            Assert.Equal(53.709217f, norderney.Latitude);
            Assert.Equal(7.229633f, norderney.Longitude);
            Assert.Equal(59f, norderney.FireHeightM);
            Assert.Equal("Fl(3)W.12s23M", norderney.Characteristic);
            Assert.False(norderney.Enabled);
        }
    }
}
