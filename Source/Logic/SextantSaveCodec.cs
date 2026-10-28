using System;
using System.IO;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Versioned, bounded primitive payload in the game's .save.
    /// No JSON library, PlayerPrefs, sidecar files or serialized UI graph.
    /// Decode is transactional: invalid/future data never partially restores.</summary>
    public static class SextantSaveCodec
    {
        private const int Magic = 0x524E4153;
        /// <summary>
        /// v3 adds frozen SH5 lower-limb reduction provenance. v2 adds the drawn-solar-disc snapshot to each sight
        /// (observed diameter, limb, applied compensation). v1 is decoded in
        /// its own layout with unknown disc provenance (zero), without changing
        /// any frozen Ho/Hc/intercept, card, calibration or marker data.
        /// </summary>
        public const int Version = 3;
        public const int MaxBytes = 32768;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static string Encode(SextantSaveSnapshot state)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic);
                writer.Write(Version);
                WriteObservation(writer, state.Observation1);
                WriteObservation(writer, state.Observation2);
                WriteObservation(writer, state.Observation3);
                WriteText(writer, state.SectionText, 8192);
                writer.Write(state.CalibrationMinutes);
                WriteFix(writer, state);
                WriteCourse(writer, state.Course);
                WriteMarkers(writer, state.Markers);
                writer.Flush();
                string payload = Convert.ToBase64String(stream.ToArray());
                SextantSaveSnapshot checkedState;
                string error;
                if (!TryDecode(payload, out checkedState, out error))
                    throw new InvalidDataException("Invalid astro snapshot: " + error);
                return payload;
            }
        }

        public static bool TryDecode(string payload, out SextantSaveSnapshot state, out string error)
        {
            state = null;
            error = null;
            try
            {
                Require(!string.IsNullOrEmpty(payload), "empty payload");
                Require(payload.Length <= ((MaxBytes + 2) / 3) * 4, "payload too large");
                byte[] bytes = Convert.FromBase64String(payload);
                Require(bytes.Length <= MaxBytes, "payload too large");
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    Require(reader.ReadInt32() == Magic, "wrong magic");
                    int version = reader.ReadInt32();
                    Require(version >= 1 && version <= Version, "unsupported version");
                    SextantSaveSnapshot decoded = ReadSnapshot(reader, version);
                    Require(stream.Position == stream.Length, "trailing data");
                    ValidateSnapshot(decoded);
                    state = decoded;
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static SextantSaveSnapshot ReadSnapshot(BinaryReader reader, int version)
        {
            var state = new SextantSaveSnapshot
            {
                Observation1 = ReadObservation(reader, version),
                Observation2 = ReadObservation(reader, version),
                Observation3 = ReadObservation(reader, version),
                SectionText = ReadText(reader, 8192),
                CalibrationMinutes = ReadFinite(reader),
                FixPublished = reader.ReadBoolean(),
                FixLatDeg = ReadFinite(reader),
                FixLonDeg = ReadFinite(reader),
                FixErrorNm = reader.ReadDouble(),
                FixCrossingDeg = ReadFinite(reader),
                FixSameBody = reader.ReadBoolean(),
                Course = ReadCourse(reader),
                Markers = ReadMarkers(reader)
            };
            return state;
        }

        private static void WriteFix(BinaryWriter writer, SextantSaveSnapshot state)
        {
            writer.Write(state.FixPublished);
            writer.Write(state.FixLatDeg);
            writer.Write(state.FixLonDeg);
            writer.Write(state.FixErrorNm);
            writer.Write(state.FixCrossingDeg);
            writer.Write(state.FixSameBody);
        }

        private static void WriteObservation(BinaryWriter w, SextantObservation o)
        {
            w.Write(o != null);
            if (o == null)
                return;
            w.Write(o.Utc.Ticks);
            w.Write(o.HoDeg);
            w.Write(o.DrLatDeg); w.Write(o.DrLonDeg);
            w.Write((int)o.BodyKind);
            WriteText(w, o.BodyName, 256);
            w.Write(o.BodyHip);
            w.Write(o.BodyRaDeg); w.Write(o.BodyDecDeg);
            w.Write(o.GhaAriesDeg); w.Write(o.ShaBodyDeg); w.Write(o.GhaBodyDeg);
            w.Write(o.LhaDeg); w.Write(o.DecDeg);
            w.Write(o.HcDeg); w.Write(o.AzDeg); w.Write(o.InterceptNm);
            w.Write(o.Quadrant); w.Write(o.QuadrantWest);
            w.Write(o.HoSigmaArcMin); w.Write(o.HoSampleCount); w.Write(o.HoRejectedCount);
            w.Write(o.IndexErrorArcMin);
            // v2: the drawn solar disc the sight was taken on, the limb, and the
            // compensation folded into HoDeg. Persisted so a reloaded record
            // still explains itself instead of depending on live renderer state.
            w.Write(o.ObservedSunAngularDiameterDeg);
            w.Write((int)o.SunLimb);
            w.Write(o.SunDiscCompensationArcMin);
            w.Write(o.SolarReductionVersion);
            w.Write(o.RawHsDeg); w.Write(o.IndexCorrectedHDeg);
            w.Write(o.SolarEyeHeightM); w.Write(o.SolarFviiiArcMin);
            w.Write(o.SolarHorizonAltitudeDeg);
        }

        private static SextantObservation ReadObservation(BinaryReader r, int version)
        {
            if (!r.ReadBoolean())
                return null;
            var o = new SextantObservation
            {
                Utc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
                HoDeg = ReadFinite(r),
                DrLatDeg = ReadFinite(r), DrLonDeg = ReadFinite(r),
                BodyKind = (CelestialBodyKind)r.ReadInt32(),
                BodyName = ReadText(r, 256), BodyHip = r.ReadInt32(),
                BodyRaDeg = ReadFinite(r), BodyDecDeg = ReadFinite(r),
                GhaAriesDeg = ReadFinite(r), ShaBodyDeg = ReadFinite(r), GhaBodyDeg = ReadFinite(r),
                LhaDeg = ReadFinite(r), DecDeg = ReadFinite(r),
                HcDeg = ReadFinite(r), AzDeg = ReadFinite(r), InterceptNm = ReadFinite(r),
                Quadrant = r.ReadInt32(), QuadrantWest = r.ReadBoolean(),
                HoSigmaArcMin = ReadFinite(r), HoSampleCount = r.ReadInt32(), HoRejectedCount = r.ReadInt32(),
                IndexErrorArcMin = ReadFinite(r)
            };
            if (version >= 2)
            {
                o.ObservedSunAngularDiameterDeg = ReadFinite(r);
                o.SunLimb = (SunSightLimb)r.ReadInt32();
                o.SunDiscCompensationArcMin = ReadFinite(r);
            }
            if (version >= 3)
            {
                o.SolarReductionVersion = r.ReadInt32();
                o.RawHsDeg = ReadFinite(r); o.IndexCorrectedHDeg = ReadFinite(r);
                o.SolarEyeHeightM = ReadFinite(r); o.SolarFviiiArcMin = ReadFinite(r);
                o.SolarHorizonAltitudeDeg = ReadFinite(r);
            }
            ValidateObservation(o);
            return o;
        }

        private static void ValidateObservation(SextantObservation o)
        {
            Require(GeoValid(o.DrLatDeg, o.DrLonDeg), "invalid sight position");
            Require(o.BodyKind >= CelestialBodyKind.Sun && o.BodyKind <= CelestialBodyKind.Star,
                "invalid body kind");
            Require(o.BodyKind != CelestialBodyKind.Star || o.BodyHip > 0, "invalid star HIP");
            Require(o.HoDeg >= -1 && o.HoDeg <= 131 && Math.Abs(o.BodyDecDeg) <= 90,
                "invalid sight altitude/declination");
            Require(o.Quadrant >= 1 && o.Quadrant <= 4, "invalid quadrant");
            Require(o.HoSigmaArcMin >= 0 && o.HoSampleCount > 0 && o.HoRejectedCount >= 0,
                "invalid sight quality");
            // The limb enum must be one of the two the contract defines, and a
            // non-Sun sight must carry no solar disc at all.
            Require(o.SunLimb == SunSightLimb.LowerLimb || o.SunLimb == SunSightLimb.UpperLimb,
                "invalid sun limb");
            Require(o.BodyKind == CelestialBodyKind.Sun || o.ObservedSunAngularDiameterDeg == 0.0,
                "solar disc on a non-solar sight");
            ValidateSolarReduction(o);
        }

        private static void ValidateSolarReduction(SextantObservation o)
        {
            Require(o.SolarReductionVersion == 0 || o.SolarReductionVersion == 1, "unknown solar reduction");
            if (o.SolarReductionVersion == 0)
            {
                Require(o.RawHsDeg == 0 && o.IndexCorrectedHDeg == 0 && o.SolarEyeHeightM == 0
                    && o.SolarFviiiArcMin == 0 && o.SolarHorizonAltitudeDeg == 0,
                    "SH5 fields on legacy reduction");
                return; // never reinterpret frozen legacy Ho
            }
            Require(o.BodyKind == CelestialBodyKind.Sun && o.SunLimb == SunSightLimb.LowerLimb,
                "SH5 reduction on wrong body/limb");
            Require(o.Utc.Year >= 1939 && o.Utc.Year <= 1945 && o.SolarEyeHeightM >= 0 && o.SolarEyeHeightM <= 14,
                "SH5 time/eye out of bounds");
            ValidateSolarChain(o);
            Require(o.SunDiscCompensationArcMin == 0 && o.SolarHorizonAltitudeDeg >= -1
                && o.SolarHorizonAltitudeDeg <= 0 && o.ObservedSunAngularDiameterDeg >= 0.5
                && o.ObservedSunAngularDiameterDeg <= 0.56, "invalid SH5 optics provenance");
        }

        private static void ValidateSolarChain(SextantObservation o)
        {
            Require(o.IndexCorrectedHDeg >= 3 && o.IndexCorrectedHDeg <= 90
                && Math.Abs(o.IndexErrorArcMin) <= 5
                && Math.Abs(o.IndexCorrectedHDeg - o.RawHsDeg - o.IndexErrorArcMin / 60) < 1e-8
                && Math.Abs(o.HoDeg - o.IndexCorrectedHDeg - o.SolarFviiiArcMin / 60) < 1e-8,
                "broken frozen SH5 reduction chain");
            // Validate bounds and arithmetic, never re-evaluate tables or optics
            // on load: future table revisions must not rewrite a frozen sight.
            Require(Math.Abs(o.SolarFviiiArcMin) <= 17, "invalid frozen F VIII");
        }

        private static void WriteCourse(BinaryWriter w, CourseSamplerSnapshot state)
        {
            w.Write(state.CourseDeg); w.Write(state.SpeedKn);
            w.Write(state.Samples.Length);
            for (int i = 0; i < state.Samples.Length; i++)
            {
                w.Write(state.Samples[i].Utc.Ticks);
                w.Write(state.Samples[i].Lat); w.Write(state.Samples[i].Lon);
            }
        }

        private static CourseSamplerSnapshot ReadCourse(BinaryReader r)
        {
            var state = new CourseSamplerSnapshot { CourseDeg = r.ReadSingle(), SpeedKn = r.ReadSingle() };
            Require(Finite(state.CourseDeg) && Finite(state.SpeedKn)
                && state.SpeedKn >= 0 && state.SpeedKn <= CourseSampler.MaxImpliedSpeedKn,
                "invalid course/speed");
            int count = r.ReadInt32();
            Require(count >= 0 && count <= 8, "invalid track count");
            state.Samples = new CourseTrackSample[count];
            for (int i = 0; i < count; i++)
            {
                CourseTrackSample sample = new CourseTrackSample
                {
                    Utc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
                    Lat = ReadFinite(r), Lon = ReadFinite(r)
                };
                Require(GeoValid(sample.Lat, sample.Lon), "invalid track position");
                if (i > 0)
                    Require(sample.Utc > state.Samples[i - 1].Utc, "unordered track");
                state.Samples[i] = sample;
            }
            return state;
        }

        private static void WriteMarkers(BinaryWriter w, FixMarkerSnapshot state)
        {
            w.Write(state.HasFix); w.Write(state.Lat); w.Write(state.Lon);
            WriteText(w, state.Label, 2048);
            w.Write(state.HasTriangle); w.Write(state.SuppressedMask);
            for (int i = 0; i < 3; i++)
            {
                w.Write(state.VertexLat[i]); w.Write(state.VertexLon[i]);
            }
        }

        private static FixMarkerSnapshot ReadMarkers(BinaryReader r)
        {
            var state = new FixMarkerSnapshot
            {
                HasFix = r.ReadBoolean(), Lat = ReadFinite(r), Lon = ReadFinite(r),
                Label = ReadText(r, 2048), HasTriangle = r.ReadBoolean(), SuppressedMask = r.ReadInt32()
            };
            Require(state.SuppressedMask >= 0 && state.SuppressedMask <= 15, "invalid dismissal mask");
            Require(!state.HasFix || GeoValid(state.Lat, state.Lon), "invalid marker position");
            Require(!state.HasTriangle || state.HasFix, "triangle without fix");
            for (int i = 0; i < 3; i++)
            {
                state.VertexLat[i] = ReadFinite(r); state.VertexLon[i] = ReadFinite(r);
                Require(!state.HasTriangle || GeoValid(state.VertexLat[i], state.VertexLon[i]),
                    "invalid LOP vertex");
            }
            return state;
        }

        private static void ValidateSnapshot(SextantSaveSnapshot state)
        {
            Require(state.Observation2 == null || state.Observation1 != null, "second sight without first");
            Require(state.Observation3 == null || state.Observation2 != null, "third sight without second");
            Require(!state.FixPublished || state.Observation2 != null, "published fix without sights");
            Require(!state.FixPublished || GeoValid(state.FixLatDeg, state.FixLonDeg), "invalid fix position");
            Require(!double.IsInfinity(state.FixErrorNm), "invalid fix error");
        }

        private static bool GeoValid(double lat, double lon)
        {
            return Finite(lat) && Finite(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double ReadFinite(BinaryReader r)
        {
            double value = r.ReadDouble();
            Require(Finite(value), "non-finite number");
            return value;
        }

        private static void WriteText(BinaryWriter w, string text, int maxBytes)
        {
            if (text == null)
            {
                w.Write(-1);
                return;
            }
            byte[] bytes = Utf8.GetBytes(text);
            Require(bytes.Length <= maxBytes, "text too long");
            w.Write(bytes.Length); w.Write(bytes);
        }

        private static string ReadText(BinaryReader r, int maxBytes)
        {
            int length = r.ReadInt32();
            if (length == -1)
                return null;
            Require(length >= 0 && length <= maxBytes, "invalid text length");
            byte[] bytes = r.ReadBytes(length);
            Require(bytes.Length == length, "truncated text");
            return Utf8.GetString(bytes);
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
                throw new InvalidDataException(error);
        }
    }
}
