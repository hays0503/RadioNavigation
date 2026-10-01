using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The PIP cameras' clip planes (author's report, 2026-10-02: "артефакты на
    /// краях при резких поворотах").
    ///
    /// The defect was found by measurement, not by a guess, and the measurement
    /// is worth recording because it is what identified the cause among five
    /// candidates: a razor-sharp horizontal boundary crossed the ocular about
    /// 7 degrees below the axis, with sky above it and sea below; it moved as
    /// the boat pitched; and it was present ONLY inside the ocular, while the
    /// main view (far 4500) never showed it. A boundary that tracks pitch is a
    /// world-distance cut, and a world-distance cut that only the ocular shows
    /// is a far-plane problem. The eye camera's far clip is tuned for the bridge
    /// window and the game rewrites it every frame, so mirroring it into the PIP
    /// sliced the ocean.
    ///
    /// What this suite pins is the *number's* relationship to the two distances
    /// that were measured live, so nobody can quietly set it back to whatever the
    /// eye camera happens to report.
    /// </summary>
    public class SextantPipClipTests
    {
        [Fact]
        public void ThePipFarClipMustClearTheHorizonAndTheSkyShell()
        {
            Assert.True(SextantPipClip.FarPlaneClearsTheWorld(1.5),
                "the PIP far clip must clear both the 3900 m shell and the main camera's 4500 m, with margin");
            Assert.True(SextantPipClip.FarPlane > SextantPipClip.MainCameraFarPlane);
            Assert.True(SextantPipClip.FarPlane > SextantPipClip.CelestialShellRadius);
        }

        [Theory]
        // Matching a measured distance is not good enough - the numbers have to be
        // BEATEN, or the defect returns the moment the weather or the scale moves.
        [InlineData(5000.0, false)]   // clears the shell, not the main camera with margin
        [InlineData(4500.0, false)]   // equals the main camera: no margin
        [InlineData(6750.0, false)]   // exactly 1.5x the main camera: still not beaten
        [InlineData(5850.0, false)]   // exactly 1.5x the shell: still not beaten
        [InlineData(9000.0, true)]
        [InlineData(double.NaN, false)]
        [InlineData(double.PositiveInfinity, false)]
        public void TheMarginRuleBeatsRatherThanMatchesTheMeasuredDistances(double far, bool expected)
        {
            Assert.Equal(expected, SextantPipClip.FarPlaneClearsTheWorld(far, 1.5));
        }

        [Theory]
        [InlineData(1.5, true)]
        [InlineData(1.0, true)]    // "strictly greater" is still a real requirement
        [InlineData(0.9, false)]   // a sub-unity margin would allow matching
        [InlineData(0.0, false)]
        [InlineData(double.NaN, false)]
        public void TheMarginItselfIsBoundedBelowByOne(double margin, bool expected)
        {
            Assert.Equal(expected, SextantPipClip.FarPlaneClearsTheWorld(SextantPipClip.FarPlane, margin));
        }

        [Fact]
        public void ThePipNearClipFloorIsPositiveAndTiny()
        {
            // A zero or negative near plane collapses the projection, so the
            // mirrored value is floored - but the floor must stay small enough
            // not to clip the interior, which is centimetres from the eye.
            Assert.True(SextantPipClip.MinNearPlane > 0d);
            Assert.True(SextantPipClip.MinNearPlane <= 0.1,
                "the floor must not eat the interior's near geometry");
            Assert.True(SextantPipClip.FarPlane > SextantPipClip.MinNearPlane * 1000d,
                "far must be far above near or the depth buffer loses everything");
        }
    }
}
