using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>Pure helpers of the native map points: the adoption keys, the
    /// static/dynamic policy and the reconcile plan (author 2026-11-05).</summary>
    public sealed class MapMarkerPointsTests
    {
        [Fact]
        public void Keys_ComposeWithPrefix()
        {
            Assert.Equal("RadioNav FIX", MapMarkerPoints.FixKey);
            Assert.Equal("RadioNav LOP B+C", MapMarkerPoints.LopVertexKey(0));
            Assert.Equal("RadioNav LH DE-042", MapMarkerPoints.LighthouseKey("DE-042"));
            Assert.Equal("RadioNav STN DDH", MapMarkerPoints.StationKey("DDH"));
            Assert.Equal("RadioNav LH ", MapMarkerPoints.LighthouseKey(null));
        }

        [Fact]
        public void LopPairs_MatchTheCockedHatOrder()
        {
            Assert.Equal("B+C", MapMarkerPoints.LopPair(0));
            Assert.Equal("C+A", MapMarkerPoints.LopPair(1));
            Assert.Equal("A+B", MapMarkerPoints.LopPair(2));
            Assert.Equal("A+B", MapMarkerPoints.LopPair(99));
        }

        [Fact]
        public void IsOwned_MatchesOnlyTheModPrefix()
        {
            Assert.True(MapMarkerPoints.IsOwned("RadioNav FIX"));
            Assert.True(MapMarkerPoints.IsOwned("RadioNav LH 1"));
            Assert.False(MapMarkerPoints.IsOwned("Point"));
            Assert.False(MapMarkerPoints.IsOwned("RadioNav")); // the space is part of the key
            Assert.False(MapMarkerPoints.IsOwned(""));
            Assert.False(MapMarkerPoints.IsOwned(null));
        }

        [Fact]
        public void IsStatic_OnlyLighthouseAndStation()
        {
            Assert.True(MapMarkerPoints.IsStatic(MapMarkerKind.Lighthouse));
            Assert.True(MapMarkerPoints.IsStatic(MapMarkerKind.Station));
            Assert.False(MapMarkerPoints.IsStatic(MapMarkerKind.Fix));
            Assert.False(MapMarkerPoints.IsStatic(MapMarkerKind.LopVertex));
        }

        [Fact]
        public void Evaluate_DynamicMarker_ExistsUnlessSuppressed()
        {
            MapMarkerPolicy policy = MapMarkerPoints.Evaluate(
                MapMarkerKind.Fix, suppressed: false, visible: true,
                pointsToolActive: false);
            Assert.True(policy.Exists);
            Assert.True(policy.Interactive);
            Assert.False(policy.PositionLocked);

            policy = MapMarkerPoints.Evaluate(
                MapMarkerKind.LopVertex, suppressed: true, visible: true,
                pointsToolActive: false);
            Assert.False(policy.Exists);
        }

        [Fact]
        public void Evaluate_StaticMarker_AlwaysExistsAndLocksPosition()
        {
            MapMarkerPolicy policy = MapMarkerPoints.Evaluate(
                MapMarkerKind.Lighthouse, suppressed: true, visible: true,
                pointsToolActive: false);
            Assert.True(policy.Exists);
            Assert.True(policy.PositionLocked);
            Assert.True(policy.Interactive);
        }

        [Fact]
        public void Evaluate_PointsTool_DisablesInteractionForEveryone()
        {
            MapMarkerKind[] kinds =
            {
                MapMarkerKind.Fix, MapMarkerKind.LopVertex,
                MapMarkerKind.Lighthouse, MapMarkerKind.Station
            };
            foreach (MapMarkerKind kind in kinds)
            {
                MapMarkerPolicy policy = MapMarkerPoints.Evaluate(kind, false, true,
                    pointsToolActive: true);
                Assert.False(policy.Interactive);
                Assert.True(policy.Exists);
            }
        }

        [Fact]
        public void Evaluate_KeepsStaticMarkersHoverable()
        {
            // The eraser must still SEE the static markers: MapShapeEraser
            // intercepts the erase and removes the anchored shapes (a line
            // between two protected markers used to be unremovable), while
            // the marker itself is never destroyed. Same for the fix marker.
            MapMarkerPolicy dynamicPolicy = MapMarkerPoints.Evaluate(
                MapMarkerKind.Fix, false, true, false);
            Assert.True(dynamicPolicy.Interactive);

            MapMarkerPolicy staticPolicy = MapMarkerPoints.Evaluate(
                MapMarkerKind.Station, false, true, false);
            Assert.True(staticPolicy.Interactive);
            Assert.True(staticPolicy.PositionLocked);
        }

        [Fact]
        public void Evaluate_HiddenMarker_IsNotInteractive()
        {
            MapMarkerPolicy policy = MapMarkerPoints.Evaluate(
                MapMarkerKind.Lighthouse, false, visible: false,
                pointsToolActive: false);
            Assert.False(policy.Interactive);
            Assert.False(policy.Visible);
        }

        [Fact]
        public void Plan_CreatesMissing_AdoptsExisting_DestroysStale()
        {
            var desired = new List<string> { "RadioNav FIX", "RadioNav LH 1", "RadioNav LH 2" };
            var existing = new List<string> { "RadioNav LH 1", "RadioNav LH 9", "Point" };
            var create = new List<string>();
            var adopt = new List<string>();
            var destroy = new List<string>();

            MapMarkerPoints.Plan(desired, existing, create, adopt, destroy);

            Assert.Equal(new[] { "RadioNav FIX", "RadioNav LH 2" }, create);
            Assert.Equal(new[] { "RadioNav LH 1" }, adopt);
            Assert.Equal(new[] { "RadioNav LH 9" }, destroy); // "Point" is not ours
        }

        [Fact]
        public void Plan_IgnoresDuplicatesAndForeignNames()
        {
            var desired = new List<string> { "RadioNav FIX", "RadioNav FIX", "" };
            var existing = new List<string> { "RadioNav FIX", "RadioNav FIX", "Point", null };
            var create = new List<string>();
            var adopt = new List<string>();
            var destroy = new List<string>();

            MapMarkerPoints.Plan(desired, existing, create, adopt, destroy);

            Assert.Empty(create);
            Assert.Equal(new[] { "RadioNav FIX" }, adopt);
            Assert.Empty(destroy);
        }

        [Fact]
        public void KeyPrefixes_ScopeFamiliesForStaleCleanup()
        {
            Assert.StartsWith(MapMarkerPoints.LighthouseKeyPrefix, MapMarkerPoints.LighthouseKey("DE-042"));
            Assert.StartsWith(MapMarkerPoints.StationKeyPrefix, MapMarkerPoints.StationKey("DDH"));
            Assert.StartsWith(MapMarkerPoints.LopKeyPrefix, MapMarkerPoints.LopVertexKey(0));
            Assert.False(MapMarkerPoints.LighthouseKey("DE-042")
                .StartsWith(MapMarkerPoints.StationKeyPrefix, System.StringComparison.Ordinal));
            Assert.False(MapMarkerPoints.FixKey
                .StartsWith(MapMarkerPoints.LighthouseKeyPrefix, System.StringComparison.Ordinal));
        }

        [Fact]
        public void IsSanitizableName_CoversOwnedAndLegacyMcp()
        {
            Assert.True(MapMarkerPoints.IsSanitizableName("RadioNav FIX"));
            Assert.True(MapMarkerPoints.IsSanitizableName("RadioNav MCP 3"));
            Assert.True(MapMarkerPoints.IsSanitizableName("MCP Point")); // legacy literal
            Assert.False(MapMarkerPoints.IsSanitizableName("MCP Point 2"));
            Assert.False(MapMarkerPoints.IsSanitizableName("Some game point"));
            Assert.False(MapMarkerPoints.IsSanitizableName(""));
            Assert.False(MapMarkerPoints.IsSanitizableName(null));
        }

        [Fact]
        public void ShouldWritePosition_StaticAlways_DynamicOnlyWhenSourceMoved()
        {
            Assert.True(MapMarkerPoints.ShouldWritePosition(true, false, true));
            Assert.True(MapMarkerPoints.ShouldWritePosition(true, true, true));
            Assert.True(MapMarkerPoints.ShouldWritePosition(false, true, true));
            Assert.False(MapMarkerPoints.ShouldWritePosition(false, false, true)); // player drag survives
            Assert.False(MapMarkerPoints.ShouldWritePosition(true, true, false));
            Assert.False(MapMarkerPoints.ShouldWritePosition(false, true, false));
        }

        [Fact]
        public void ClampCreateCount_TruncatesToLimit()
        {
            Assert.Equal(0, MapMarkerPoints.ClampCreateCount(0, 10));
            Assert.Equal(5, MapMarkerPoints.ClampCreateCount(5, 10));
            Assert.Equal(10, MapMarkerPoints.ClampCreateCount(10, 10));
            Assert.Equal(10, MapMarkerPoints.ClampCreateCount(400, 10));
            Assert.Equal(0, MapMarkerPoints.ClampCreateCount(5, 0));
            Assert.Equal(0, MapMarkerPoints.ClampCreateCount(5, -1));
        }
    }
}
