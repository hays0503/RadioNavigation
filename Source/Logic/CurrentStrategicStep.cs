using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One bounded current contribution for a distant sandbox group. Encounter
    /// coordinates are kilometres and the field is metres per second, so the
    /// offset is divided by 1000. A long native interval is subdivided into
    /// fixed steps instead of being dropped, so a compressed clock keeps the
    /// transfer; an interval with no usable origin (never updated group) is
    /// rejected by the caller and reported, never invented.
    /// </summary>
    internal static class CurrentStrategicStep
    {
        internal const float MaxIntervalSeconds = 1800f;
        internal const int MaxSteps = 30 * 48;
        internal const float MaxSupportedSeconds = MaxIntervalSeconds * MaxSteps;

        internal static bool IsPositiveFinite(float seconds) =>
            seconds > 0f && !float.IsNaN(seconds) && !float.IsInfinity(seconds);

        /// <summary>Steps needed for an interval, or 0 when it is unusable.</summary>
        internal static int StepCount(float seconds)
        {
            if (!IsPositiveFinite(seconds))
                return 0;
            double steps = Math.Ceiling(seconds / MaxIntervalSeconds);
            return steps > MaxSteps ? 0 : (int)Math.Max(1d, steps);
        }

        internal static bool TryOffsetKm(float elapsedSeconds, Vector3 waterEastNorth, out Vector2 offsetKm)
        {
            offsetKm = Vector2.zero;
            if (!IsPositiveFinite(elapsedSeconds) || elapsedSeconds > MaxIntervalSeconds
                || float.IsNaN(waterEastNorth.x) || float.IsNaN(waterEastNorth.z)
                || float.IsInfinity(waterEastNorth.x) || float.IsInfinity(waterEastNorth.z))
                return false;
            offsetKm = WorldVelocity(Vector2.zero, waterEastNorth) * elapsedSeconds;
            return !float.IsNaN(offsetKm.x) && !float.IsNaN(offsetKm.y)
                && !float.IsInfinity(offsetKm.x) && !float.IsInfinity(offsetKm.y);
        }

        internal static Vector2 WorldVelocity(Vector2 stockKmPerSecond, Vector3 waterMetersPerSecond) =>
            stockKmPerSecond + new Vector2(waterMetersPerSecond.x, waterMetersPerSecond.z) * 0.001f;

        internal static Vector2 Midpoint(Vector2 start, Vector2 end, Vector2 drift, int index, int steps) =>
            steps <= 0 ? start : start + (end - start) * ((index + 0.5f) / steps) + drift;

        internal static DateTime MidpointUtc(DateTime end, long elapsedTicks, int index, int steps)
        {
            if (steps <= 0 || elapsedTicks < 0 || end == default(DateTime))
                return default(DateTime);
            long ticksBack = (long)(elapsedTicks * (1d - (index + 0.5d) / steps));
            return ticksBack > end.Ticks ? default(DateTime) : end.AddTicks(-ticksBack);
        }
    }
}
