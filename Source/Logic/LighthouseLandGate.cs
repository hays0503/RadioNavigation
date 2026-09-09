using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Land/water curation decision for the lighthouse fleet (pure C#,
    /// offline-tested). CoastQuadTree.GetDistanceToCoast returns a SIGNED
    /// distance in km: negative = the point lies inside the game's coast
    /// polygons (land), positive = outside them (water). Beacons whose
    /// charted position falls on the water side beyond ShoreBandKm are
    /// excluded from the placed fleet by data curation (moved out of
    /// Leuchtfeuer.json), so this gate ships as library code for the
    /// rn_lighthouse_landprobe sweep and the offline sign tests — it does
    /// NOT filter the runtime fleet.
    ///
    /// The ShoreBandKm buffer (user decision 2026-09-09) keeps beacons
    /// within ±300 m of the coast edge: the vector coast is coarser than
    /// the terrain, and beach/mole lights exactly on the edge would
    /// otherwise flip verdicts over millimeters of projection difference.
    /// </summary>
    public static class LighthouseLandGate
    {
        /// <summary>
        /// Water-side exclusion threshold in km: only distances strictly
        /// greater than this are classified as water. Symmetric band
        /// [-ShoreBandKm, +ShoreBandKm] is "borderline" (kept, but flagged
        /// in the sweep CSV for manual review).
        /// </summary>
        public const double ShoreBandKm = 0.3;

        /// <summary>Meters per degree along the equator used by the game.</summary>
        public const double EquatorLengthM = 40075160.0;

        /// <summary>
        /// True when the signed coast distance is clearly on the water side.
        /// NaN/infinity (probe unavailable) conservatively classifies as
        /// NOT water — unknown verdicts never exclude a beacon.
        /// </summary>
        public static bool IsWater(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm))
            {
                return false;
            }

            return distanceKm > ShoreBandKm;
        }

        /// <summary>
        /// Human-readable verdict for the sweep CSV and logs:
        /// "unknown" (no data), "land" (well inside the coast),
        /// "borderline" (within the shore band, kept), "water" (excluded).
        /// </summary>
        public static string VerdictLabel(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm))
            {
                return "unknown";
            }

            if (distanceKm > ShoreBandKm)
            {
                return "water";
            }

            if (distanceKm >= -ShoreBandKm)
            {
                return "borderline";
            }

            return "land";
        }

        /// <summary>
        /// Latitude degrees to encounter-space km (north). Mirrors the
        /// game's Coordinates.ToWorldPosition equirectangular projection
        /// (Coordinates.cs:47: km = degrees * 40075.16 / 360) in double
        /// precision so the offline coast port and the runtime probe agree.
        /// </summary>
        public static double LatitudeToKm(double latitude)
        {
            return latitude * EquatorLengthM / 1000.0 / 360.0;
        }

        /// <summary>Longitude degrees to encounter-space km (east).</summary>
        public static double LongitudeToKm(double longitude)
        {
            return longitude * EquatorLengthM / 1000.0 / 360.0;
        }
    }
}
