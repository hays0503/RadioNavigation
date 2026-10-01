using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantRimProfileTests
    {
        [Fact]
        public void InteriorRemainsClearAndTheTubeShadowIsPreserved()
        {
            SextantRimSample centre = SextantRimProfile.Evaluate(0.5f, 0f);
            SextantRimSample shoulder = SextantRimProfile.Evaluate(0.7f, 0f);

            Assert.Equal(0f, centre.A, 6);
            Assert.InRange(shoulder.A, 0.20f, 0.25f);
            Assert.Equal(0f, shoulder.R, 6);
            Assert.Equal(0f, shoulder.G, 6);
            Assert.Equal(0f, shoulder.B, 6);
        }

        [Fact]
        public void BezelIsAThinMutedBandInsteadOfASecondBroadVignette()
        {
            SextantRimSample bevel = SextantRimProfile.Evaluate(0.95f, 1f);
            SextantRimSample inside = SextantRimProfile.Evaluate(0.90f, 1f);
            SextantRimSample outside = SextantRimProfile.Evaluate(0.985f, 1f);

            Assert.True(bevel.R > inside.R + 0.08f);
            Assert.True(bevel.G > inside.G + 0.08f);
            Assert.True(bevel.R > outside.R + 0.08f);
            Assert.InRange(bevel.A, 0.90f, 1f);
        }

        [Fact]
        public void BezelHighlightHasAQuietDirectionalReflection()
        {
            SextantRimSample lit = SextantRimProfile.Evaluate(0.95f, 1f);
            SextantRimSample opposite = SextantRimProfile.Evaluate(0.95f, -1f);

            Assert.True(lit.R > opposite.R);
            Assert.True(lit.G > opposite.G);
            Assert.InRange(lit.R, 0f, 0.35f);
        }

        [Fact]
        public void RimOverlapCoversTheDefectZone()
        {
            // The author's fix (2026-10-02): the ring must be OPAQUE from
            // 0.90 outward, overlapping the zone where the turn arcs were
            // measured (r/R 0.93-1.02). Opacity is the invariant, not
            // blackness: the steel bevel lives on top of the black tube and
            // is the designed bezel highlight (static, turn-independent), so
            // only the alpha is pinned here. Where alpha is 1 no smeared
            // scene content can show through, whatever it is.
            for (float r = 0.90f; r <= 1.02f; r += 0.01f)
            {
                SextantRimSample sample = SextantRimProfile.Evaluate(r, 1f);
                Assert.True(sample.A >= 0.98f,
                    "rim must be opaque at r=" + r.ToString("F2") + ", got A=" + sample.A);
            }
            // ...and the glass just inside the overlap stays clear.
            SextantRimSample glass = SextantRimProfile.Evaluate(0.84f, 0f);
            Assert.True(glass.A < 0.6f, "glass must stay clear at r=0.84, got A=" + glass.A);
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(-1f)]
        [InlineData(0f)]
        public void NoRadialBandEndsInASinglePixelStep(float light)
        {
            // The author's staircase complaint (2026-10-01): the rim overlay's
            // bands had a 0.002 softness, so the bright steel bevel dropped
            // 39 -> 4 luminance in one pixel and the silhouette read as stairs.
            // The budget is expressed per PIXEL, because that is how the defect
            // appears on screen and it scales with the ocular size and uiScale.
            // Both lighting extremes are covered: the bright side is where the
            // step against the sky is worst.
            const float step = SextantRimProfile.RadiusPerPixel;
            float worst = 0f;
            string worstAt = "n/a";

            float previous = Luminance(SextantRimProfile.Evaluate(0f, light), 1f);
            for (float r = step; r <= 1.2f; r += step)
            {
                // Composited over the brightest thing behind the rim (a sunlit
                // sky) — the case where a step is most visible.
                float luma = Luminance(SextantRimProfile.Evaluate(r, light), 1f);
                float rise = Math.Abs(luma - previous);
                if (rise > worst)
                {
                    worst = rise;
                    worstAt = "r=" + r.ToString("F4");
                }
                previous = luma;
            }

            Assert.True(worst <= SextantRimProfile.MaxLuminanceRisePerPixel,
                "the composited rim must not step more than "
                    + SextantRimProfile.MaxLuminanceRisePerPixel + " luminance/px, got "
                    + worst.ToString("F4") + " at " + worstAt
                    + " (light " + light.ToString("F0") + ")");
        }

        private static float Luminance(SextantRimSample s, float background)
        {
            return (0.299f * s.R + 0.587f * s.G + 0.114f * s.B) * s.A
                + background * (1f - s.A);
        }

        [Fact]
        public void ShadeAlwaysCoversTheApertureAtEveryOcularSize()
        {
            // The angular rim (author 2026-10-01, seen at 2560x1440): the
            // eyecup was a FIXED 1000 units while the ocular is a fraction of
            // the screen height, so at 1267 units the shade no longer reached
            // past the aperture and the Mask's binary stencil cut became the
            // silhouette — 74 -> 0 luminance in a single pixel.
            //
            // The sweep is the whole point: the bug only appears at the sizes
            // where the ocular outgrows the fixed shade, so testing one size
            // would have passed with the defect in place.
            double[] ocularSizes = { 400d, 603.43d, 674d, 900d, 1126d, 1267d, 1600d, 2400d, 4000d };
            foreach (double ocular in ocularSizes)
            {
                Assert.True(SextantRimProfile.ShadeCoversAperture(ocular),
                    "shade must hide the stencil cut at ocular=" + ocular
                        + " units (opaque to " + SextantRimProfile.ShadeOpaqueRadiusUnits(ocular)
                        + ", aperture " + SextantRimProfile.ApertureRadiusUnits(ocular) + ")");
                // ...and it must stay a sane size: not a screen-filling box.
                double shadeSide = ocular * SextantRimProfile.EyecupFactor;
                Assert.InRange(shadeSide / ocular, 1.2, 2.5);
            }

            // A degenerate ocular is rejected rather than trusted.
            Assert.False(SextantRimProfile.ShadeCoversAperture(0d));
            Assert.False(SextantRimProfile.ShadeCoversAperture(double.NaN));
            Assert.False(SextantRimProfile.ShadeCoversAperture(double.PositiveInfinity));
        }

        [Fact]
        public void ShadeFactorHasMarginOverTheCoverageThreshold()
        {
            // Coverage needs factor > 1 / EyecupSolidTo. Landing exactly on the
            // threshold would put the stencil cut at the last opaque pixel, so
            // the margin is part of the contract.
            double threshold = 1d / SextantRimProfile.EyecupSolidTo;
            Assert.True(SextantRimProfile.EyecupFactor > threshold,
                "EyecupFactor " + SextantRimProfile.EyecupFactor
                    + " must clear the coverage threshold " + threshold);
            Assert.True(SextantRimProfile.EyecupFactor <= threshold + 0.2d,
                "EyecupFactor must not grow so far that the shade swallows the screen");
        }

        [Fact]
        public void InvalidRadiusFallsBackToTheOpaqueOuterLip()
        {
            SextantRimSample sample = SextantRimProfile.Evaluate(float.NaN, 0f);
            Assert.Equal(1f, sample.A, 6);
            Assert.InRange(sample.R, 0f, 1f);
            Assert.InRange(sample.G, 0f, 1f);
            Assert.InRange(sample.B, 0f, 1f);
        }
    }
}
