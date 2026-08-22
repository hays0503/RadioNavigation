using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Registry of lighthouse profiles. Wangerooge is the only enabled entry
    /// and the one the mod actually spawns today; the other entries are
    /// curated from the Leuchtfeuer registry (Source/Data/Leuchtfeuer.json)
    /// and are ready to be switched on with a single "Enabled = true".
    ///
    /// To add a lighthouse:
    ///  1. run Tools/Import-Leuchtfeuer.ps1 with the desired filter to get a
    ///     profile block seeded from the registry data;
    ///  2. paste the block into All below;
    ///  3. set RotationDegreesPerSecond (0 for a fixed light, 360/period for
    ///     a rotating beam) and Enabled.
    /// </summary>
    public static class LighthouseProfiles
    {
        public static readonly LighthouseProfile[] All =
        {
            new LighthouseProfile(
                code: "MOD-WANGEROOGE",
                name: "Wangerooge",
                country: "Deutschland",
                location: "Wangerooge (Nordsee)",
                latitude: 53.519973f,
                longitude: 8.127701f,
                fireHeightM: 63f,
                characteristic: "Fl W 20s",
                type: "Lighthouse",
                prefabPath: "lighthouse",
                rotatorObjectName: "LighthouseRotator",
                rotationDegreesPerSecond: 18f, // full beam rotation in 20 s
                enabled: true),

            new LighthouseProfile(
                code: "B1054",
                name: "Leuchtturm Norderney",
                country: "Deutschland",
                location: "Norderney (Nordsee)",
                latitude: 53.709217f,
                longitude: 7.229633f,
                fireHeightM: 59f,
                characteristic: "Fl(3)W.12s23M",
                type: "Breakwater light",
                prefabPath: "lighthouse",
                rotatorObjectName: "LighthouseRotator",
                rotationDegreesPerSecond: 0f,
                enabled: false),

            new LighthouseProfile(
                code: "B0983",
                name: "Leuchtturm Campen - Leitfeuer",
                country: "Deutschland",
                location: "Campen (Nordsee)",
                latitude: 53.405667f,
                longitude: 7.0155f,
                fireHeightM: 62f,
                characteristic: "Dir.F.W.30M; Dir.Fl.W.5s30M; Dir.Fl(4)W.15s30M",
                type: "Breakwater light",
                prefabPath: "lighthouse",
                rotatorObjectName: "LighthouseRotator",
                rotationDegreesPerSecond: 0f,
                enabled: false),

            new LighthouseProfile(
                code: "B1309",
                name: "Obereversand",
                country: "Deutschland",
                location: "Dorum-Neufeld (Weser)",
                latitude: 53.739167f,
                longitude: 8.508333f,
                fireHeightM: 29f,
                characteristic: "F.W.",
                type: "Breakwater light",
                prefabPath: "lighthouse",
                rotatorObjectName: "LighthouseRotator",
                rotationDegreesPerSecond: 0f,
                enabled: false),

            new LighthouseProfile(
                code: "B1318",
                name: "Vorhafen Molenfeuer Ost Knie",
                country: "Deutschland",
                location: "Helgoland (Helgoland)",
                latitude: 54.171933f,
                longitude: 7.89925f,
                fireHeightM: 5f,
                characteristic: "Oc.WG.6s7/4M",
                type: "Breakwater light",
                prefabPath: "lighthouse",
                rotatorObjectName: "LighthouseRotator",
                rotationDegreesPerSecond: 0f,
                enabled: false),
        };

        public static IEnumerable<LighthouseProfile> Active()
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Enabled)
                {
                    yield return All[i];
                }
            }
        }
    }
}
