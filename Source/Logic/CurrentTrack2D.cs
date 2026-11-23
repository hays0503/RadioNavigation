using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Serialized motion state: epoch, owner, position and world velocity.
    /// Plain fields only, so the game's serializer can store it as-is.
    /// </summary>
    internal sealed class CurrentTrackPayload
    {
        public int Version;
        public long Epoch;
        public int Owner;
        /// <summary>Event epoch still awaiting acknowledgement, or -1.</summary>
        public long PendingEventEpoch;
        public double EastMeters;
        public double NorthMeters;
        public double VelocityEastMetersPerSecond;
        public double VelocityNorthMetersPerSecond;
    }

    /// <summary>
    /// Two-axis strategic track with one owner per interval and an explicit
    /// barrier before an unhandled event. Exact relaxation toward a constant
    /// equilibrium, so chunking and frame budgets cannot change the result.
    /// Epochs are nonnegative game ticks (100 ns), not whole seconds.
    /// Not yet connected to game time, groups or the campaign.
    /// </summary>
    internal sealed class CurrentTrack2D
    {
        public const int PayloadVersion = 1;

        public long Epoch { get; private set; }
        public CurrentOwner Owner { get; private set; }
        public double EastMeters { get; private set; }
        public double NorthMeters { get; private set; }
        public double VelocityEastMetersPerSecond { get; private set; }
        public double VelocityNorthMetersPerSecond { get; private set; }
        public long PendingEventEpoch { get; private set; }

        public CurrentTrack2D(long epoch, double eastMeters, double northMeters,
            double velocityEastMetersPerSecond, double velocityNorthMetersPerSecond)
        {
            if (epoch < 0)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            if (!IsFinite(eastMeters) || !IsFinite(northMeters)
                || !IsFinite(velocityEastMetersPerSecond) || !IsFinite(velocityNorthMetersPerSecond))
                throw new ArgumentOutOfRangeException(nameof(eastMeters));
            Epoch = epoch;
            Owner = CurrentOwner.Physical;
            EastMeters = eastMeters;
            NorthMeters = northMeters;
            VelocityEastMetersPerSecond = velocityEastMetersPerSecond;
            VelocityNorthMetersPerSecond = velocityNorthMetersPerSecond;
            PendingEventEpoch = -1;
        }

        public bool Handoff(long epoch, CurrentOwner from, CurrentOwner to)
        {
            if (epoch != Epoch || from != Owner || to == from || PendingEventEpoch >= 0)
                return false;
            if (to != CurrentOwner.Physical && to != CurrentOwner.Strategic)
                return false;
            Owner = to;
            return true;
        }

        /// <summary>
        /// Free world position published by the physical branch for a completed
        /// interval. Velocity is kept; the physical branch owns its own forces.
        /// </summary>
        public bool PublishPhysical(long start, long end, double eastMeters, double northMeters,
            double velocityEastMetersPerSecond, double velocityNorthMetersPerSecond)
        {
            if (Owner != CurrentOwner.Physical || PendingEventEpoch >= 0
                || !OwnsInterval(start, end)
                || !IsFinite(eastMeters) || !IsFinite(northMeters)
                || !IsFinite(velocityEastMetersPerSecond) || !IsFinite(velocityNorthMetersPerSecond))
                return false;
            Epoch = end;
            EastMeters = eastMeters;
            NorthMeters = northMeters;
            VelocityEastMetersPerSecond = velocityEastMetersPerSecond;
            VelocityNorthMetersPerSecond = velocityNorthMetersPerSecond;
            return true;
        }

        public bool AdvanceStrategic(long start, long end, double equilibriumEast,
            double equilibriumNorth, double relaxationSeconds)
        {
            long barrier;
            return AdvanceStrategic(start, end, equilibriumEast, equilibriumNorth,
                relaxationSeconds, out barrier);
        }

        /// <summary>
        /// Advances toward a constant equilibrium. When <paramref name="earliestEventEpoch"/>
        /// lies inside the interval, motion stops exactly at it, the event becomes
        /// pending, and no further advance is accepted until it is acknowledged.
        /// </summary>
        public bool AdvanceUntilEvent(long start, long end, double equilibriumEast,
            double equilibriumNorth, double relaxationSeconds, long earliestEventEpoch,
            out long stoppedAtEpoch)
        {
            stoppedAtEpoch = -1;
            if (earliestEventEpoch < -1 || (earliestEventEpoch >= 0 && earliestEventEpoch < start))
                return false;
            if (earliestEventEpoch == start)
            {
                if (Owner != CurrentOwner.Strategic || PendingEventEpoch >= 0 || !OwnsInterval(start, end))
                    return false;
                PendingEventEpoch = stoppedAtEpoch = start;
                return true;
            }
            if (earliestEventEpoch >= 0 && earliestEventEpoch <= end)
            {
                long barrier;
                if (!AdvanceStrategic(start, earliestEventEpoch, equilibriumEast, equilibriumNorth,
                    relaxationSeconds, out barrier))
                    return false;
                PendingEventEpoch = earliestEventEpoch;
                stoppedAtEpoch = earliestEventEpoch;
                return true;
            }
            long unused;
            return AdvanceStrategic(start, end, equilibriumEast, equilibriumNorth,
                relaxationSeconds, out unused);
        }

        public bool AcknowledgeEvent(long epoch)
        {
            if (PendingEventEpoch < 0 || epoch != PendingEventEpoch)
                return false;
            PendingEventEpoch = -1;
            return true;
        }

        public CurrentTrackPayload ToPayload()
        {
            return new CurrentTrackPayload
            {
                Version = PayloadVersion,
                Epoch = Epoch,
                Owner = (int)Owner,
                PendingEventEpoch = PendingEventEpoch,
                EastMeters = EastMeters,
                NorthMeters = NorthMeters,
                VelocityEastMetersPerSecond = VelocityEastMetersPerSecond,
                VelocityNorthMetersPerSecond = VelocityNorthMetersPerSecond
            };
        }

        public static bool TryRestore(CurrentTrackPayload payload, out CurrentTrack2D track)
        {
            track = null;
            if (payload == null || payload.Version != PayloadVersion || payload.Epoch < 0
                || (payload.Owner != (int)CurrentOwner.Physical
                    && payload.Owner != (int)CurrentOwner.Strategic)
                || (payload.PendingEventEpoch != -1 && payload.PendingEventEpoch != payload.Epoch)
                || !IsFinite(payload.EastMeters) || !IsFinite(payload.NorthMeters)
                || !IsFinite(payload.VelocityEastMetersPerSecond)
                || !IsFinite(payload.VelocityNorthMetersPerSecond))
                return false;
            track = new CurrentTrack2D(payload.Epoch, payload.EastMeters, payload.NorthMeters,
                payload.VelocityEastMetersPerSecond, payload.VelocityNorthMetersPerSecond);
            track.Owner = (CurrentOwner)payload.Owner;
            track.PendingEventEpoch = payload.PendingEventEpoch;
            return true;
        }

        private bool AdvanceStrategic(long start, long end, double equilibriumEast,
            double equilibriumNorth, double relaxationSeconds, out long stoppedAtEpoch)
        {
            stoppedAtEpoch = -1;
            if (Owner != CurrentOwner.Strategic || PendingEventEpoch >= 0
                || !OwnsInterval(start, end)
                || !IsFinite(equilibriumEast) || !IsFinite(equilibriumNorth)
                || !IsFinite(relaxationSeconds) || relaxationSeconds <= 0)
                return false;

            double elapsed = (end - start) / (double)TimeSpan.TicksPerSecond;
            double fraction = elapsed / relaxationSeconds;
            double lost = fraction < 1e-4 ? fraction * (1.0 - 0.5 * fraction) : 1.0 - Math.Exp(-fraction);
            double differenceEast = VelocityEastMetersPerSecond - equilibriumEast;
            double differenceNorth = VelocityNorthMetersPerSecond - equilibriumNorth;
            double response = fraction < 1e-4 ? elapsed * fraction * (0.5 - fraction / 6.0)
                : elapsed - relaxationSeconds * lost;
            double east = EastMeters + VelocityEastMetersPerSecond * elapsed - differenceEast * response;
            double north = NorthMeters + VelocityNorthMetersPerSecond * elapsed - differenceNorth * response;
            double velocityEast = VelocityEastMetersPerSecond - differenceEast * lost;
            double velocityNorth = VelocityNorthMetersPerSecond - differenceNorth * lost;
            if (!IsFinite(east) || !IsFinite(north) || !IsFinite(velocityEast) || !IsFinite(velocityNorth))
                return false;

            Epoch = end;
            EastMeters = east;
            NorthMeters = north;
            VelocityEastMetersPerSecond = velocityEast;
            VelocityNorthMetersPerSecond = velocityNorth;
            return true;
        }

        private bool OwnsInterval(long start, long end)
        {
            return start == Epoch && end > start;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
