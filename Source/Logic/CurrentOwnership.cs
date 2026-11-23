using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal enum CurrentOwner
    {
        Physical = 0,
        Strategic = 1
    }

    // One-axis constant-equilibrium reference. This is not a game-time hook,
    // route planner, persistence schema or the production two-axis state.
    internal sealed class CurrentOwnership
    {
        public long Epoch { get; private set; }
        public double Position { get; private set; }
        public double WorldSpeed { get; private set; }
        public CurrentOwner Owner { get; private set; }

        public CurrentOwnership(long epoch, double position, double worldSpeed)
        {
            Epoch = epoch;
            Position = position;
            WorldSpeed = worldSpeed;
            Owner = CurrentOwner.Physical;
        }

        public bool Handoff(long epoch, CurrentOwner from, CurrentOwner to)
        {
            if (epoch != Epoch || from != Owner || to == from
                || (to != CurrentOwner.Physical && to != CurrentOwner.Strategic))
                return false;
            Owner = to;
            return true;
        }

        public bool PublishPhysical(long start, long end, double position, double worldSpeed)
        {
            if (!Owns(start, end, CurrentOwner.Physical) || !IsFinite(position) || !IsFinite(worldSpeed))
                return false;
            Commit(end, position, worldSpeed);
            return true;
        }

        public bool AdvanceStrategic(long start, long end, double equilibrium, double relaxationSeconds)
        {
            if (!Owns(start, end, CurrentOwner.Strategic) || !IsFinite(equilibrium)
                || !IsFinite(relaxationSeconds) || relaxationSeconds <= 0.0)
                return false;
            double elapsed = (double)end - start;
            double fraction = elapsed / relaxationSeconds;
            double lost = fraction < 1e-4 ? fraction * (1.0 - 0.5 * fraction) : 1.0 - Math.Exp(-fraction);
            double difference = WorldSpeed - equilibrium;
            double speed = WorldSpeed - difference * lost;
            double position = Position + equilibrium * elapsed + difference * relaxationSeconds * lost;
            if (!IsFinite(speed) || !IsFinite(position))
                return false;
            Commit(end, position, speed);
            return true;
        }

        private bool Owns(long start, long end, CurrentOwner owner)
        {
            return Epoch == start && end > start && Owner == owner;
        }

        private void Commit(long end, double position, double speed)
        {
            Epoch = end;
            Position = position;
            WorldSpeed = speed;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
