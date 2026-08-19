using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Renders the star sky for astronomical navigation (STAR_SKY_PLAN).
    /// Phase 1: visibility logic only, no mesh yet.
    /// Best practice: pure math via AstroMath, no allocation in Update.
    /// </summary>
    public sealed class StarSkyRenderer : MonoBehaviour
    {
        private IPlayerPositionProvider positionProvider;
        private DateTime currentTimeUtc;
        private double lstDeg;
        private int visibleStarCount;
        private float lastLogTime;

        private void Awake()
        {
            positionProvider = new GamePlayerPositionProvider();
            Debug.Log("[RadioNav][StarSky] Renderer initialized with " + StarCatalog.Stars.Length + " stars");
        }

        private void LateUpdate()
        {
            // Get current time from game (placeholder: use system time for now)
            // TODO: integrate with UBOAT GameTime when available
            currentTimeUtc = DateTime.UtcNow;
            double jd = AstroMath.ToJulianDay(currentTimeUtc);

            // Get observer position
            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
            {
                visibleStarCount = 0;
                return;
            }

            // Compute Local Sidereal Time
            lstDeg = AstroMath.LstDeg(jd, lon);

            // Count visible stars (alt > 0)
            visibleStarCount = 0;
            foreach (var star in StarCatalog.Stars)
            {
                double raRad = star.RaDeg * Mathf.Deg2Rad;
                double decRad = star.DecDeg * Mathf.Deg2Rad;

                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                    out double altRad, out _, out _, out _, out _);

                if (altRad > 0)
                    visibleStarCount++;
            }

            // Throttled logging for diagnostics
            if (Time.time - lastLogTime > 5f)
            {
                Debug.Log($"[RadioNav][StarSky] JD={jd:F2} LST={lstDeg:F1}° lat={lat:F2} lon={lon:F2} visible={visibleStarCount}/{StarCatalog.Stars.Length}");
                lastLogTime = Time.time;
            }
        }

        /// <summary>
        /// Returns altitude/azimuth for a given HIP star (for sextant integration).
        /// Returns false if star not found or below horizon.
        /// </summary>
        public bool TryGetStarAltAz(int hip, out float altDeg, out float azDeg)
        {
            altDeg = 0;
            azDeg = 0;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return false;

            foreach (var star in StarCatalog.Stars)
            {
                if (star.Hip != hip) continue;

                double raRad = star.RaDeg * Mathf.Deg2Rad;
                double decRad = star.DecDeg * Mathf.Deg2Rad;

                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                    out double altRad, out double azRad, out _, out _, out _);

                if (altRad <= 0) return false;

                altDeg = (float)(altRad * Mathf.Rad2Deg);
                azDeg = (float)(azRad * Mathf.Rad2Deg);
                return true;
            }
            return false;
        }
    }
}
