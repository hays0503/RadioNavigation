using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentProfileFieldTests
    {
        private static CurrentQuery At(float depth) => new CurrentQuery { DepthMeters = depth };

        private static CurrentProfileField Parse(string raw)
        {
            Assert.True(CurrentProfileField.TryParse(raw, out var field, out var error), error);
            return field;
        }

        [Fact]
        public void InterpolatesBetweenLayersAndClampsOutsideThem()
        {
            var field = Parse("0:1,0;10:0,2");

            Assert.True(field.HasFlow);
            Assert.True(field.TrySample(At(0), out var surface));
            Assert.True(field.TrySample(At(5), out var middle));
            Assert.True(field.TrySample(At(10), out var deep));
            Assert.True(field.TrySample(At(1000), out var deeper));

            Assert.Equal(1f, surface.x, 6);
            Assert.Equal(0f, surface.z, 6);
            Assert.Equal(0.5f, middle.x, 6);
            Assert.Equal(1f, middle.z, 6);
            Assert.Equal(0f, deep.x, 6);
            Assert.Equal(2f, deep.z, 6);
            Assert.Equal(deep, deeper);
        }

        [Fact]
        public void SingleLayerIsAConstantFieldIncludingZero()
        {
            var field = Parse("12:0.25,-0.5");
            Assert.True(field.TrySample(At(0), out var atSurface));
            Assert.True(field.TrySample(At(500), out var atDepth));
            Assert.Equal(new Vector3(0.25f, 0f, -0.5f), atSurface);
            Assert.Equal(atSurface, atDepth);

            var zero = Parse("0:0,0;30:0,0");
            Assert.False(zero.HasFlow);
            Assert.True(zero.TrySample(At(15), out var still));
            Assert.Equal(Vector3.zero, still);
        }

        [Fact]
        public void RejectsInvalidDepthsAndComponents()
        {
            foreach (string raw in new[]
            {
                "", "0:1", "0:1,0;0:2,0", "5:1,0;2:1,0", "-1:1,0",
                "0:NaN,0", "0:1,0;10:2", "0:1,0;10:Infinity,0", "0:1,0;"
            })
            {
                Assert.False(CurrentProfileField.TryParse(raw, out var field, out var error));
                Assert.Null(field);
                Assert.False(string.IsNullOrEmpty(error));
            }
        }

        [Fact]
        public void RejectsTooManyLayers()
        {
            var raw = new System.Text.StringBuilder();
            for (int i = 0; i <= 32; i++)
            {
                if (i > 0)
                    raw.Append(';');
                raw.Append(i).Append(":1,0");
            }
            Assert.False(CurrentProfileField.TryParse(raw.ToString(), out _, out _));
        }

        [Fact]
        public void UsesInvariantCultureAndRejectsNonFiniteQueries()
        {
            var field = Parse("0:1.5,-2.25");
            Assert.True(field.TrySample(At(0), out var value));
            Assert.Equal(1.5f, value.x, 6);
            Assert.Equal(-2.25f, value.z, 6);
            Assert.False(field.TrySample(At(float.NaN), out _));
            Assert.False(field.TrySample(At(float.PositiveInfinity), out _));
        }
    }
}
