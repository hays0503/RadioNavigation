using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Resolves the mod's bundled celestial shader (<c>RadioNav/SternspreizStreak</c>,
    /// asset <c>RadioNavStarField</c>) for the five sky renderers. The shader
    /// carries the pipeline passes the Deep pipeline actually draws (Point /
    /// PointForward / PointSky) plus the Sky-stage pass that lets the UniStorm
    /// cloud composite blend over the celestial sprites; the stock sprite
    /// shader has none of those passes and contributes zero scene pixels.
    ///
    /// F229: the game never registers the mod bundle in the ResourceManager, so
    /// the shader must come from <see cref="ModBootstrapper.LoadBundleAsset"/>.
    /// The bundle may not be loaded when the sky roots are created, so callers
    /// keep the StarsMesh retry ladder (bounded <c>TryUpgradeToBundledShader</c>)
    /// and re-resolve for a few frames before giving up on the fallback.
    /// </summary>
    internal static class CelestialShaderResolver
    {
        /// <summary>Bundle asset name of the celestial shader.</summary>
        internal const string BundledShaderAsset = "RadioNavStarField";

        /// <summary>Shader name of the bundled celestial shader.</summary>
        internal const string BundledShaderName = "RadioNav/SternspreizStreak";

        /// <summary>
        /// Bundled shader when the bundle is loaded, else the stock sprite
        /// shader, else the legacy additive particles shader. Callers keep the
        /// fallback only so material creation cannot crash; the retry ladder
        /// upgrades to the bundled shader once the bundle appears.
        /// </summary>
        internal static Shader ResolveShader()
        {
            return ResolveShader("Legacy Shaders/Particles/Additive");
        }

        /// <summary>
        /// Same ladder with the renderer's historical last-resort shader (used
        /// by the discs, which fell back to <c>Particles/Alpha Blended</c>).
        /// Sprites/Default is the real fallback; the last resort is defensive.
        /// </summary>
        internal static Shader ResolveShader(string lastResortShader)
        {
            Shader bundled = LoadBundled();
            if (bundled != null)
                return bundled;
            Shader fallback = Shader.Find("Sprites/Default");
            return fallback != null ? fallback : Shader.Find(lastResortShader);
        }

        /// <summary>Bundled shader only, or null while the bundle is not loaded yet.</summary>
        internal static Shader LoadBundled()
        {
            return ModBootstrapper.LoadBundleAsset(BundledShaderAsset) as Shader;
        }

        /// <summary>True when the shader is the bundled celestial one (i.e. the
        /// material carries the Sky-stage pass and can use the sky queue).</summary>
        internal static bool IsBundled(Shader shader)
        {
            return shader != null && shader.name == BundledShaderName;
        }
    }
}
