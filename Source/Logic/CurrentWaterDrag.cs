using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Isolated water-only reference for F1; not an extra force on top of stock NNP drag.
    internal static class CurrentWaterDrag
    {
        // Coefficients are non-negative and already divided by mass; time is simulated seconds.
        // Scene axes: +X east, +Z north, +Y up. Water flow has no vertical component.
        public static Vector3 Advance(Vector3 worldVelocity, CurrentVelocity water,
            float linearPerSecond, float quadraticPerMeter, float seconds)
        {
            if (seconds <= 0f || (linearPerSecond == 0f && quadraticPerMeter == 0f))
                return worldVelocity;

            double x = (double)worldVelocity.x - water.EastMetersPerSecond;
            double y = worldVelocity.y;
            double z = (double)worldVelocity.z - water.NorthMetersPerSecond;
            double speed = Math.Sqrt(x * x + y * y + z * z);
            if (speed == 0.0)
                return worldVelocity;

            // Solve s' = -linear*s - quadratic*s^2 along the relative velocity ray.
            double exposure = (double)linearPerSecond * seconds;
            double linearLoss = exposure < 1e-4
                ? exposure * (1.0 - 0.5 * exposure)
                : 1.0 - Math.Exp(-exposure);
            double wetSeconds = linearPerSecond == 0f ? seconds : linearLoss / linearPerSecond;
            double quadraticLoss = quadraticPerMeter * speed * wetSeconds;
            double retained = (1.0 - linearLoss) / (1.0 + quadraticLoss);
            return new Vector3(
                (float)(water.EastMetersPerSecond + x * retained),
                (float)(y * retained),
                (float)(water.NorthMetersPerSecond + z * retained));
        }
    }
}
