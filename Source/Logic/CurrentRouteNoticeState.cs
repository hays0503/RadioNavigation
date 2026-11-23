using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal enum RouteNotice
    {
        None = 0,
        Unreachable = 1,
        Restored = 2
    }

    /// <summary>
    /// Transition-only notice state for the route correction. A notice is
    /// raised when the corrected leg becomes unreachable, once when it
    /// recovers, and once when a new route starts on a corrected course (that
    /// one carries the ETA). Repeated solves in the same state stay silent,
    /// and two notices are never closer than the minimum interval. Pure logic
    /// so the transitions are unit-tested without a scene.
    /// </summary>
    internal sealed class CurrentRouteNoticeState
    {
        internal const long MinIntervalTicks = 60 * TimeSpan.TicksPerSecond;

        private bool unreachable;
        private bool awaitingFirstCorrection;
        private long lastTick = long.MinValue / 2;

        internal RouteNotice Observe(bool nowUnreachable, long tick)
        {
            if (tick < lastTick)
            {
                // The campaign clock went backwards (a save older than the
                // session was loaded): forget the state silently, otherwise
                // the interval gate would suppress notices for a long time.
                lastTick = long.MinValue / 2;
                unreachable = false;
                awaitingFirstCorrection = true;
                return RouteNotice.None;
            }
            if (nowUnreachable)
            {
                awaitingFirstCorrection = false;
                if (unreachable || tick - lastTick < MinIntervalTicks)
                    return RouteNotice.None;
                unreachable = true;
                lastTick = tick;
                return RouteNotice.Unreachable;
            }
            if (unreachable)
            {
                if (tick - lastTick < MinIntervalTicks)
                    return RouteNotice.None;
                unreachable = false;
                lastTick = tick;
                return RouteNotice.Restored;
            }
            if (!awaitingFirstCorrection || tick - lastTick < MinIntervalTicks)
                return RouteNotice.None;
            awaitingFirstCorrection = false;
            lastTick = tick;
            return RouteNotice.Restored;
        }

        /// <summary>Forget the state without raising a notice (route ended).</summary>
        internal void Reset()
        {
            unreachable = false;
            awaitingFirstCorrection = true;
            lastTick = long.MinValue / 2;
        }
    }
}
