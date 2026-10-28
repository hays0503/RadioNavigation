using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Table centre geometry is deliberately independent of native
    /// TOD and SUNDRIFT. Rendering and reduction share these explicit inputs.</summary>
    public static class Sh5SolarNavigation
    {
        public static bool TryCentre(DateTime utc, double latDeg, double lonDeg,
            out double altitudeDeg, out double azimuthDeg)
        {
            altitudeDeg = 0;
            azimuthDeg = 0;
            double gha, dec;
            if (!Sh5SunAlmanac.TryPlace(utc, out gha, out dec)
                || !GeoValid(latDeg, lonDeg))
                return false;
            Place(gha, dec, latDeg, lonDeg, out altitudeDeg, out azimuthDeg);
            return true;
        }

        public static void Place(double ghaDeg, double decDeg, double latDeg, double lonDeg,
            out double altitudeDeg, out double azimuthDeg)
        {
            double phi = latDeg * Math.PI / 180;
            double delta = decDeg * Math.PI / 180;
            double lha = (ghaDeg + lonDeg) * Math.PI / 180;
            double e = -Math.Cos(delta) * Math.Sin(lha);
            double n = Math.Sin(delta) * Math.Cos(phi) - Math.Cos(delta) * Math.Cos(lha) * Math.Sin(phi);
            double u = Math.Sin(delta) * Math.Sin(phi) + Math.Cos(delta) * Math.Cos(lha) * Math.Cos(phi);
            altitudeDeg = Math.Asin(Math.Max(-1, Math.Min(1, u))) * 180 / Math.PI;
            azimuthDeg = RunningFixCalculator.Normalize360(Math.Atan2(e, n) * 180 / Math.PI);
        }

        public static bool TryApparentCentre(DateTime utc, double latDeg, double lonDeg,
            double eyeHeightM, double horizonAltitudeDeg, double diameterDeg,
            out double apparentAltDeg, out double azimuthDeg)
        {
            apparentAltDeg = 0;
            azimuthDeg = 0;
            double centre, lower;
            if (!TryCentre(utc, latDeg, lonDeg, out centre, out azimuthDeg)
                || !Finite(horizonAltitudeDeg) || horizonAltitudeDeg > 0 || horizonAltitudeDeg < -1
                || !Finite(diameterDeg) || diameterDeg < 0.5 || diameterDeg > 0.56
                || !SolarHeightReduction.TryLowerLimb(centre, eyeHeightM, utc.Month, out lower))
                return false;
            apparentAltDeg = horizonAltitudeDeg + lower + diameterDeg / 2;
            return apparentAltDeg <= 90;
        }

        public static bool TryObservation(DateTime utc, double rawHsDeg, double indexArcMin,
            double latDeg, double lonDeg, SunDiscSighting disc, out SextantObservation observation)
        {
            observation = null;
            double gha, dec, ho, correction;
            if (!SightingValid(disc, utc) || !GeoValid(latDeg, lonDeg)
                || !Sh5SunAlmanac.TryPlace(utc, out gha, out dec)
                || !SolarHeightReduction.TryReduce(rawHsDeg, indexArcMin, disc.EyeHeightM,
                    utc.Month, disc.Limb, out ho, out correction))
                return false;
            double hc, az;
            Place(gha, dec, latDeg, lonDeg, out hc, out az);
            double gamma = AstroMath.GmstDeg(AstroMath.ToJulianDay(utc));
            double lha = RunningFixCalculator.Normalize360(gha + lonDeg);
            observation = new SextantObservation
            {
                Utc = utc, HoDeg = ho, DrLatDeg = latDeg, DrLonDeg = lonDeg,
                BodyKind = CelestialBodyKind.Sun, BodyName = CelestialBody.Sun().Name,
                BodyRaDeg = RunningFixCalculator.Normalize360(gamma - gha), BodyDecDeg = dec,
                GhaAriesDeg = gamma, ShaBodyDeg = RunningFixCalculator.Normalize360(gha - gamma),
                GhaBodyDeg = gha, DecDeg = dec, LhaDeg = lha, HcDeg = hc, AzDeg = az,
                InterceptNm = (ho - hc) * 60, Quadrant = (int)(lha / 90) + 1,
                QuadrantWest = lha < 180, SolarReductionVersion = 1,
                RawHsDeg = rawHsDeg, IndexCorrectedHDeg = rawHsDeg + indexArcMin / 60,
                SolarEyeHeightM = disc.EyeHeightM, SolarFviiiArcMin = correction,
                SolarHorizonAltitudeDeg = disc.HorizonAltitudeDeg,
                IndexErrorArcMin = indexArcMin, HoSampleCount = 1,
                ObservedSunAngularDiameterDeg = disc.ObservedDiameterDeg, SunLimb = disc.Limb
            };
            return true;
        }

        private static bool SightingValid(SunDiscSighting disc, DateTime utc)
        {
            return disc.TableRequested && disc.TableCompatible && disc.ConfiguredUtc == utc
                && Finite(disc.ObservedDiameterDeg) && disc.ObservedDiameterDeg >= 0.5
                && disc.ObservedDiameterDeg <= 0.56 && Finite(disc.HorizonAltitudeDeg)
                && disc.HorizonAltitudeDeg >= -1 && disc.HorizonAltitudeDeg <= 0;
        }

        private static bool GeoValid(double lat, double lon)
        {
            return Finite(lat) && Finite(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
