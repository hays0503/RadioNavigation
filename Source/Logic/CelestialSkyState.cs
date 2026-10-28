using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    internal enum CelestialSkyNotReady
    {
        None = 0, NoWorld = 1, Loading = 2, MissingInput = 3, WrongWorld = 4, GameTime = 5, WorldUtc = 6,
        Observer = 7, HeightDatum = 8, Atmosphere = 9, SourceUnavailable = 10, Computation = 11
    }

    /// <summary>Explicit trusted inputs; no clock, prefs, camera or worksheet fallback.</summary>
    internal sealed class CelestialSkyInput
    {
        internal readonly long WorldRevision;
        internal readonly DateTime GmtUt1;
        internal readonly double LatitudeDeg, LongitudeDeg, EllipsoidHeightM, EyeAboveSeaM;
        internal readonly double PressureHpa, TemperatureC, WorldUtcOffset;
        internal readonly bool TimeTrusted, ObserverTrusted, HeightTrusted, AtmosphereAccepted, WorldUtcKnown;

        internal CelestialSkyInput(long revision, DateTime gmt, double lat, double lon,
            double ellipsoidHeight, double eyeHeight, double pressure, double temperature,
            double worldUtc, bool timeTrusted, bool observerTrusted, bool heightTrusted,
            bool atmosphereAccepted, bool worldUtcKnown)
        {
            WorldRevision = revision; GmtUt1 = gmt; LatitudeDeg = lat; LongitudeDeg = lon;
            EllipsoidHeightM = ellipsoidHeight; EyeAboveSeaM = eyeHeight;
            PressureHpa = pressure; TemperatureC = temperature; WorldUtcOffset = worldUtc;
            TimeTrusted = timeTrusted; ObserverTrusted = observerTrusted; HeightTrusted = heightTrusted;
            AtmosphereAccepted = atmosphereAccepted; WorldUtcKnown = worldUtcKnown;
        }

        internal bool SameKey(CelestialSkyInput other)
        {
            return other != null && SameEpoch(other) && SameObserver(other) && SameEnvironment(other);
        }

        private bool SameEpoch(CelestialSkyInput other)
        {
            return WorldRevision == other.WorldRevision && GmtUt1 == other.GmtUt1
                && GmtUt1.Kind == other.GmtUt1.Kind && TimeTrusted == other.TimeTrusted
                && WorldUtcOffset == other.WorldUtcOffset && WorldUtcKnown == other.WorldUtcKnown;
        }

        private bool SameObserver(CelestialSkyInput other)
        {
            return LatitudeDeg == other.LatitudeDeg && LongitudeDeg == other.LongitudeDeg
                && EllipsoidHeightM == other.EllipsoidHeightM && EyeAboveSeaM == other.EyeAboveSeaM
                && ObserverTrusted == other.ObserverTrusted && HeightTrusted == other.HeightTrusted;
        }

        private bool SameEnvironment(CelestialSkyInput other)
        {
            return PressureHpa == other.PressureHpa && TemperatureC == other.TemperatureC
                && AtmosphereAccepted == other.AtmosphereAccepted;
        }
    }

    internal struct CelestialSkyVector
    {
        internal readonly double X, Y, Z;
        internal CelestialSkyVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        internal CelestialSkyVector Unit { get { return this / Length; } }
        public static CelestialSkyVector operator -(CelestialSkyVector a, CelestialSkyVector b)
        { return new CelestialSkyVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static CelestialSkyVector operator *(CelestialSkyVector a, double k)
        { return new CelestialSkyVector(a.X * k, a.Y * k, a.Z * k); }
        public static CelestialSkyVector operator /(CelestialSkyVector a, double k) { return a * (1 / k); }
        internal static double AngleArcMin(CelestialSkyVector a, CelestialSkyVector b)
        {
            var cross = new CelestialSkyVector(a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            return Math.Atan2(cross.Length, a.X * b.X + a.Y * b.Y + a.Z * b.Z) / CelestialSkyMath.R * 60;
        }
    }

    internal sealed class CelestialSkyBody
    {
        internal readonly int Hip;
        internal readonly CelestialSkyVector VacuumEnu;
        internal readonly double GeocentricRaRad, GeocentricDecRad, DistanceKm, DiameterDeg;
        internal readonly double VacuumAltitudeDeg, AzimuthDeg;
        internal readonly double RefractedCentreDeg, RefractedLowerDeg, RefractedUpperDeg;
        internal bool OpticalProjectionReady
        {
            get { return CelestialSkyMath.Finite(RefractedLowerDeg) && CelestialSkyMath.Finite(RefractedUpperDeg); }
        }

        internal CelestialSkyBody(int hip, CelestialSkyVector enu, double ra, double dec,
            double distance, double diameter, CelestialSkyInput input)
        {
            Hip = hip; VacuumEnu = enu.Unit; GeocentricRaRad = ra; GeocentricDecRad = dec;
            DistanceKm = distance; DiameterDeg = diameter;
            VacuumAltitudeDeg = Math.Asin(Math.Max(-1, Math.Min(1, VacuumEnu.Z))) / CelestialSkyMath.R;
            AzimuthDeg = CelestialSkyMath.Wrap(Math.Atan2(VacuumEnu.X, VacuumEnu.Y) / CelestialSkyMath.R);
            RefractedCentreDeg = CelestialSkyMath.Refract(VacuumAltitudeDeg, input.PressureHpa, input.TemperatureC);
            RefractedLowerDeg = CelestialSkyMath.Refract(VacuumAltitudeDeg - diameter / 2, input.PressureHpa, input.TemperatureC);
            RefractedUpperDeg = CelestialSkyMath.Refract(VacuumAltitudeDeg + diameter / 2, input.PressureHpa, input.TemperatureC);
        }
    }

    /// <summary>Runtime-only immutable computation result. No serializer or render binding.</summary>
    internal sealed class CelestialSkyState
    {
        internal const int SchemaVersion = 1;
        internal const string ModelVersion = "Meeus47Full-UT1TT-WGS84-ENU-v1";
        internal readonly long SnapshotVersion;
        internal readonly CelestialSkyInput Input;
        internal readonly double JdUt1, JdTt, DeltaTSeconds, GmstDeg, GastDeg;
        internal readonly CelestialSkyBody Sun, Moon;
        internal readonly IReadOnlyList<CelestialSkyBody> NavigationStars;

        internal CelestialSkyState(long version, CelestialSkyInput input, double ut, double tt,
            double dt, double gmst, double gast, CelestialSkyBody sun, CelestialSkyBody moon,
            List<CelestialSkyBody> stars)
        {
            SnapshotVersion = version; Input = input; JdUt1 = ut; JdTt = tt;
            DeltaTSeconds = dt; GmstDeg = gmst; GastDeg = gast; Sun = sun; Moon = moon;
            NavigationStars = Array.AsReadOnly(stars.ToArray());
        }
    }
}
