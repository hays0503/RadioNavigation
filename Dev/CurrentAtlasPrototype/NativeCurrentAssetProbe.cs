using System;
using System.Globalization;
using System.IO;
using PlayWay.Water.Extensions;
using UnityEngine;

// Inspect candidates without awakening their Unity/game behaviour.
internal static class NativeCurrentAssetProbe
{
    public static void Run(string output)
    {
        new NativeGameBridge(137);
        string game = Directory.GetParent(Path.GetDirectoryName(NativeGameBridge.Managed)).FullName;
        string bundles = Path.Combine(game, "UBOAT_Data/StreamingAssets/Packages/uboat/Bundles");
        using (var report = new StreamWriter(Path.Combine(output, "current-assets.tsv")))
        {
            report.WriteLine("bundle\tasset\troot\trigidBodies\tnativeWaterBodies\tkinematic\tmassKg");
            Inspect(report, bundles, "uboat.entities.liberty");
            Inspect(report, bundles, "uboat.entities.large-freighter");
            Inspect(report, bundles, "uboat.entities.lifeboats");
        }
    }

    private static void Inspect(StreamWriter report, string path, string name)
    {
        var bundle = AssetBundle.LoadFromFile(Path.Combine(path, name));
        if (bundle == null)
            throw new InvalidOperationException("Missing probe bundle " + name);
        try
        {
            foreach (string asset in bundle.GetAllAssetNames())
            {
                var root = bundle.LoadAsset<GameObject>(asset);
                if (root == null)
                    continue;
                var bodies = root.GetComponentsInChildren<Rigidbody>(true);
                var water = root.GetComponentsInChildren<NeuralNetworkPhysics>(true);
                int kinematic = 0;
                double mass = 0;
                foreach (var body in bodies)
                {
                    if (body.isKinematic)
                        kinematic++;
                    mass += body.mass;
                }
                report.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6:R}",
                    name, asset, root.name, bodies.Length, water.Length, kinematic, mass));
            }
        }
        finally
        {
            bundle.Unload(true);
        }
    }
}
