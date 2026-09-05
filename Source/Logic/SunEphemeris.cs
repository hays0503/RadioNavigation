using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Own Meeus sun ephemeris behind SextantController (P5.4 slice 4c,
    /// SRP): sun RA/Dec through the same ENU-to-world mapping the star sky
    /// uses, so axis conventions are already game-proven. Position and time
    /// are explicit inputs; the controller adapts them from the position
    /// provider and the game clock. Runs offline in SunEphemerisTests.
    /// Pure C#.
    /// </summary>
    public static class SunEphemeris
    {
        public struct SunFix
        {
            public readonly bool Ok;
            public readonly double DirX;
            public readonly double DirY;
            public readonly double DirZ;
            public readonly float AltDeg;
            public readonly float AzDeg;

            public SunFix(bool ok, double dirX, double dirY, double dirZ, float altDeg, float azDeg)
            {
                Ok = ok;
                DirX = dirX;
                DirY = dirY;
                DirZ = dirZ;
                AltDeg = altDeg;
                AzDeg = azDeg;
            }
        }

        public static SunFix Compute(double latDeg, double lonDeg, DateTime utc)
        {
            double jd = SunEphemerisCalibration.CalibratedJd(utc);
            double lstDeg = AstroMath.LstDeg(jd, lonDeg);

            double raRad, decRad;
            AstroMath.SunRaDec(jd, out raRad, out decRad);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg, out altRad, out azRad, out e, out n, out u);

            double len = Math.Sqrt(e * e + n * n + u * u);
            if (len < 1e-9)
                return new SunFix(false, 0.0, 0.0, 0.0, 0f, 0f);

            return new SunFix(true, e / len, u / len, n / len,
                (float)(altRad * Mathf.Rad2Deg), (float)(azRad * Mathf.Rad2Deg));
        }
    }
}
