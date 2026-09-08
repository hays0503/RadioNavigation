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
        public const double MaxLstDriftDeg = 0.1;
        public const float MaxLatitudeDriftDeg = 0.01f;

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
            if (System.Math.Abs(lstDeg - lastLstDeg) > MaxLstDriftDeg)
                return true;
            return System.Math.Abs(lat - lastLat) > MaxLatitudeDriftDeg;
        }
    }
}
