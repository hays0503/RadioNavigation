using System;
using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Read-only diagnostics, not a new sight-reduction model. Uses
    /// the actual mod astronomy with an EXPLICIT time shift; never changes
    /// SUNDRIFT, the instrument, a worksheet or a frozen observation.</summary>
    internal static class SolarSightAudit
    {
        internal struct Sample
        {
            internal double JdUt;
            internal double JdTt;
            internal double GhaDeg;
            internal double DecDeg;
            internal double AltDeg;
            internal double AzDeg;
            internal double DiameterDeg;
        }

        internal static Sample Compute(DateTime utc, double latDeg, double lonDeg, double deltaMinutes)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Audit time must be explicit GMT/UTC", nameof(utc));
            if (!Finite(latDeg) || Math.Abs(latDeg) > 90.0)
                throw new ArgumentOutOfRangeException(nameof(latDeg));
            if (!Finite(lonDeg) || Math.Abs(lonDeg) > 180.0)
                throw new ArgumentOutOfRangeException(nameof(lonDeg));
            if (!Finite(deltaMinutes) || Math.Abs(deltaMinutes) > 1440.0)
                throw new ArgumentOutOfRangeException(nameof(deltaMinutes));

            double jdUt = AstroMath.ToJulianDay(utc) + deltaMinutes / 1440.0;
            double jdTt = jdUt + SunEphemerisCalibration.DeltaTSeconds / 86400.0;
            double ra, dec;
            AstroMath.SunRaDec(jdTt, out ra, out dec);
            double alt, az, e, n, u;
            AstroMath.RaDecToEnu(ra, dec, AstroMath.LstDeg(jdUt, lonDeg), latDeg,
                out alt, out az, out e, out n, out u);
            double gha = (AstroMath.GmstDeg(jdUt) - ra * 180.0 / Math.PI + 360.0) % 360.0;
            return new Sample
            {
                JdUt = jdUt, JdTt = jdTt, GhaDeg = gha,
                DecDeg = dec * 180.0 / Math.PI, AltDeg = alt * 180.0 / Math.PI,
                AzDeg = az * 180.0 / Math.PI, DiameterDeg = SunDiscScale.AngularDiameterDeg(jdTt)
            };
        }

        internal static void AppendModel(StringBuilder sb, Sample sample)
        {
            sb.Append("{\"jdUt\":").Append(Number(sample.JdUt));
            sb.Append(",\"jdTt\":").Append(Number(sample.JdTt));
            sb.Append(",\"ghaDeg\":").Append(Number(sample.GhaDeg));
            sb.Append(",\"decDeg\":").Append(Number(sample.DecDeg));
            sb.Append(",\"altDeg\":").Append(Number(sample.AltDeg));
            sb.Append(",\"azDeg\":").Append(Number(sample.AzDeg));
            sb.Append(",\"diameterDeg\":").Append(Number(sample.DiameterDeg)).Append('}');
        }

        /// <summary>World axes e=x, u=y, n=z. Null is unavailable, not a zero
        /// altitude. Direction is configuration/readback, NOT a visible pixel.</summary>
        internal static void AppendDirection(StringBuilder sb, double x, double y, double z)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (!Finite(length) || length < 1e-9)
            {
                sb.Append("null");
                return;
            }
            double alt = Math.Asin(Math.Max(-1.0, Math.Min(1.0, y / length))) * 180.0 / Math.PI;
            double az = (Math.Atan2(x, z) * 180.0 / Math.PI + 360.0) % 360.0;
            sb.Append("{\"x\":").Append(Number(x / length));
            sb.Append(",\"y\":").Append(Number(y / length));
            sb.Append(",\"z\":").Append(Number(z / length));
            sb.Append(",\"altDeg\":").Append(Number(alt));
            sb.Append(",\"azDeg\":").Append(Number(az)).Append('}');
        }

        internal static string Number(double value)
        {
            return Finite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null";
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
