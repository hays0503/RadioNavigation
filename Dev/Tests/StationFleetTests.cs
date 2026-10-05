using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// StationFleet contract: station object construction + keyer event
    /// wiring behind SonneController.Awake/OnDestroy. Extracted so fleet
    /// build, per-station callback routing and symmetric unwire run
    /// offline without the MonoBehaviour.
    /// </summary>
    public sealed class StationFleetTests
    {
        [Fact]
        public void Build_ArraysMatchStationTable()
        {
            var fleet = new StationFleet((i, e, c) => { }, i => { });

            int n = BeaconData.Stations.Length;
            Assert.True(n > 0);
            Assert.Equal(n, fleet.States.Length);
            Assert.Equal(n, fleet.Schedulers.Length);
            Assert.Equal(n, fleet.Keyers.Length);
            Assert.Equal(n, fleet.Carriers.Length);
        }

        [Fact]
        public void Build_InitialStateIsSilent()
        {
            var fleet = new StationFleet((i, e, c) => { }, i => { });

            for (int i = 0; i < fleet.States.Length; i++)
            {
                Assert.Equal(0f, fleet.States[i].Gain);
                Assert.Equal(Constants.BfoBaseHz, fleet.States[i].BeatFrequency);
                Assert.False(fleet.States[i].InBeam);
            }
        }

        [Fact]
        public void ElementEvent_RoutedWithStationIndex()
        {
            var routed = new List<int>();
            var fleet = new StationFleet(
                (i, e, c) => routed.Add(i),
                i => { });

            fleet.Keyers[2].LoadConsol(new[] { NavigationElementType.Dot });
            for (int k = 0; k < 50; k++)
                fleet.Keyers[2].Advance();

            Assert.Contains(2, routed);
        }

        [Fact]
        public void Unwire_DetachesHandlers()
        {
            var routed = new List<int>();
            var fleet = new StationFleet(
                (i, e, c) => routed.Add(i),
                i => { });
            fleet.Keyers[1].LoadConsol(new[] { NavigationElementType.Dot });

            fleet.Unwire();
            for (int k = 0; k < 50; k++)
                fleet.Keyers[1].Advance();

            Assert.Empty(routed);
        }
    }
}
