using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CelestialSkyStage1Tests
    {
        private sealed class Source : ICelestialSkyInputSource
        {
            internal CelestialSkyInput Value;
            internal bool Available = true;
            internal bool Throw;
            internal int Reads;
            public bool TryRead(long revision, out CelestialSkyInput input, out CelestialSkyNotReady reason)
            {
                Reads++;
                if (Throw) throw new InvalidOperationException("source failed");
                input = Value; reason = CelestialSkyNotReady.SourceUnavailable;
                return Available;
            }
        }

        private static readonly DateTime Gmt = new DateTime(1942, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        private static CelestialSkyInput Input(long revision = 1, DateTime? time = null,
            double lat = 55, double lon = 4, double ellipsoid = 5, double eye = 5,
            bool trustedTime = true, bool trustedHeight = true, bool atmosphere = true, double utc = 0)
        {
            return new CelestialSkyInput(revision, time ?? Gmt, lat, lon, ellipsoid, eye,
                1010, 10, utc, trustedTime, true, trustedHeight, atmosphere, true);
        }

        private static CelestialWorldOwner ReadyOwner(out Source source)
        {
            var owner = new CelestialWorldOwner();
            long revision = owner.BeginLoad();
            Assert.True(owner.CompleteLoad(revision));
            source = new Source { Value = Input(revision) };
            Assert.True(owner.Tick(source));
            return owner;
        }

        [Fact] public void UT1_TT_GMST_AreDistinct()
        {
            var s = CelestialSkyMath.Compute(Input(), 1);
            Assert.InRange(Math.Abs((s.JdTt - s.JdUt1) * 86400 - s.DeltaTSeconds), 0, 0.00005);
            Assert.Equal(AstroMath.GmstDeg(s.JdUt1), s.GmstDeg);
            Assert.InRange(Math.Abs(s.GastDeg - s.GmstDeg), 0.00001, 0.01);
            Assert.InRange(s.DeltaTSeconds, 24.7, 26);
        }

        [Fact] public void InvalidInputs_AreNotReady()
        {
            Assert.Equal(CelestialSkyNotReady.MissingInput, CelestialSkyMath.Validate(null));
            Assert.Equal(CelestialSkyNotReady.WrongWorld, CelestialSkyMath.Validate(Input(0)));
            Assert.Equal(CelestialSkyNotReady.GameTime, CelestialSkyMath.Validate(Input(trustedTime: false)));
            Assert.Equal(CelestialSkyNotReady.GameTime, CelestialSkyMath.Validate(Input(time: DateTime.SpecifyKind(Gmt, DateTimeKind.Unspecified))));
            Assert.Equal(CelestialSkyNotReady.GameTime, CelestialSkyMath.Validate(Input(time: Gmt.AddYears(10))));
            Assert.Equal(CelestialSkyNotReady.WorldUtc, CelestialSkyMath.Validate(Input(utc: 1)));
            Assert.Equal(CelestialSkyNotReady.Observer, CelestialSkyMath.Validate(Input(lat: double.NaN)));
            Assert.Equal(CelestialSkyNotReady.Observer, CelestialSkyMath.Validate(Input(lon: 180.1)));
            Assert.Equal(CelestialSkyNotReady.HeightDatum, CelestialSkyMath.Validate(Input(trustedHeight: false)));
            Assert.Equal(CelestialSkyNotReady.HeightDatum, CelestialSkyMath.Validate(Input(eye: -1)));
            Assert.Equal(CelestialSkyNotReady.Atmosphere, CelestialSkyMath.Validate(Input(atmosphere: false)));
        }

        [Fact] public void NoWorld_AndLoading_NeverReadSource()
        {
            var owner = new CelestialWorldOwner(); var source = new Source { Value = Input() };
            Assert.False(owner.Tick(source));
            owner.BeginLoad(); Assert.False(owner.Tick(source));
            Assert.Equal(0, source.Reads); Assert.Null(owner.Current);
        }

        [Fact] public void SameInputs_ReuseImmutableSnapshot()
        {
            Source source; var owner = ReadyOwner(out source); var state = owner.Current;
            Assert.True(owner.Tick(source)); Assert.Same(state, owner.Current);
            Assert.Equal(1L, state.SnapshotVersion);
            Assert.True(state.NavigationStars is System.Collections.ObjectModel.ReadOnlyCollection<CelestialSkyBody>);
            Assert.Equal(57, state.NavigationStars.Count);
            Assert.Equal(57, state.NavigationStars.Select(x => x.Hip).Distinct().Count());
        }

        [Fact] public void GmtChange_UpdatesWithoutInstrument()
        {
            Source source; var owner = ReadyOwner(out source); var state = owner.Current;
            source.Value = Input(owner.WorldRevision, Gmt.AddSeconds(30));
            Assert.True(owner.Tick(source)); Assert.NotSame(state, owner.Current);
            Assert.True(CelestialSkyVector.AngleArcMin(state.Sun.VacuumEnu, owner.Current.Sun.VacuumEnu) > 1);
            Assert.Equal(2L, owner.Current.SnapshotVersion);
        }

        [Fact] public void LongitudeChange_UpdatesAtFrozenGmt()
        {
            Source source; var owner = ReadyOwner(out source); var state = owner.Current;
            source.Value = Input(owner.WorldRevision, lon: 5);
            Assert.True(owner.Tick(source)); Assert.NotSame(state, owner.Current);
            Assert.True(CelestialSkyVector.AngleArcMin(state.Sun.VacuumEnu, owner.Current.Sun.VacuumEnu) > 20);
        }

        [Fact] public void Heights_AreSeparateInputs()
        {
            var a = CelestialSkyMath.Compute(Input(), 1);
            var b = CelestialSkyMath.Compute(Input(ellipsoid: 1005), 1);
            var c = CelestialSkyMath.Compute(Input(eye: 1005), 1);
            Assert.True(Math.Abs(a.Moon.DistanceKm - b.Moon.DistanceKm) > 0.01);
            Assert.Equal(a.Moon.DistanceKm, c.Moon.DistanceKm);
            Assert.False(a.Input.SameKey(c.Input));
        }

        [Fact] public void ReloadSameGmt_ChangesRevisionAndRejectsOldInput()
        {
            Source source; var owner = ReadyOwner(out source); var old = owner.Current;
            long revision = owner.BeginLoad(); Assert.Null(owner.Current);
            Assert.False(owner.CompleteLoad(revision - 1)); Assert.True(owner.CompleteLoad(revision));
            Assert.False(owner.Tick(source)); Assert.Equal(CelestialSkyNotReady.WrongWorld, owner.NotReady);
            source.Value = Input(revision); Assert.True(owner.Tick(source));
            Assert.False(owner.CanUse(old, old.Input)); Assert.Equal(2L, owner.Current.SnapshotVersion);
        }

        [Fact] public void RepeatedLoadedCallback_IsIdempotent()
        {
            Source source; var owner = ReadyOwner(out source); var state = owner.Current;
            Assert.True(owner.CompleteLoad(owner.WorldRevision)); Assert.Same(state, owner.Current);
        }

        [Fact] public void Unload_InvalidatesAndRejectsDelayedCompletion()
        {
            Source source; var owner = ReadyOwner(out source); long revision = owner.WorldRevision;
            owner.Unload(); Assert.Null(owner.Current); Assert.False(owner.CompleteLoad(revision));
            Assert.False(owner.Tick(source)); Assert.Equal(CelestialSkyNotReady.NoWorld, owner.NotReady);
        }

        [Fact] public void MissingInvalidAndThrowingSource_ClearCurrent()
        {
            Source source; var owner = ReadyOwner(out source);
            source.Available = false; Assert.False(owner.Tick(source)); Assert.Null(owner.Current);
            source.Available = true; Assert.True(owner.Tick(source));
            source.Throw = true; Assert.False(owner.Tick(source)); Assert.Null(owner.Current);
            source.Throw = false; source.Value = Input(lat: double.NaN);
            Assert.False(owner.Tick(source)); Assert.Null(owner.Current);
            Assert.False(owner.Tick(null)); Assert.Null(owner.Current);
        }

        [Fact] public void DateYearAndDateline_AreNotLocalTimezoneConversions()
        {
            var before = new DateTime(1939, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            var after = before.AddSeconds(1);
            var a = CelestialSkyMath.Compute(Input(time: before, lon: -180), 1);
            var b = CelestialSkyMath.Compute(Input(time: after, lon: 180), 2);
            Assert.InRange((b.JdUt1 - a.JdUt1) * 86400, 0.9999, 1.0001);
            var same = CelestialSkyMath.Compute(Input(time: before, lon: 180), 1);
            Assert.InRange(CelestialSkyVector.AngleArcMin(a.Sun.VacuumEnu, same.Sun.VacuumEnu), 0, 0.0000001);
        }

        [Fact] public void Refraction_IsNotVacuumAltitudeOrRigidSemidiameter()
        {
            double lower = CelestialSkyMath.Refract(5 - .265, 1010, 10);
            double rigid = CelestialSkyMath.Refract(5, 1010, 10) - .265;
            Assert.InRange(Math.Abs(lower - rigid) * 60, .39, .41);
            Assert.True(double.IsNaN(CelestialSkyMath.Refract(-1.1, 1010, 10)));
            Assert.True(double.IsNaN(CelestialSkyMath.Refract(45, double.NaN, 10)));
        }

        [Fact] public void Refraction_HasPositivePhysicalSignAndPressureScaling()
        {
            double r = (CelestialSkyMath.Refract(45, 1010, 10) - 45) * 60;
            Assert.InRange(r, .99, 1.03);
            double half = (CelestialSkyMath.Refract(45, 505, 10) - 45) * 60;
            Assert.InRange(Math.Abs(half / r - .5), 0, 0.000000001);
            Assert.True(CelestialSkyMath.Refract(10, 1010, 0) > CelestialSkyMath.Refract(10, 1010, 20));
            Assert.Equal(90.0, CelestialSkyMath.Refract(90, 1010, 10));
        }

        [Fact] public void Readiness_RequiresEveryTrustFlagAndAcceptedAtmosphere()
        {
            Assert.Equal(CelestialSkyNotReady.WorldUtc, CelestialSkyMath.Validate(new CelestialSkyInput(
                1, Gmt, 55, 4, 5, 5, 1010, 10, 0, true, true, true, true, false)));
            Assert.Equal(CelestialSkyNotReady.Observer, CelestialSkyMath.Validate(new CelestialSkyInput(
                1, Gmt, 55, 4, 5, 5, 1010, 10, 0, true, false, true, true, true)));
            Assert.Equal(CelestialSkyNotReady.Atmosphere, CelestialSkyMath.Validate(new CelestialSkyInput(
                1, Gmt, 55, 4, 5, 5, double.NaN, 10, 0, true, true, true, true, true)));
        }

        [Fact] public void VersionIdentifiers_AreSeparateFromSaveCodecAndAdvanceOnReload()
        {
            Assert.Equal(1, CelestialSkyState.SchemaVersion);
            Assert.Equal("Meeus47Full-UT1TT-WGS84-ENU-v1", CelestialSkyState.ModelVersion);
            Source source; var owner = ReadyOwner(out source); long world = owner.WorldRevision;
            long version = owner.Current.SnapshotVersion;
            owner.Unload(); long next = owner.BeginLoad(); Assert.True(next > world);
            owner.CompleteLoad(next); source.Value = Input(next); Assert.True(owner.Tick(source));
            Assert.True(owner.Current.SnapshotVersion > version);
        }

        [Fact] public void IndependentDE421AndStarFixtures_PassFixedBudgets()
        {
            const string resource = "RadioNavigation.Tests.CelestialSkyDe421.tsv";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                Assert.True(stream != null, "Missing frozen independent fixture");
                using (var reader = new StreamReader(stream))
                {
                    reader.ReadLine(); string line; int rows = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] x = line.Split('\t');
                        Func<int, double> n = i => double.Parse(x[i], CultureInfo.InvariantCulture);
                        DateTime gmt = DateTime.Parse(x[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                        var state = CelestialSkyMath.Compute(Input(time: gmt, lat: n(1), lon: n(2), ellipsoid: n(3), eye: n(4)), 1);
                        CelestialSkyBody body = x[5] == "Sun" ? state.Sun : state.Moon;
                        if (x[5] == "Star") body = state.NavigationStars.Single(s => s.Hip == int.Parse(x[6], CultureInfo.InvariantCulture));
                        double error = CelestialSkyVector.AngleArcMin(body.VacuumEnu, new CelestialSkyVector(n(7), n(8), n(9)));
                        Assert.True(error <= (x[5] == "Moon" ? 2 : 1), x[0] + " " + x[5] + " centre " + error);
                        Assert.InRange(Math.Abs(body.DiameterDeg - n(10)) * 60, 0, .1);
                        rows++;
                    }
                    Assert.Equal(90, rows);
                }
            }
        }
    }
}
