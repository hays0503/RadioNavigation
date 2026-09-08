using System;
using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation
{
    // Type doubles for the test assembly ONLY. The real MonoBehaviour classes
    // live in Source/UI + Source/Logic and are deliberately NOT linked into
    // tests (they would drag the whole Unity-bound graph). Do NOT link the
    // real files while these doubles exist — duplicate definitions will fail
    // the build. Type identity vs the real classes is covered by the game
    // compile + SourceCheck; here we pin names, counts, order and guard logic.
    public sealed class StarsMesh { }
    public sealed class ConstellationMesh { }
    public sealed class PlanetRenderer { }
    public sealed class SunDiscRenderer { }
    public sealed class StationMapOverlay { }
    public sealed class LighthouseMapMarker { }
    public sealed class StationMapMarker { }
    public sealed class FixMapMarker { }
    public sealed class SextantController { }
    public sealed class LighthousePlacer { public static void EnsureRunning() { } }
    public sealed class StarSkyPhase0Probe { public static void EnsureRunning() { } }
}

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SceneContentBuilderTests
    {
        private sealed class FakeHost : ISceneContentHost
        {
            public readonly HashSet<string> Existing = new HashSet<string>();
            public readonly List<string> Created = new List<string>();

            public bool Exists(string rootName) { return Existing.Contains(rootName); }

            public void CreateRoot(string rootName, Type componentType)
            {
                Existing.Add(rootName);
                Created.Add(rootName + ":" + componentType.Name);
            }
        }

        [Fact]
        public void SkyRoots_HasFourExpectedNamesInOrder()
        {
            var names = new List<string>();
            foreach (var root in SceneContentBuilder.SkyRoots)
                names.Add(root.Name);

            Assert.Equal(
                new[] { "StarSkyRoot", "ConstellationRoot", "PlanetRoot", "SunDiscRoot" },
                names);
        }

        [Fact]
        public void SkyRoots_ComponentTypesMatchRoots()
        {
            var pairs = new List<string>();
            foreach (var root in SceneContentBuilder.SkyRoots)
                pairs.Add(root.Name + ":" + root.ComponentType.Name);

            Assert.Equal(
                new[]
                {
                    "StarSkyRoot:StarsMesh",
                    "ConstellationRoot:ConstellationMesh",
                    "PlanetRoot:PlanetRenderer",
                    "SunDiscRoot:SunDiscRenderer"
                },
                pairs);
        }

        [Fact]
        public void MapComponents_HasFiveExpectedTypesInOrder()
        {
            Assert.Equal(
                new[]
                {
                    typeof(StationMapOverlay),
                    typeof(LighthouseMapMarker),
                    typeof(StationMapMarker),
                    typeof(FixMapMarker),
                    typeof(SextantController)
                },
                SceneContentBuilder.MapComponents);
        }

        [Fact]
        public void EnsureSkyRoots_AllExist_CreatesNothing()
        {
            var host = new FakeHost();
            foreach (var root in SceneContentBuilder.SkyRoots)
                host.Existing.Add(root.Name);

            SceneContentBuilder.EnsureSkyRoots(host);

            Assert.Empty(host.Created);
        }

        [Fact]
        public void EnsureSkyRoots_Missing_CreatesOnceEach()
        {
            var host = new FakeHost();

            SceneContentBuilder.EnsureSkyRoots(host);
            SceneContentBuilder.EnsureSkyRoots(host);

            Assert.Equal(4, host.Created.Count);
            Assert.Contains("StarSkyRoot:StarsMesh", host.Created);
            Assert.Contains("SunDiscRoot:SunDiscRenderer", host.Created);
        }

        [Fact]
        public void EnsureMapComponents_Partial_OnlyMissingAdded()
        {
            var present = new HashSet<Type> { typeof(StationMapOverlay), typeof(FixMapMarker) };
            var added = new List<Type>();

            SceneContentBuilder.EnsureMapComponents(present.Contains, added.Add);

            Assert.Equal(3, added.Count);
            Assert.DoesNotContain(typeof(StationMapOverlay), added);
            Assert.Contains(typeof(SextantController), added);
        }

        [Fact]
        public void EnsureRunners_InvokesBothOnce()
        {
            int lighthouse = 0;
            int probe = 0;

            SceneContentBuilder.EnsureRunners(() => lighthouse++, () => probe++);

            Assert.Equal(1, lighthouse);
            Assert.Equal(1, probe);
        }
    }
}
