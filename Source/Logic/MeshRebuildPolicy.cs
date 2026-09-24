using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Shared rebuild thresholds for the procedural sky meshes (SRP/dedup:
    /// StarsMesh, PlanetRenderer and ConstellationMesh carried the same
    /// distance/LST/latitude comparisons). Rotation check is opt-in:
    /// constellation lines skip it, like before. Pure mesh math.
    /// </summary>
    public static class MeshRebuildPolicy
    {
        public const float MaxCameraMoveM = 10f;
        public const float MaxCameraRotationDeg = 5f;
        public const double MaxLstDriftDeg = RebuildDrift.MaxLstDriftDeg;
        public const float MaxLatitudeDriftDeg = RebuildDrift.MaxLatitudeDriftDeg;

        /// <summary>Minimum realtime interval between full mesh rebuilds (P7):
        /// at ×32 the 0.1° LST threshold alone fires ~1.3×/s per mesh.</summary>
        public const float MinRebuildIntervalSeconds = RebuildDrift.MinRebuildIntervalSeconds;

        /// <summary>True when enough realtime passed since the last rebuild
        /// (the throttling half of the policy; trip conditions are separate).</summary>
        public static bool RebuildIntervalElapsed(float lastRebuildRealtime, float nowRealtime)
        {
            return RebuildDrift.RebuildIntervalElapsed(lastRebuildRealtime, nowRealtime);
        }

        /// <summary>Signed angular difference wrapped into [−180, 180]: the
        /// 359.9° → 0.1° wrap must not read as a 359.8° jump (P7).</summary>
        public static double AngularDifferenceDeg(double a, double b)
        {
            return RebuildDrift.AngularDifferenceDeg(a, b);
        }

        public static bool NeedsRebuild(Vector3 camPos, Vector3 lastCamPos,
            Quaternion camRot, Quaternion lastCamRot,
            double lstDeg, double lastLstDeg, float lat, float lastLat,
            bool checkRotation)
        {
            if (Vector3.Distance(camPos, lastCamPos) > MaxCameraMoveM)
                return true;
            if (checkRotation && Quaternion.Angle(camRot, lastCamRot) > MaxCameraRotationDeg)
                return true;
            return LstLatDrifted(lstDeg, lastLstDeg, lat, lastLat);
        }

        /// <summary>Position/LST/latitude drift without the rotation check
        /// (constellation lines do not track camera roll).</summary>
        public static bool NeedsRebuild(Vector3 camPos, Vector3 lastCamPos,
            double lstDeg, double lastLstDeg, float lat, float lastLat)
        {
            if (Vector3.Distance(camPos, lastCamPos) > MaxCameraMoveM)
                return true;
            return LstLatDrifted(lstDeg, lastLstDeg, lat, lastLat);
        }

        private static bool LstLatDrifted(double lstDeg, double lastLstDeg, float lat, float lastLat)
        {
            return RebuildDrift.LstLatDrifted(lstDeg, lastLstDeg, lat, lastLat);
        }
    }
}
