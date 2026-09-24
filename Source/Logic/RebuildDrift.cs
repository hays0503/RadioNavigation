namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Unity-free rebuild-drift math (CELESTIAL_REALISM_PLAN P7): the wrapped
    /// angular difference and the realtime throttle. Kept out of
    /// MeshRebuildPolicy so the math is unit-tested without Unity stubs.
    /// </summary>
    public static class RebuildDrift
    {
        /// <summary>LST drift that forces a mesh rebuild (degrees).</summary>
        public const double MaxLstDriftDeg = 0.1;

        /// <summary>Latitude drift that forces a mesh rebuild (degrees).</summary>
        public const float MaxLatitudeDriftDeg = 0.01f;

        /// <summary>Minimum realtime interval between full mesh rebuilds: at
        /// ×32 the 0.1° LST threshold alone fires ~1.3×/s per mesh.</summary>
        public const float MinRebuildIntervalSeconds = 0.5f;

        /// <summary>Signed angular difference wrapped into [−180, 180]: the
        /// 359.9° → 0.1° wrap must not read as a 359.8° jump.</summary>
        public static double AngularDifferenceDeg(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180.0)
                d -= 360.0;
            if (d < -180.0)
                d += 360.0;
            return d;
        }

        /// <summary>True when enough realtime passed since the last rebuild.</summary>
        public static bool RebuildIntervalElapsed(float lastRebuildRealtime, float nowRealtime)
        {
            return nowRealtime - lastRebuildRealtime >= MinRebuildIntervalSeconds;
        }

        /// <summary>LST/latitude drift with the wrap handled.</summary>
        public static bool LstLatDrifted(double lstDeg, double lastLstDeg, float lat, float lastLat)
        {
            if (System.Math.Abs(AngularDifferenceDeg(lstDeg, lastLstDeg)) > MaxLstDriftDeg)
                return true;
            return System.Math.Abs(lat - lastLat) > MaxLatitudeDriftDeg;
        }
    }
}
