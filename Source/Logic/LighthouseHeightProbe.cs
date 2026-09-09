using System;
using System.Globalization;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One-shot calibration for the Leuchtfeuer lighthouse fleet: measures
    /// the mod's "lighthouse" prefab real-world height (m) so the per-
    /// lighthouse scale is fireHeightM / H0. Run once from the dev console:
    ///   rn_lighthouse_height [prefabName]
    ///
    /// The prefab asset is instantiated off-screen (y = -1000; terrain is
    /// not needed for renderer bounds), the combined world-space bounds of
    /// all renderers give H0, then the instance is destroyed and the asset
    /// usage freed. The result is persisted in PlayerPrefs so later sessions
    /// reuse it without re-measuring.
    /// </summary>
    public static class LighthouseHeightProbe
    {
        public const string PlayerPrefsKey = "RadioNavigation.Lighthouse.H0M";
        public const string DefaultPrefab = "lighthouse";

        [Inject] private static ResourceManager resourceManager = null;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            DevConsole.AddListener("rn_lighthouse_height", Measure);
            Debug.Log("[RadioNav][Lighthouse] rn_lighthouse_height registered, saved H0=" +
                PlayerPrefs.GetFloat(PlayerPrefsKey, 0f).ToString("F2", CultureInfo.InvariantCulture) + " m");
        }

        private static void Measure(string[] args)
        {
            // DevConsole.Submit passes the FULL tokenized line: args[0] is the
            // command name itself (DevConsole.cs:201-204), so the optional
            // prefab argument lives at index 1.
            string prefabName = (args != null && args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                ? args[1]
                : DefaultPrefab;

            if (resourceManager == null)
            {
                Debug.LogError("[RadioNav][Lighthouse] probe: ResourceManager not injected");
                return;
            }

            GameObject holder = new GameObject("LighthouseHeightProbe");
            try
            {
                Resource<GameObject> res = resourceManager.RetrieveAsset<GameObject>(prefabName, holder, false);
                if (!res.Valid || res.Asset == null)
                {
                    Debug.LogError("[RadioNav][Lighthouse] probe: prefab not found: '" + prefabName + "'");
                    return;
                }

                GameObject instance = UnityEngine.Object.Instantiate(res.Asset,
                    new Vector3(0f, -1000f, 0f), Quaternion.identity);
                float hLamp = MeasureLampHeightM(instance);
                float hSolid = MeasureBoundsHeightM(instance, true, true);
                LogTallestRenderers(instance);
                UnityEngine.Object.Destroy(instance);

                if (hLamp <= 0f || float.IsNaN(hLamp))
                {
                    Debug.LogError("[RadioNav][Lighthouse] probe: rotator pivot not found on '" + prefabName + "'");
                    return;
                }

                PlayerPrefs.SetFloat(PlayerPrefsKey, hLamp);
                PlayerPrefs.Save();
                Debug.Log("[RadioNav][Lighthouse] H0=" + hLamp.ToString("F2", CultureInfo.InvariantCulture) +
                    " m (lamp pivot; solid-mesh union=" + hSolid.ToString("F2", CultureInfo.InvariantCulture) +
                    ") prefab='" + prefabName + "' scale(fireM/H0): Wangerooge 63m -> " +
                    (63f / hLamp).ToString("F3", CultureInfo.InvariantCulture) +
                    ", pier 8m -> " + (8f / hLamp).ToString("F3", CultureInfo.InvariantCulture));
            }
            finally
            {
                resourceManager.FreeAllAssets(holder);
                UnityEngine.Object.Destroy(holder);
            }
        }

        /// <summary>
        /// Height of the lamp pivot above the prefab base (m): the DB
        /// fireHeight is the height of the LIGHT above sea level, so the
        /// scale formula fireHeightM / H0 lands the lamp exactly at the
        /// real height. Falls back to 0 when the rotator child is missing.
        /// </summary>
        private static float MeasureLampHeightM(GameObject root)
        {
            Transform rotator = TransformHelpers.FindChildRecursive(root.transform, "LighthouseRotator");
            if (rotator == null)
            {
                return 0f;
            }

            return rotator.position.y - root.transform.position.y;
        }

        private static float MeasureBoundsHeightM(GameObject root, bool meshOnly, bool excludeBeam)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds bounds = default(Bounds);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || (meshOnly && !(r is MeshRenderer)))
                {
                    continue;
                }

                // The spotlight cone mesh is ~1.2 km tall and would poison
                // the union; it is not part of the visible tower.
                if (excludeBeam && r.name == "Spot Light")
                {
                    continue;
                }

                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return any ? bounds.size.y : 0f;
        }

        /// Diagnostics: the tallest renderers, so a runaway bounds outlier
        /// (particle systems, flare quads) is visible by name.
        private static void LogTallestRenderers(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            var heights = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                float h = r.bounds.size.y;
                if (h > 0f)
                {
                    heights.Add(new System.Collections.Generic.KeyValuePair<float, string>(h,
                        r.GetType().Name + ":" + GetPath(r.transform)));
                }
            }

            heights.Sort((a, b) => b.Key.CompareTo(a.Key));
            int count = Math.Min(heights.Count, 8);
            for (int i = 0; i < count; i++)
            {
                Debug.Log("[RadioNav][Lighthouse] probe renderer[" + i + "] h=" +
                    heights[i].Key.ToString("F2", CultureInfo.InvariantCulture) + " m  " + heights[i].Value);
            }
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }
    }
}
