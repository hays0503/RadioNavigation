using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantLopPlotTests
    {
        private static readonly DateTime Time = new DateTime(1941, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        private static SextantObservation Sight(DateTime utc, double azimuth, double intercept)
        {
            return new SextantObservation
            {
                Utc = utc, AzDeg = azimuth, InterceptNm = intercept,
                HoDeg = 30 + intercept / 60, HcDeg = 30, DrLatDeg = 48, DrLonDeg = -15
            };
        }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(90, 2)]
        [InlineData(242.493, -0.056)]
        [InlineData(150.041, -0.097)]
        public void LopPlot_LinePassesThroughBookPointAndIsPerpendicular(double azimuth, double intercept)
        {
            var calc = new RunningFixCalculator();
            var obs = Sight(Time, azimuth, intercept);
            calc.Record(obs);
            SextantLopSegment line = Assert.Single(SextantLopPlot.Build(calc, 0, 0));
            LineOfPosition expected = RunningFixCalculator.BuildLop(obs);
            Assert.Equal(expected.TangentLatDeg, (line.StartLat + line.EndLat) / 2, 10);
            Assert.Equal(expected.TangentLonDeg, (line.StartLon + line.EndLon) / 2, 10);
            double north = (line.EndLat - line.StartLat) * 60;
            double east = (line.EndLon - line.StartLon) * 60 * Math.Cos(expected.TangentLatDeg * Math.PI / 180);
            double az = azimuth * Math.PI / 180;
            Assert.Equal(0, north * Math.Cos(az) + east * Math.Sin(az), 9);
            Assert.Equal(40, Math.Sqrt(north * north + east * east), 9);
            Assert.True(line.Coincident);
        }

        [Fact]
        public void LopPlot_OriginalAndTransferredUseTheSameRunAsTheBook()
        {
            var calc = new RunningFixCalculator();
            calc.Record(Sight(Time, 150, -0.1));
            calc.Record(Sight(Time.AddHours(1), 205, -1.7));
            SextantLopSegment[] lines = SextantLopPlot.Build(calc, 90, 8);
            Assert.Equal(3, lines.Length); // O1, T1, O2=T2
            var first = lines.Single(l => l.Slot == 1 && !l.Transferred);
            var moved = lines.Single(l => l.Slot == 1 && l.Transferred);
            Assert.Equal(Time, first.Utc);
            Assert.Equal(Time.AddHours(1), moved.Utc);
            var expected = RunningFixCalculator.TranslateLop(RunningFixCalculator.BuildLop(calc.Observation1), 90, 8);
            Assert.Equal(expected.TangentLatDeg, (moved.StartLat + moved.EndLat) / 2, 10);
            Assert.Equal(expected.TangentLonDeg, (moved.StartLon + moved.EndLon) / 2, 10);
            Assert.Equal(first.StartLat, moved.StartLat, 10);
            Assert.True(moved.StartLon > first.StartLon);
        }

        [Fact]
        public void LopPlot_ThreeMovingSightsGiveFiveLinesAndThirdSlotReplacementIsBounded()
        {
            var calc = new RunningFixCalculator();
            calc.Record(Sight(Time, 150, 0));
            calc.Record(Sight(Time.AddHours(1), 205, 0));
            calc.Record(Sight(Time.AddHours(2), 242, 0));
            var first = SextantLopPlot.Build(calc, 45, 7);
            Assert.Equal(5, first.Length);
            Assert.Equal(2, first.Count(l => l.Transferred));
            calc.Record(Sight(Time.AddHours(3), 250, 0));
            var updated = SextantLopPlot.Build(calc, 45, 7);
            Assert.Equal(5, updated.Length);
            Assert.Equal(Time.AddHours(3), updated.Single(l => l.Slot == 3).Utc);
            Assert.All(updated.Where(l => l.Transferred), l => Assert.Equal(Time.AddHours(3), l.Utc));
        }

        [Fact]
        public void LopPlot_StoppedOrSimultaneousSightsNeverCreateDuplicateSets()
        {
            var calc = new RunningFixCalculator();
            calc.Record(Sight(Time, 120, 0));
            calc.Record(Sight(Time, 210, 0));
            Assert.All(SextantLopPlot.Build(calc, 90, 10), l => Assert.True(l.Coincident));
            calc.Record(Sight(Time.AddHours(2), 240, 0));
            var stopped = SextantLopPlot.Build(calc, 90, 0);
            Assert.Equal(3, stopped.Length);
            Assert.All(stopped, l => Assert.False(l.Transferred));
        }

        [Fact]
        public void LopPlot_DoesNotClampPolarOrNonFiniteGeometryIntoFakeLines()
        {
            var calc = new RunningFixCalculator();
            var obs = Sight(Time, 0, 0);
            obs.DrLatDeg = 90;
            calc.Record(obs);
            Assert.Empty(SextantLopPlot.Build(calc, 0, 0));
            obs.DrLatDeg = 48;
            obs.HoDeg = double.NaN;
            Assert.Empty(SextantLopPlot.Build(calc, 0, 0));
        }

        [Theory]
        [InlineData("RadioNav FIX")]
        [InlineData("RadioNav LOP A+B")]
        [InlineData("RadioNav LH DE-042")]
        [InlineData("RadioNav SIGHTLINE O4 A")]
        [InlineData("RadioNav SIGHTLINE O1 A custom")]
        [InlineData("LOP anchor")]
        [InlineData("")]
        public void LopPlot_OwnershipNeverIncludesOtherMarkersOrUserShapes(string name)
        {
            Assert.False(SextantLopPlot.OwnsEndpoint(name));
            Assert.False(SextantLopPlot.OwnsLine(name, SextantLopPlot.EndpointKey(1, false, true)));
        }

        [Fact]
        public void LopPlot_TwelveReservedEndpointsAreUniqueAndOnlyTheirExactPairsAreOwned()
        {
            var keys = new HashSet<string>();
            for (int slot = 1; slot <= 3; slot++)
            {
                foreach (bool moved in new[] { false, true })
                {
                    string a = SextantLopPlot.EndpointKey(slot, moved, false);
                    string b = SextantLopPlot.EndpointKey(slot, moved, true);
                    Assert.True(keys.Add(a)); Assert.True(keys.Add(b));
                    Assert.True(SextantLopPlot.OwnsEndpoint(a));
                    Assert.True(SextantLopPlot.OwnsLine(a, b));
                    Assert.True(SextantLopPlot.OwnsLine(b, a));
                    Assert.False(SextantLopPlot.OwnsLine(a, a));
                }
            }
            Assert.Equal(12, keys.Count);
            Assert.False(SextantLopPlot.OwnsLine(SextantLopPlot.EndpointKey(1, false, false), SextantLopPlot.EndpointKey(2, false, true)));
            Assert.False(SextantLopPlot.OwnsLine(SextantLopPlot.EndpointKey(1, false, false), SextantLopPlot.EndpointKey(1, true, true)));
        }

        private sealed class Chart : ISextantLopChart
        {
            internal bool Ready;
            internal int Replaces, Adopts;
            internal SextantLopSegment[] Lines;
            public bool TryReplace(SextantLopSegment[] lines)
            {
                if (!Ready) return false;
                Replaces++; Lines = lines; return true;
            }
            public bool TryAdopt()
            {
                if (!Ready) return false;
                Adopts++; return true;
            }
        }

        [Fact]
        public void LopPlot_SessionRetriesOnlyPendingWorkAndNeverResurrectsAnErasedLine()
        {
            var session = new SextantLopMapSession();
            var chart = new Chart();
            var lines = new[] { new SextantLopSegment { Slot = 1 } };
            session.Publish(lines);
            session.Flush(chart, false);
            Assert.Equal(0, chart.Replaces);
            chart.Ready = true;
            session.Flush(chart, true);
            Assert.Equal(0, chart.Replaces);
            session.Flush(chart, false);
            Assert.Same(lines, chart.Lines);
            chart.Lines = new SextantLopSegment[0]; // user erase, not a new publication
            session.Flush(chart, false);
            Assert.Equal(1, chart.Replaces);
            Assert.Empty(chart.Lines);
        }

        [Fact]
        public void LopPlot_LoadCancelsPreviousPendingPublicationAndOnlyAdoptsSavedNativeGeometry()
        {
            var session = new SextantLopMapSession();
            var chart = new Chart { Ready = true };
            session.Publish(new[] { new SextantLopSegment { Slot = 3 } });
            session.Restore();
            session.Flush(chart, false);
            Assert.Equal(0, chart.Replaces);
            Assert.Equal(1, chart.Adopts);
            session.Flush(chart, false);
            Assert.Equal(1, chart.Adopts);
        }

        [Fact]
        public void LopPlot_NewIOverridesAResetPendingWhileMapWasClosed()
        {
            var session = new SextantLopMapSession();
            var chart = new Chart { Ready = true };
            session.Publish(new SextantLopSegment[0]);
            var fresh = new[] { new SextantLopSegment { Slot = 1 } };
            session.Publish(fresh);
            session.Flush(chart, false);
            Assert.Same(fresh, chart.Lines);
            Assert.Equal(1, chart.Replaces);
        }

        private sealed class Position : IPlayerPositionProvider
        {
            public bool TryGetPosition(out float lat, out float lon) { lat = 55; lon = 6; return true; }
        }

        [Fact]
        public void LopPlot_ActualWorksheetPublishesOnlyAfterCommitResetsAndRestoresWithoutRebuilding()
        {
            var worksheet = new SextantWorksheet(new Position());
            var outputs = new List<SextantLopSegment[]>();
            int restores = 0;
            worksheet.LopPlotChanged = outputs.Add;
            worksheet.LopPlotRestored = () => restores++;
            Assert.False(worksheet.CaptureObservation(CelestialBody.None(), 30));
            Assert.Empty(outputs);
            Assert.True(worksheet.CaptureObservation(CelestialBody.Star(11767, "Polaris"), 55));
            Assert.Single(outputs);
            Assert.Single(outputs[0]);
            worksheet.ReportBlockedSight(SightBlock.Low);
            worksheet.UpdatePanel(10);
            Assert.Single(outputs);
            var saved = worksheet.CaptureState();
            worksheet.RestoreState(saved);
            Assert.Equal(1, restores);
            Assert.Single(outputs); // no current-speed recomputation on load
            worksheet.ResetSeries();
            Assert.Equal(2, outputs.Count);
            Assert.Empty(outputs[1]);
        }
    }
}
