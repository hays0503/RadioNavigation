using System;

namespace UBOAT.Mods.RadioNavigation
{
    // Constant-flow heading oracle in the game's local east/north frame.
    // Inputs are desired ground-track direction and *available* through-water
    // speed, not a ground-speed readout. Not wired to the native autopilot.
    internal static class CurrentRouteSteering
    {
        internal struct Solution
        {
            public float HeadingEast { get; }
            public float HeadingNorth { get; }
            public float GroundMetersPerSecond { get; }

            public Solution(double east, double north, double groundSpeed)
            {
                HeadingEast = (float)east;
                HeadingNorth = (float)north;
                GroundMetersPerSecond = (float)groundSpeed;
            }
        }

        internal static bool TrySolve(double trackEast, double trackNorth, float speed,
            float flowEast, float flowNorth, out Solution result)
        {
            result = default(Solution);
            double length = Math.Sqrt(trackEast * trackEast + trackNorth * trackNorth);
            if (!(length > 0) || double.IsInfinity(length) || !(speed > 0) || float.IsInfinity(speed))
                return false;
            double east = trackEast / length;
            double north = trackNorth / length;
            // Orthogonal projection. Cancel sideways current with the same
            // magnitude of thrust-relative velocity and retain the forward root.
            double cross = (flowEast * trackNorth - flowNorth * trackEast) / length;
            double speedLeft = (double)speed * speed - cross * cross;
            if (speedLeft < 0)
                return false;
            double ahead = Math.Sqrt(speedLeft);
            double groundSpeed = ahead + (flowEast * trackEast + flowNorth * trackNorth) / length;
            if (!(groundSpeed > 0) || groundSpeed > float.MaxValue)
                return false;
            result = new Solution((east * ahead - north * cross) / speed,
                (north * ahead + east * cross) / speed, groundSpeed);
            return true;
        }
    }
}
