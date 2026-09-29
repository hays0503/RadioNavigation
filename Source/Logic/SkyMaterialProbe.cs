using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One-shot sky-material diagnostic (2026-09-29). The game's own star dome
    /// (<c>Stars</c>, material <c>Material:Stars</c>, shader family
    /// <c>Time of Day/…</c>) is drawn BEFORE the UniStorm cloud composite, so
    /// the clouds cover it — while the mod's five celestial renderers draw
    /// afterwards and therefore paint over the clouds. This probe dumps the
    /// render queue / shader / render type of the game's sky-flavoured
    /// materials next to the mod's five materials (plus camera clips and the
    /// relevant GameObject layers) so the celestial geometry can later be moved
    /// into the same draw layer as the game's dome. It only logs — nothing in
    /// the render path is changed.
    ///
    /// Runs at most once per session; the guard is a static bool reset by the
    /// mod's hot-reload hygiene (<see cref="Reset"/>).
    /// </summary>
    public sealed class SkyMaterialProbe : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][SkyProbe] ";
        private const int MaxMaterialLines = 16;
        private const int MaxNamedObjectLines = 4;
        private const int MaxTotalLines = 40;

        /// <summary>Case-insensitive name fragments that mark a sky material.</summary>
        private static readonly string[] NameFragments =
        {
            "Stars", "Atmosphere", "Cloud", "Space", "Sky"
        };

        private static bool logged;

        /// <summary>Hot-reload hygiene (a fresh assembly already starts with a
        /// false guard; the explicit reset keeps the convention).</summary>
        public static void Reset()
        {
            logged = false;
        }

        /// <summary>Creates the probe component once per session, if not
        /// already logged and not already present.</summary>
        public static void EnsureRunning()
        {
            if (logged)
                return;
            if (FindObjectOfType<SkyMaterialProbe>() != null)
                return;
            var go = new GameObject("SkyMaterialProbe");
            go.AddComponent<SkyMaterialProbe>();
            DontDestroyOnLoad(go);
        }

        private void Start()
        {
            StartCoroutine(LogAfterFirstFrame());
        }

        private IEnumerator LogAfterFirstFrame()
        {
            // Let the scene settle: materials exist (StarsMesh resolves its
            // bundled shader in LateUpdate) and Camera.main is tagged (it can
            // still be null on the first frame). Bounded wait, then one dump.
            for (int i = 0; i < 120 && Camera.main == null; i++)
                yield return null;
            yield return null;
            if (logged)
                yield break;
            logged = true;
            LogSnapshot();
        }

        private static void LogSnapshot()
        {
            var lines = new List<string>();
            CollectGameMaterials(lines);
            CollectModMaterials(lines);
            CollectCamera(lines);
            CollectNamedObject(lines, "Stars");
            CollectNamedObject(lines, "Sky Dome");
            CollectNamedObject(lines, "StarSkyRoot");

            if (lines.Count > MaxTotalLines)
            {
                int extra = lines.Count - MaxTotalLines;
                lines.RemoveRange(MaxTotalLines, extra);
                lines.Add("... (+" + extra + " more lines truncated)");
            }
            for (int i = 0; i < lines.Count; i++)
                Debug.Log(LogPrefix + lines[i]);
        }

        /// <summary>Every loaded <see cref="Material"/> whose name contains a
        /// sky fragment, deduplicated by (name, shader, queue) and sorted.</summary>
        private static void CollectGameMaterials(List<string> lines)
        {
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            var seen = new HashSet<string>();
            var matches = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i];
                if (m == null || string.IsNullOrEmpty(m.name))
                    continue;
                if (!MatchesSkyName(m.name))
                    continue;
                string shader = m.shader != null ? m.shader.name : "null";
                if (!seen.Add(m.name + "|" + shader + "|" + m.renderQueue))
                    continue;
                matches.Add("mat '" + m.name + "' shader='" + shader
                    + "' queue=" + m.renderQueue + " type='" + m.GetTag("RenderType", false) + "'");
            }
            matches.Sort(StringComparer.Ordinal);
            lines.Add("game sky materials: " + matches.Count + " matched of "
                + all.Length + " loaded (dedup by name/shader/queue)");
            int cap = Math.Min(matches.Count, MaxMaterialLines);
            for (int i = 0; i < cap; i++)
                lines.Add(matches[i]);
            if (matches.Count > cap)
                lines.Add("  ... (+" + (matches.Count - cap) + " more game materials)");
        }

        private static bool MatchesSkyName(string name)
        {
            for (int i = 0; i < NameFragments.Length; i++)
            {
                if (name.IndexOf(NameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void CollectModMaterials(List<string> lines)
        {
            AddModMaterial<StarsMesh>(lines, "StarsMesh");
            AddModMaterial<ConstellationMesh>(lines, "ConstellationMesh");
            AddModMaterial<PlanetRenderer>(lines, "PlanetRenderer");
            AddModMaterial<MoonDiscRenderer>(lines, "MoonDiscRenderer");
            AddModMaterial<SunDiscRenderer>(lines, "SunDiscRenderer");
        }

        private static void AddModMaterial<T>(List<string> lines, string label)
            where T : MonoBehaviour
        {
            T[] found = FindObjectsOfType<T>(true);
            if (found == null || found.Length == 0)
            {
                lines.Add("mod " + label + ": none in scene");
                return;
            }

            GameObject go = found[0].gameObject;
            Renderer r = found[0].GetComponent<Renderer>();
            Material m = r != null ? r.sharedMaterial : null;
            string matName = m != null ? m.name : "null";
            string shader = m != null && m.shader != null ? m.shader.name : "null";
            int queue = m != null ? m.renderQueue : -1;
            bool occl = m != null && m.HasProperty("_CloudOcclusionTex");
            lines.Add("mod " + label + " count=" + found.Length
                + " go='" + go.name + "' layer=" + go.layer
                + " mat='" + matName + "' shader='" + shader
                + "' queue=" + queue + " cloudOcclusion=" + occl);
        }

        private static void CollectCamera(List<string> lines)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                lines.Add("camera: none (Camera.main is null)");
                return;
            }
            lines.Add("camera '" + cam.name + "' near=" + cam.nearClipPlane
                + " far=" + cam.farClipPlane);
        }

        /// <summary>Logs the numeric layer of scene objects with the given exact
        /// name, sorted by hierarchy path (the sky-dome child
        /// <c>Sky Dome/Stars</c> therefore sorts before unrelated
        /// <c>World Canvas/…</c> objects). Assets / prefabs are skipped via
        /// <c>scene.IsValid()</c>.</summary>
        private static void CollectNamedObject(List<string> lines, string name)
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            var found = new List<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null || go.name != name || !go.scene.IsValid())
                    continue;
                found.Add(go);
            }
            found.Sort((a, b) => string.CompareOrdinal(
                PathOf(a.transform), PathOf(b.transform)));

            if (found.Count == 0)
            {
                lines.Add("obj '" + name + "': not found");
                return;
            }
            int cap = Math.Min(found.Count, MaxNamedObjectLines);
            for (int i = 0; i < cap; i++)
            {
                GameObject go = found[i];
                lines.Add("obj '" + name + "' layer=" + go.layer
                    + " active=" + go.activeInHierarchy
                    + " path=" + PathOf(go.transform));
            }
            if (found.Count > cap)
                lines.Add("  ... (+" + (found.Count - cap) + " more '" + name + "')");
        }

        private static string PathOf(Transform t)
        {
            if (t == null)
                return "?";
            string path = t.name;
            Transform parent = t.parent;
            int guard = 0;
            while (parent != null && guard < 8)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
                guard++;
            }
            return path;
        }
    }
}
