using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Shared transform helpers. FindChildRecursive was duplicated verbatim
    /// in LighthouseBeamGlow and LighthousePlacer — consolidated here
    /// (behavioral parity verified by TransformHelpersTests).
    /// </summary>
    public static class TransformHelpers
    {
        /// <summary>
        /// Depth-first search for a direct or nested child by exact name.
        /// The root itself is a match. Returns null for a null root, an
        /// empty name, or when nothing matches. The first match in
        /// traversal order wins.
        /// </summary>
        public static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
