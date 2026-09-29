using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the sky render-queue policy: the mod lowers its celestial material
    /// queue so the UniStorm cloud pass blends over the sky instead of below
    /// the star quads (live tuning is done through SkyRenderQueueSwitch).
    /// </summary>
    public sealed class SkyRenderQueuePolicyTests
    {
        [Fact]
        public void Clamp_PassesThroughValuesInsideTheBand()
        {
            Assert.Equal(1000, SkyRenderQueuePolicy.Clamp(1000));
            Assert.Equal(2000, SkyRenderQueuePolicy.Clamp(2000));
            Assert.Equal(2900, SkyRenderQueuePolicy.Clamp(2900));
            Assert.Equal(3500, SkyRenderQueuePolicy.Clamp(3500));
        }

        [Fact]
        public void Clamp_RaisesValuesBelowTheFloor()
        {
            Assert.Equal(SkyRenderQueuePolicy.MinQueue, SkyRenderQueuePolicy.Clamp(1));
            Assert.Equal(SkyRenderQueuePolicy.MinQueue, SkyRenderQueuePolicy.Clamp(999));
        }

        [Fact]
        public void Clamp_LowersValuesAboveTheCeiling()
        {
            Assert.Equal(SkyRenderQueuePolicy.MaxQueue, SkyRenderQueuePolicy.Clamp(3501));
            Assert.Equal(SkyRenderQueuePolicy.MaxQueue, SkyRenderQueuePolicy.Clamp(6000));
            Assert.Equal(SkyRenderQueuePolicy.MaxQueue, SkyRenderQueuePolicy.Clamp(int.MaxValue));
        }

        [Fact]
        public void Clamp_KeepsOffAtZero()
        {
            Assert.Equal(0, SkyRenderQueuePolicy.Off);
            Assert.Equal(SkyRenderQueuePolicy.Off, SkyRenderQueuePolicy.Clamp(0));
            Assert.Equal(SkyRenderQueuePolicy.Off, SkyRenderQueuePolicy.Clamp(-5));
        }

        [Fact]
        public void TryApply_IgnoresNullMaterialAndOffQueue()
        {
            Assert.False(SkyRenderQueuePolicy.TryApply(null, 2900));

            var material = new Material { renderQueue = 3000 };
            Assert.False(SkyRenderQueuePolicy.TryApply(material, SkyRenderQueuePolicy.Off));
            Assert.Equal(3000, material.renderQueue);
        }

        [Fact]
        public void TryApply_WritesTheClampedQueueAndReportsSuccess()
        {
            var material = new Material { renderQueue = 3000 };
            Assert.True(SkyRenderQueuePolicy.TryApply(material, 2900));
            Assert.Equal(2900, material.renderQueue);

            Assert.True(SkyRenderQueuePolicy.TryApply(material, 50));
            Assert.Equal(SkyRenderQueuePolicy.MinQueue, material.renderQueue);

            Assert.True(SkyRenderQueuePolicy.TryApply(material, 9999));
            Assert.Equal(SkyRenderQueuePolicy.MaxQueue, material.renderQueue);
        }

        [Fact]
        public void Switch_DefaultsOffAndRoundTrips()
        {
            SkyRenderQueueSwitch.Reset();
            Assert.Equal(SkyRenderQueuePolicy.Off, SkyRenderQueueSwitch.QueueOverride);

            SkyRenderQueueSwitch.Set(2900);
            Assert.Equal(2900, SkyRenderQueueSwitch.QueueOverride);

            SkyRenderQueueSwitch.Reset();
            Assert.Equal(SkyRenderQueuePolicy.Off, SkyRenderQueueSwitch.QueueOverride);
        }
    }
}
