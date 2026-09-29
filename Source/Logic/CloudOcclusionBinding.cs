using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Discovers textures for the legacy, disabled-by-default cloud-mask
    /// experiment. Production occlusion is the PointSky draw occurring before
    /// UniStorm's own cloud composite. Two historical sources are resolved:
    ///
    ///  - <c>fullCloudBuff0</c>/<c>fullCloudBuff1</c> — UniStorm's ping-pong
    ///    cloud dome (modes 1/2). Its direction-to-UV mapping is not calibrated;
    ///    the old guesses darkened broad parts of the sky and remain diagnostic.
    ///  - <c>GBuffer3…</c> — historical mode-3 target. RenderDoc 2026-09-30
    ///    showed that PointSky draws before the cloud composite and that the
    ///    observed cloud pass uses Zero/Zero alpha blend factors. Post-composite
    ///    GBuffer3 alpha is therefore not a cloud-opacity mask.
    ///
    /// <see cref="Apply"/> is called from the five sky renderers every frame,
    /// but the expensive <see cref="Resources.FindObjectsOfTypeAll{T}"/> scan
    /// only runs on the retry schedule (~5 s) until the texture appears; after
    /// that it is cached. Everything is a no-op while
    /// <see cref="CloudOcclusionSwitch.Mode"/> is 0, so the default path is
    /// untouched.
    /// </summary>
    internal static class CloudOcclusionBinding
    {
        private const string PreferredName = "fullCloudBuff0";
        private const string NameFragment = "fullCloudBuff";
        private const string GBufferName = "GBuffer3";
        private const float RetryIntervalSeconds = 5f;
        private const string LogPrefix = "[RadioNav][CloudOcclusion] ";

        private static RenderTexture cachedCloud;
        private static RenderTexture cachedGBuffer;
        private static float nextRetryRealtime;
        private static bool cloudFoundLogged;
        private static bool cloudMissingLogged;
        private static bool gbufferFoundLogged;
        private static bool gbufferMissingLogged;
        private static bool inventoryLogged;

        /// <summary>
        /// Pushes the texture selected by the live mode and the mode/strength
        /// values into a material. No-op while the mode is 0 or the texture has
        /// not been found yet (a stale <c>_CloudOcclusionTex</c> must never be
        /// paired with the wrong mode); each property is written only when the
        /// material declares it (<see cref="Material.HasProperty"/>), so
        /// materials without the occlusion block stay byte-for-byte untouched.
        /// </summary>
        internal static void Apply(Material material)
        {
            if (material == null || CloudOcclusionSwitch.Mode <= 0)
                return;

            RenderTexture rt = CloudOcclusionSwitch.Mode >= 3
                ? ResolveGBufferTexture()
                : ResolveCloudTexture();
            if (rt == null)
                return;

            if (material.HasProperty("_CloudOcclusionTex"))
                material.SetTexture("_CloudOcclusionTex", rt);
            if (material.HasProperty("_CloudOcclusionMode"))
                material.SetFloat("_CloudOcclusionMode", CloudOcclusionSwitch.Mode);
            if (material.HasProperty("_CloudOcclusionStrength"))
                material.SetFloat("_CloudOcclusionStrength",
                    Mathf.Clamp01(CloudOcclusionSwitch.Strength));
        }

        /// <summary>
        /// Dome map for modes 1/2. Cached lookup with a ~5 s retry schedule.
        /// Prefers the exact <c>fullCloudBuff0</c> name, falls back to any name
        /// containing <c>fullCloudBuff</c>. Logs once on success
        /// (name/size/format) and once on the first miss.
        /// </summary>
        private static RenderTexture ResolveCloudTexture()
        {
            if (cachedCloud != null)
                return cachedCloud;

            if (!Scan())
                return null;

            if (cachedCloud != null)
            {
                if (!cloudFoundLogged)
                {
                    cloudFoundLogged = true;
                    Debug.Log(LogPrefix + "cloud dome texture found: " + Describe(cachedCloud));
                }
                return cachedCloud;
            }

            if (!cloudMissingLogged)
            {
                cloudMissingLogged = true;
                Debug.Log(LogPrefix + "cloud texture '" + NameFragment
                    + "' not found yet; retrying every " + RetryIntervalSeconds + " s");
            }
            return null;
        }

        /// <summary>
        /// Historical mode-3 screen-space target. It is retained only to
        /// diagnose old experiments; its post-composite alpha is not cloud
        /// opacity. Same ~5 s retry schedule as the dome map.
        /// </summary>
        private static RenderTexture ResolveGBufferTexture()
        {
            if (cachedGBuffer != null)
            {
                LogGBufferFound();
                return cachedGBuffer;
            }

            if (!Scan())
                return null;

            if (cachedGBuffer != null)
            {
                LogGBufferFound();
                return cachedGBuffer;
            }

            if (!gbufferMissingLogged)
            {
                gbufferMissingLogged = true;
                LogInventory();
                Debug.Log(LogPrefix + "screen-space '" + GBufferName
                    + "' target not found yet; retrying every " + RetryIntervalSeconds + " s");
            }
            return null;
        }

        private static void LogGBufferFound()
        {
            if (gbufferFoundLogged)
                return;
            gbufferFoundLogged = true;
            LogInventory();
            Debug.Log(LogPrefix + "screen-space " + GBufferName + " target chosen: "
                + Describe(cachedGBuffer));
        }

        private static string Describe(RenderTexture rt)
        {
            return rt.name + " " + rt.width + "x" + rt.height + " " + rt.format;
        }

        /// <summary>
        /// Runs at most one <see cref="Resources.FindObjectsOfTypeAll{T}"/>
        /// scan per retry interval and fills both caches. Returns false while
        /// inside the retry window (nothing was scanned, callers stay quiet).
        /// </summary>
        private static bool Scan()
        {
            if (!RetryDue())
                return false;

            RenderTexture exactCloud = null;
            RenderTexture fragment = null;
            RenderTexture gbufferExact = null;
            RenderTexture gbuffer = null;
            RenderTexture[] all = Resources.FindObjectsOfTypeAll<RenderTexture>();
            FindMatches(all, PreferredName, NameFragment, ref exactCloud, ref fragment);
            FindMatches(all, GBufferName, GBufferName, ref gbufferExact, ref gbuffer);

            if (cachedCloud == null)
                cachedCloud = exactCloud != null ? exactCloud : fragment;
            if (cachedGBuffer == null)
                cachedGBuffer = gbufferExact != null ? gbufferExact : gbuffer;
            return true;
        }

        /// <summary>
        /// One pass over the loaded RenderTextures: records the first name
        /// exactly equal to <paramref name="exactName"/> (preferred) and the
        /// first containing <paramref name="fragmentName"/>.
        /// </summary>
        private static void FindMatches(RenderTexture[] all, string exactName, string fragmentName,
            ref RenderTexture exact, ref RenderTexture partial)
        {
            for (int i = 0; i < all.Length; i++)
            {
                RenderTexture rt = all[i];
                if (rt == null)
                    continue;
                string n = rt.name;
                if (n == null)
                    continue;
                if (exact == null && n == exactName)
                {
                    exact = rt;
                    continue;
                }
                if (partial == null && n.IndexOf(fragmentName, System.StringComparison.Ordinal) >= 0)
                    partial = rt;
            }
        }

        private static bool RetryDue()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextRetryRealtime)
                return false;
            nextRetryRealtime = now + RetryIntervalSeconds;
            return true;
        }

        /// <summary>
        /// One-shot evidence dump: every loaded RenderTexture with its name,
        /// size and format, so the mode-3 target choice can be judged from the
        /// log rather than assumed.
        /// </summary>
        private static void LogInventory()
        {
            if (inventoryLogged)
                return;
            inventoryLogged = true;

            RenderTexture[] all = Resources.FindObjectsOfTypeAll<RenderTexture>();
            var sb = new StringBuilder();
            sb.Append(LogPrefix).Append("RenderTexture inventory (")
              .Append(all.Length).Append(" loaded; name size format):");
            for (int i = 0; i < all.Length; i++)
            {
                RenderTexture rt = all[i];
                if (rt == null)
                    continue;
                sb.Append("\n  ").Append(rt.name).Append(' ')
                  .Append(rt.width).Append('x').Append(rt.height).Append(' ')
                  .Append(rt.format);
            }
            Debug.Log(sb.ToString());
        }
    }
}
