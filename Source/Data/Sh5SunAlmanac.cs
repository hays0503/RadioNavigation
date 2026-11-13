using System;
using System.IO;
using System.Security.Cryptography;

namespace UBOAT.Mods.RadioNavigation
{
    public struct Sh5AlmanacDetails
    {
        public DateTime RowUtc;
        public double Hours, GhaDeg, DecDeg, IncrementArcMin;
    }

    /// <summary>Exact printed SH5 campaign rows, not a fitted Meeus offset.
    /// GMT columns; PDF p.73 Sun increments = 15 degrees/hour and signed
    /// four-hour d. Complete leap-aware dense pack, hash-checked on load.</summary>
    public static class Sh5SunAlmanac
    {
        public const string FileName = "sh5-sun.bin";
        public const string PackSha256 = "8e1fc1da5b092dad0d08a3fc7a74f58ba008b0dd18904475ae9c988ff999e1b0";
        public const string SourceSha256 = "0faa33d1afc3835a4cca72c8c7b21750e611ad5e11a339adffe14c6f50068f48";
        private const int Count = 15342;
        private static readonly DateTime start = new DateTime(1939, 1, 1);
        private static readonly DateTime end = new DateTime(1946, 1, 1);
        private static int[] gha;
        private static short[] declination;
        private static short[] increments;

        public static bool Loaded { get { return gha != null; } }
        public static string LastError { get; private set; }

        public static bool Initialize(string dataDirectory)
        {
            gha = null;
            declination = null;
            increments = null;
            LastError = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(dataDirectory, FileName));
                if (bytes.Length != 40 + Count * 8 || Hash(bytes) != PackSha256)
                    throw new InvalidDataException("SH5 Sun pack length/hash mismatch");
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 0x31534E52 || reader.ReadInt32() != Count
                        || Hex(reader.ReadBytes(32)) != SourceSha256)
                        throw new InvalidDataException("SH5 Sun pack provenance mismatch");
                    var newGha = new int[Count];
                    var newDec = new short[Count];
                    var newD = new short[Count];
                    for (int i = 0; i < Count; i++)
                    {
                        newGha[i] = reader.ReadInt32();
                        newDec[i] = reader.ReadInt16();
                        newD[i] = reader.ReadInt16();
                    }
                    declination = newDec;
                    increments = newD;
                    gha = newGha; // publish only the complete validated pack
                }
                return true;
            }
            catch (Exception error)
            {
                LastError = error.Message;
                return false;
            }
        }

        public static bool TryPlace(DateTime utc, out double ghaDeg, out double decDeg)
        {
            ghaDeg = 0;
            decDeg = 0;
            Sh5AlmanacDetails details;
            if (!TryDetails(utc, out details))
                return false;
            ghaDeg = RunningFixCalculator.Normalize360(details.GhaDeg + details.Hours * 15);
            decDeg = details.DecDeg + details.IncrementArcMin / 60 * details.Hours / 4;
            return true;
        }

        public static bool TryDetails(DateTime utc, out Sh5AlmanacDetails details)
        {
            details = new Sh5AlmanacDetails();
            // Unspecified is the game clock's historical UTC convention. Never
            // silently accept host-local time or extrapolate out of campaign.
            if (!Loaded || utc.Kind == DateTimeKind.Local || utc < start || utc >= end)
                return false;
            int index = (utc.Date - start).Days * 6 + utc.Hour / 4;
            details.RowUtc = utc.Date.AddHours(utc.Hour / 4 * 4);
            details.Hours = utc.TimeOfDay.TotalHours - utc.Hour / 4 * 4;
            details.GhaDeg = gha[index] / 600.0;
            details.DecDeg = declination[index] / 600.0;
            details.IncrementArcMin = increments[index] / 10.0;
            return true;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(bytes));
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
