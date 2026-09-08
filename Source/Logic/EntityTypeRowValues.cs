using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The 18-column Entities/Types row layout behind
    /// LighthousePlacer.CreateEntityType. Extracted (SRP — sheet layout is
    /// not placement logic) so it runs offline in EntityTypeRowValuesTests.
    /// Fills a caller-owned buffer (the placer keeps its P5.1 reused
    /// statics). Pure C#.
    /// </summary>
    public static class EntityTypeRowValues
    {
        public static void BuildValues(string typeName, string prefabPath, List<string> values)
        {
            values.Clear();
            values.Add(typeName);       // 0: name
            values.Add("Structure");    // 1: category
            values.Add("0");            // 2: speed
            values.Add("0");            // 3: standard displacement
            values.Add("0");            // 4: full displacement
            values.Add("0");            // 5: length
            values.Add("0");            // 6: beam
            values.Add("0");            // 7: draught
            values.Add("0");            // 8: mast height
            values.Add(prefabPath);     // 9: keyed as "Prefab Path"
            values.Add("0");            // 10: range
            values.Add("0");            // 11: crew
            values.Add("0");            // 12: threat
            values.Add("False");        // 13: military
            values.Add("False");        // 14: is class
            values.Add("0");            // 15: reward
            values.Add("0");            // 16: iron cross
            values.Add("0");            // 17: estimated durability
        }
    }
}
