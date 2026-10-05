using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SternspreizLensTiltTests
    {
        [Fact]
        public void Angle_IsSlowAndBoundedAroundHorizontal()
        {
            Assert.Equal(0f, SternspreizLensTilt.AngleDeg(0d), 6);
            Assert.Equal(SternspreizLensTilt.AmplitudeDeg,
                SternspreizLensTilt.AngleDeg(SternspreizLensTilt.PeriodSeconds * 0.25d), 5);
            Assert.Equal(0f, SternspreizLensTilt.AngleDeg(SternspreizLensTilt.PeriodSeconds * 0.5d), 5);
            Assert.Equal(-SternspreizLensTilt.AmplitudeDeg,
                SternspreizLensTilt.AngleDeg(SternspreizLensTilt.PeriodSeconds * 0.75d), 5);

            for (int i = 0; i < 1000; i++)
            {
                float angle = SternspreizLensTilt.AngleDeg(i * 0.037d);
                Assert.InRange(angle, -SternspreizLensTilt.AmplitudeDeg,
                    SternspreizLensTilt.AmplitudeDeg);
            }
        }

        [Fact]
        public void UpdateGateThrottlesSmallAngleChanges()
        {
            float amplitude = SternspreizLensTilt.AmplitudeDeg;
            Assert.True(SternspreizLensTilt.ShouldUpdate(float.NaN, 0f, 0f, 0f));
            Assert.False(SternspreizLensTilt.ShouldUpdate(0f,
                SternspreizLensTilt.MinAngleStepDeg * 0.5f, 0f, 1f));
            Assert.False(SternspreizLensTilt.ShouldUpdate(0f, amplitude, 0f,
                SternspreizLensTilt.MinUpdateIntervalSeconds * 0.5f));
            Assert.True(SternspreizLensTilt.ShouldUpdate(0f, amplitude, 0f,
                SternspreizLensTilt.MinUpdateIntervalSeconds));
        }

        [Fact]
        public void ApplyRollRotatesOnlyTheImagePlaneBasis()
        {
            var camera = new LensCamera
            {
                Px = 12f, Py = 34f, Pz = 56f,
                Rx = 1f, Ry = 0f, Rz = 0f,
                Ux = 0f, Uy = 1f, Uz = 0f,
                Fx = 0f, Fy = 0f, Fz = 1f,
                TanHalfFovY = 0.6f
            };

            LensCamera rolled = SternspreizLensTilt.ApplyRoll(camera, 90f);

            Assert.Equal(camera.Px, rolled.Px);
            Assert.Equal(camera.Py, rolled.Py);
            Assert.Equal(camera.Pz, rolled.Pz);
            Assert.Equal(0f, rolled.Rx, 5);
            Assert.Equal(1f, rolled.Ry, 5);
            Assert.Equal(-1f, rolled.Ux, 5);
            Assert.Equal(0f, rolled.Uy, 5);
            Assert.Equal(camera.Fx, rolled.Fx);
            Assert.Equal(camera.Fy, rolled.Fy);
            Assert.Equal(camera.Fz, rolled.Fz);
            Assert.Equal(1f, rolled.Rx * rolled.Rx + rolled.Ry * rolled.Ry + rolled.Rz * rolled.Rz, 5);
            Assert.Equal(1f, rolled.Ux * rolled.Ux + rolled.Uy * rolled.Uy + rolled.Uz * rolled.Uz, 5);
        }
    }
}
