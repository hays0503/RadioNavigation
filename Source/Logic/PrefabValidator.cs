using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Validates the Radio prefab hierarchy against the hardcoded child paths
    /// (ADR-019, extracted from Main.cs P5.5). Pure logic: the caller supplies
    /// a path-existence predicate so the check is testable without Unity.
    /// </summary>
    public static class PrefabValidator
    {
        public const string SkipValidationKey = "RadioNav.SkipValidation";

        /// <summary>The child paths every Radio prefab must contain.</summary>
        public static readonly string[] RequiredChildren =
        {
            PrefabNames.Cursor,
            PrefabNames.Toggle,
            PrefabNames.Ground,
            PrefabNames.Ground + "/" + PrefabNames.Background,
            PrefabNames.Ground + "/" + PrefabNames.Background + "/" + PrefabNames.GroundCheckmark,
            PrefabNames.Expander,
            PrefabNames.RadioZone,
            PrefabNames.VolumeSpinner,
            PrefabNames.FeedbackSpinner
        };

        /// <summary>Returns the subset of required paths absent from the prefab.</summary>
        public static List<string> FindMissing(Func<string, bool> pathExists)
        {
            var missing = new List<string>();
            foreach (string path in RequiredChildren)
            {
                if (!pathExists(path))
                    missing.Add(path);
            }
            return missing;
        }
    }
}
