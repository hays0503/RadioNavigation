using System;
using System.Collections.Generic;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Runtime loader for the Leuchtfeuer lighthouse registry
    /// (Source/Data/Leuchtfeuer.json — 552 lights after the 2026-09-09
    /// land curation, ADR-030, and fire-height curation, ADR-031: the 287
    /// water-side entries are archived in LeuchtfeuerExcluded.json, the
    /// 345 theater lights below 15 m fire height in LeuchtfeuerMinor.json;
    /// neither archive is read at runtime).
    /// Filters the DB to
    /// the game theater (North Sea + Baltic window), converts each entry to
    /// a LighthouseProfile and dedupes against the curated profiles so the
    /// manually tuned Wangerooge entry is not placed twice.
    ///
    /// All registry profiles are Enabled: the map marker layer draws every
    /// one of them, while LighthousePlacer gates the expensive 3D spawn
    /// behind its own distance gate. Loading failures degrade gracefully —
    /// the mod keeps working with the curated profiles only.
    /// </summary>
    public static class LeuchtfeuerRegistry
    {
        /// <summary>Game theater window (lat/lon degrees, inclusive).</summary>
        public const double MinLatitude = 49.0;
        public const double MaxLatitude = 62.0;
        public const double MinLongitude = -5.0;
        public const double MaxLongitude = 20.0;

        /// <summary>Dedupe radius vs curated profiles (degrees, ~550 m).</summary>
        public const double DedupeDegrees = 0.005;

        public const string FileName = "Leuchtfeuer.json";
        public const string PrefabPath = "lighthouse";
        public const string RotatorObjectName = "LighthouseRotator";

        private static readonly List<LighthouseProfile> profiles =
            new List<LighthouseProfile>();

        public static bool Loaded { get; private set; }
        public static string LastLoadError { get; private set; }

        public static IReadOnlyList<LighthouseProfile> Profiles
        {
            get { return profiles; }
        }

        /// <summary>
        /// Loads and filters the registry. curated provides the hand-tuned
        /// profiles used for dedupe (their coordinates win). Returns false
        /// with LastLoadError set when the file is missing or malformed.
        /// </summary>
        public static bool Initialize(string dataDirectory, IList<LighthouseProfile> curated)
        {
            profiles.Clear();
            Loaded = false;
            LastLoadError = null;

            if (string.IsNullOrEmpty(dataDirectory))
            {
                LastLoadError = "data directory is null";
                return false;
            }

            string path = System.IO.Path.Combine(dataDirectory, FileName);
            if (!System.IO.File.Exists(path))
            {
                LastLoadError = "registry file not found: " + path;
                return false;
            }

            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Parse(System.IO.File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                LastLoadError = FileName + ": parse failed: " + ex.Message;
                return false;
            }

            object entriesValue;
            if (!root.TryGetValue("entries", out entriesValue)
                || !(entriesValue is List<object>))
            {
                LastLoadError = FileName + ": missing 'entries' array";
                return false;
            }

            int skipped = 0;
            List<object> entries = (List<object>)entriesValue;
            for (int i = 0; i < entries.Count; i++)
            {
                LighthouseProfile profile = BuildProfile(entries[i] as Dictionary<string, object>);
                if (profile == null)
                {
                    skipped++;
                    continue;
                }

                if (!InTheater(profile.Latitude, profile.Longitude))
                {
                    skipped++;
                    continue;
                }

                if (IsDuplicateOfCurated(profile, curated))
                {
                    skipped++;
                    continue;
                }

                profiles.Add(profile);
            }

            Loaded = true;
            return true;
        }

        /// <summary>Theater bounds check (inclusive).</summary>
        public static bool InTheater(double latitude, double longitude)
        {
            return latitude >= MinLatitude && latitude <= MaxLatitude
                && longitude >= MinLongitude && longitude <= MaxLongitude;
        }

        /// <summary>
        /// Converts one JSON entry to a profile; null when the entry is
        /// garbage (missing name/coords). fireHeight parse failures keep the
        /// profile but fall back to a sane default height.
        /// </summary>
        public static LighthouseProfile BuildProfile(Dictionary<string, object> entry)
        {
            if (entry == null)
            {
                return null;
            }

            string name = GetString(entry, "name");
            double lat = GetDouble(entry, "lat");
            double lon = GetDouble(entry, "lon");
            if (string.IsNullOrEmpty(name) || double.IsNaN(lat) || double.IsNaN(lon))
            {
                return null;
            }

            float fireHeight = ParseFireHeightM(GetString(entry, "fireHeight"));
            if (fireHeight <= 0f)
            {
                fireHeight = 20f; // sane visual default for broken DB rows
            }

            string characteristic = GetString(entry, "characteristic");
            return new LighthouseProfile(
                code: GetString(entry, "code"),
                name: name,
                country: GetString(entry, "country"),
                location: GetString(entry, "location"),
                latitude: (float)lat,
                longitude: (float)lon,
                fireHeightM: fireHeight,
                characteristic: characteristic,
                type: GetString(entry, "type"),
                prefabPath: PrefabPath,
                rotatorObjectName: RotatorObjectName,
                rotationDegreesPerSecond: RotationFromCharacteristic(characteristic),
                enabled: true);
        }

        /// <summary>"13 m" -> 13; garbage -> 0.</summary>
        public static float ParseFireHeightM(string fireHeight)
        {
            if (string.IsNullOrEmpty(fireHeight))
            {
                return 0f;
            }

            string digits = "";
            bool seenDigit = false;
            for (int i = 0; i < fireHeight.Length; i++)
            {
                char c = fireHeight[i];
                if (char.IsDigit(c) || c == '.' || c == ',')
                {
                    digits += c == ',' ? '.' : c;
                    seenDigit = true;
                }
                else if (seenDigit)
                {
                    break; // first non-digit after the number ends it (" m")
                }
            }

            float value;
            if (!seenDigit || !float.TryParse(
                    digits, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return 0f;
            }

            return value;
        }

        /// <summary>
        /// Beam rotation from the IALA characteristic: the first "Ns"
        /// period found drives 360/N °/s; no period means a fixed light
        /// (0). Range suffixes like "23M" are not periods.
        /// </summary>
        public static float RotationFromCharacteristic(string characteristic)
        {
            float periodSeconds = FindPeriodSeconds(characteristic);
            if (periodSeconds <= 0f)
            {
                return 0f;
            }

            return 360f / periodSeconds;
        }

        private static float FindPeriodSeconds(string characteristic)
        {
            if (string.IsNullOrEmpty(characteristic))
            {
                return 0f;
            }

            for (int i = 0; i < characteristic.Length; i++)
            {
                if (char.ToLowerInvariant(characteristic[i]) != 's' || i == 0)
                {
                    continue;
                }

                // Walk back over digits to find "Ns"; a dot/comma is only a
                // decimal separator when digits stand on both sides (so the
                // '.' in "W.12s" does not turn the period into 0.12).
                int end = i;
                int start = i;
                while (start > 0)
                {
                    char prev = characteristic[start - 1];
                    if (char.IsDigit(prev))
                    {
                        start--;
                        continue;
                    }
                    if ((prev == '.' || prev == ',')
                        && start >= 2
                        && char.IsDigit(characteristic[start - 2]))
                    {
                        start--;
                        continue;
                    }
                    break;
                }

                if (start == end || !float.TryParse(
                        characteristic.Substring(start, end - start),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var period) || period <= 0f)
                {
                    continue;
                }

                return period;
            }

            return 0f;
        }

        private static bool IsDuplicateOfCurated(LighthouseProfile profile, IList<LighthouseProfile> curated)
        {
            if (curated == null)
            {
                return false;
            }

            for (int i = 0; i < curated.Count; i++)
            {
                double dLat = Math.Abs(curated[i].Latitude - profile.Latitude);
                double dLon = Math.Abs(curated[i].Longitude - profile.Longitude);
                if (dLat <= DedupeDegrees && dLon <= DedupeDegrees)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetString(Dictionary<string, object> entry, string key)
        {
            object value;
            return entry.TryGetValue(key, out value) ? value as string : null;
        }

        private static double GetDouble(Dictionary<string, object> entry, string key)
        {
            object value;
            if (!entry.TryGetValue(key, out value) || !(value is double))
            {
                return double.NaN;
            }

            return (double)value;
        }
    }
}
