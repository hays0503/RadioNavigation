using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using UBOAT.Mods.RadioNavigation;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 && args.Length != 5)
                throw new ArgumentException("Usage: <tables.json> <artifacts/output.json> [latDeg lonDeg deltaMinutes]");
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string output = Path.GetFullPath(args[1]);
            string artifacts = Path.Combine(root, "artifacts") + Path.DirectorySeparatorChar;
            if (!output.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase)
                || string.Equals(output, Path.GetFullPath(args[0]), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be a new report under this project's artifacts/");
            double lat = args.Length == 5 ? Parse(args[2]) : 55.0;
            double lon = args.Length == 5 ? Parse(args[3]) : 5.75;
            double delta = args.Length == 5 ? Parse(args[4]) : 0.0;
            var rows = new List<object>();
            using (JsonDocument input = JsonDocument.Parse(File.ReadAllText(args[0])))
            {
                foreach (JsonElement row in input.RootElement.GetProperty("rows").EnumerateArray())
                {
                    string stamp = row.GetProperty("utc").GetString();
                    if (stamp == null || !stamp.EndsWith("Z", StringComparison.Ordinal))
                        throw new ArgumentException("Each C# input row must carry explicit GMT/UTC Z");
                    DateTime utc = DateTime.Parse(stamp, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                    if (utc.Year < 1939 || utc.Year > 1945)
                        throw new ArgumentException("Each C# input row must be in the campaign");
                    rows.Add(new { utc = utc.ToString("O", CultureInfo.InvariantCulture),
                        uncalibrated = Fields(SolarSightAudit.Compute(utc, lat, lon, 0.0)),
                        calibrated = Fields(SolarSightAudit.Compute(utc, lat, lon, delta)) });
                }
            }
            if (rows.Count == 0)
                throw new ArgumentException("No input rows");
            string[] sources = { "AstroMath.cs", "SunDiscScale.cs", "SunEphemerisCalibration.cs", "SolarSightAudit.cs" };
            var hashes = new Dictionary<string, string>();
            foreach (string source in sources)
                hashes[source] = Hash(Path.Combine(root, "Source", "Logic", source));
            var report = new { schema = 1, method = "Actual linked C#; geometric centre; GMST/UT + solar TT; no refraction",
                inputSha256 = Hash(args[0]), sourceSha256 = hashes, assemblySha256 = Hash(typeof(Program).Assembly.Location),
                latDeg = lat, lonDeg = lon, deltaMinutes = delta, rows };
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("C# solar samples=" + rows.Count + "; " + output);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private static object Fields(SolarSightAudit.Sample s)
    {
        return new { ghaDeg = s.GhaDeg, decDeg = s.DecDeg, altDeg = s.AltDeg,
            azDeg = s.AzDeg, diameterDeg = s.DiameterDeg, jdUt = s.JdUt, jdTt = s.JdTt };
    }

    private static double Parse(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static string Hash(string path)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
}
