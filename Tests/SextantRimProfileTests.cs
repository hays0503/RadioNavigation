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
