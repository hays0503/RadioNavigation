using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SolarFlareRenderStateTests
    {
        [Fact]
        public void ReplacementSun_HidesItsSeparatePostprocessFlare_AndRestoresAllThree()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = true };
            var flare = new Renderer { enabled = true };
            var state = CelestialRenderState.Begin(sun, true, moon, true, flare);
            Assert.False(sun.enabled);
            Assert.False(moon.enabled);
            Assert.False(flare.enabled);
            state.Restore();
            Assert.True(sun.enabled);
            Assert.True(moon.enabled);
            Assert.True(flare.enabled);
            state.Restore();
            Assert.True(flare.enabled);
        }

        [Fact]
        public void MissingReplacement_DoesNotTouchTheSolarFlare()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = true };
            var flare = new Renderer { enabled = true };
            var state = CelestialRenderState.Begin(sun, false, moon, true, flare);
            Assert.True(sun.enabled);
            Assert.True(flare.enabled);
            Assert.False(moon.enabled);
            state.Restore();
            Assert.True(flare.enabled);
        }

        [Fact]
        public void AlreadyDisabledFlare_StaysDisabledAfterTheCamera()
        {
            var sun = new Renderer { enabled = true };
            var flare = new Renderer { enabled = false };
            var state = CelestialRenderState.Begin(sun, true, null, false, flare);
            state.Restore();
            Assert.True(sun.enabled);
            Assert.False(flare.enabled);
        }

        [Fact]
        public void NestedRender_DoesNotPrematurelyExposeTheSolarFlare()
        {
            var sun = new Renderer { enabled = true };
            var flare = new Renderer { enabled = true };
            var outer = CelestialRenderState.Begin(sun, true, null, false, flare);
            var inner = CelestialRenderState.Begin(sun, true, null, false, flare);
            inner.Restore();
            Assert.False(sun.enabled);
            Assert.False(flare.enabled);
            outer.Restore();
            Assert.True(sun.enabled);
            Assert.True(flare.enabled);
        }

        [Fact]
        public void FlareSetterFailure_RestoresDiscsBeforeRethrowing()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = true };
            var flare = new Renderer { enabled = true, ThrowOnDisable = true };
            Assert.Throws<InvalidOperationException>(() =>
                CelestialRenderState.Begin(sun, true, moon, true, flare));
            Assert.True(sun.enabled);
            Assert.True(moon.enabled);
            Assert.True(flare.enabled);
        }
    }
}
