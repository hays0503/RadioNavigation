// Read-only offline witness; no Unity, native terrain or asset mutation.
using System;
using System.Globalization;
using UBOAT.Mods.RadioNavigation;
using UBOAT.Mods.RadioNavigation.Tests;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Expected game Coast Quad Tree.dat path.");
        var coast = CoastQuadTreePort.Load(args[0]);
        var culture = CultureInfo.InvariantCulture;
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            string[] point = line.Split(',');
            double lon = double.Parse(point[0], culture) / 200;
            double lat = double.Parse(point[1], culture) / 200;
            float d = coast.GetDistanceToCoastKm((float)LighthouseLandGate.LongitudeToKm(lon),
                                                (float)LighthouseLandGate.LatitudeToKm(lat));
            Console.WriteLine(d.ToString("R", culture));
        }
    }
}
