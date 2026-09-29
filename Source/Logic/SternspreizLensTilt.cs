using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Very small, slow observer-frame roll of the Sternspreiz streaks. This is
    /// an author-requested visual motion (not a measured optical property); the
    /// star centre and angular dimensions never move. The caller rebuilds only
    /// the small capped lens mesh at the update cadence returned here.
    /// </summary>
    public static class SternspreizLensTilt
    {
        public const float AmplitudeDeg = 0.25f;
        public const float PeriodSeconds = 8f;
        public const float MinAngleStepDeg = 0.015f;
        public const float MinUpdateIntervalSeconds = 0.1f;

        public static float AngleDeg(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
                return 0f;
            double phase = elapsedSeconds * (2d * Math.PI / PeriodSeconds);
            return AmplitudeDeg * (float)Math.Sin(phase);
        }

        public static bool ShouldUpdate(float previousAngleDeg, float currentAngleDeg,
            float previousRealtime, float nowRealtime)
        {
            if (float.IsNaN(currentAngleDeg) || float.IsInfinity(currentAngleDeg))
                return false;
            if (float.IsNaN(previousAngleDeg) || float.IsInfinity(previousAngleDeg))
                return true;
            if (Math.Abs(currentAngleDeg - previousAngleDeg) < MinAngleStepDeg)
                return false;
            return nowRealtime - previousRealtime >= MinUpdateIntervalSeconds;
        }

        /// <summary>Rotates only the camera's image-plane right/up basis.</summary>
        public static LensCamera ApplyRoll(LensCamera camera, float angleDeg)
        {
            if (float.IsNaN(angleDeg) || float.IsInfinity(angleDeg) || angleDeg == 0f)
                return camera;

            float radians = angleDeg * (float)(Math.PI / 180d);
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);

            float rightX = camera.Rx;
            float rightY = camera.Ry;
            float rightZ = camera.Rz;
            float upX = camera.Ux;
            float upY = camera.Uy;
            float upZ = camera.Uz;

            camera.Rx = rightX * cos + upX * sin;
            camera.Ry = rightY * cos + upY * sin;
            camera.Rz = rightZ * cos + upZ * sin;
            camera.Ux = upX * cos - rightX * sin;
            camera.Uy = upY * cos - rightY * sin;
            camera.Uz = upZ * cos - rightZ * sin;
            return camera;
        }
    }
}
