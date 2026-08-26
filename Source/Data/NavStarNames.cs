// Auto-generated from Stellarium skycultures/common_star_names.fab by Tools/gen-navstar-names.ps1
// (SEXTANT_PLAN sec. 7.7, star list STAR_SKY_PLAN sec. 3.7). Do not edit by hand.
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    public static class NavStarNames
    {
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 677, "Alpheratz" },
            { 2081, "Ankaa" },
            { 3179, "Schedar" },
            { 3419, "Diphda" },
            { 7588, "Achernar" },
            { 9884, "Hamal" },
            { 11767, "Polaris" },
            { 13847, "Acamar" },
            { 14135, "Menkar" },
            { 15863, "Mirfak" },
            { 21421, "Aldebaran" },
            { 24436, "Rigel" },
            { 24608, "Capella" },
            { 25336, "Bellatrix" },
            { 25428, "Elnath" },
            { 26311, "Alnilam" },
            { 27989, "Betelgeuse" },
            { 30438, "Canopus" },
            { 32349, "Sirius" },
            { 33579, "Adhara" },
            { 37279, "Procyon" },
            { 37826, "Pollux" },
            { 41037, "Avior" },
            { 44816, "Suhail" },
            { 45238, "Miaplacidus" },
            { 46390, "Alphard" },
            { 49669, "Regulus" },
            { 54061, "Dubhe" },
            { 57632, "Denebola" },
            { 59803, "Gienah" },
            { 60718, "Acrux" },
            { 61084, "Gacrux" },
            { 62956, "Alioth" },
            { 65474, "Spica" },
            { 67301, "Alkaid" },
            { 68702, "Hadar" },
            { 68933, "Menkent" },
            { 69673, "Arcturus" },
            { 71683, "Rigil Kentaurus" },
            { 72607, "Kochab" },
            { 76267, "Alphecca" },
            { 80763, "Antares" },
            { 82273, "Atria" },
            { 84012, "Sabik" },
            { 85927, "Shaula" },
            { 86032, "Rasalhague" },
            { 87833, "Eltanin" },
            { 90185, "Kaus Australis" },
            { 91262, "Vega" },
            { 92855, "Nunki" },
            { 97649, "Altair" },
            { 100751, "Peacock" },
            { 102098, "Deneb" },
            { 107315, "Enif" },
            { 109268, "Al Na’ir" },
            { 113368, "Fomalhaut" },
            { 113963, "Markab" },
        };

        public static bool TryGetName(int hip, out string name)
        {
            return Names.TryGetValue(hip, out name);
        }
    }
}
