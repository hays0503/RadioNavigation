using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    internal struct CurrentVelocity
    {
        public float EastMetersPerSecond { get; }
        public float NorthMetersPerSecond { get; }

        public CurrentVelocity(float eastMetersPerSecond, float northMetersPerSecond)
        {
            EastMetersPerSecond = eastMetersPerSecond;
            NorthMetersPerSecond = northMetersPerSecond;
        }
    }

    internal sealed class ConstantCurrentField : ICurrentField
    {
        private readonly CurrentVelocity velocity;

        public ConstantCurrentField(float eastMetersPerSecond, float northMetersPerSecond)
        {
            velocity = new CurrentVelocity(eastMetersPerSecond, northMetersPerSecond);
        }

        public bool HasFlow => velocity.EastMetersPerSecond != 0f || velocity.NorthMetersPerSecond != 0f;

        public bool TrySample(CurrentQuery query, out Vector3 value)
        {
            value = Vector3.zero;
            float depthMeters = query.DepthMeters;
            if (!(depthMeters >= 0f) || float.IsInfinity(depthMeters))
                return false;
            value = new Vector3(velocity.EastMetersPerSecond, 0f, velocity.NorthMetersPerSecond);
            return true;
        }

        // Same query coordinates as the future atlas; this field is uniform in space and time.
        public CurrentVelocity Sample(double latitudeDegrees, double longitudeDegrees, double depthMeters, DateTime utc)
        {
            // A uniform field still answers a valid query; a non-finite or
            // unset query is a caller bug, not another kind of still water.
            if (double.IsNaN(latitudeDegrees) || double.IsInfinity(latitudeDegrees)
                || double.IsNaN(longitudeDegrees) || double.IsInfinity(longitudeDegrees)
                || double.IsNaN(depthMeters) || double.IsInfinity(depthMeters)
                || utc == default(DateTime))
                throw new ArgumentException("Invalid atlas query", nameof(latitudeDegrees));
            return velocity;
        }
    }
}
