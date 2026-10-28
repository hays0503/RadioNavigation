using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>One synchronous camera render's reversible native-disc state.
    /// Restore exactly what was changed; a disabled native body stays disabled.
    /// A value token also handles nested renders without a shared 'old' state.</summary>
    internal struct CelestialRenderState
    {
        private Renderer sun;
        private Renderer moon;
        private Renderer sunFlare;

        internal static CelestialRenderState Begin(Renderer nativeSun, bool replaceSun,
            Renderer nativeMoon, bool replaceMoon, Renderer nativeSunFlare = null)
        {
            CelestialRenderState state = new CelestialRenderState();
            try
            {
                Suppress(nativeSun, replaceSun, ref state.sun);
                Suppress(nativeMoon, replaceMoon, ref state.moon);
                // The Sun's HDRPLensFlare is a separate child renderer. It is
                // drawn additively AFTER PPv2, so shading the scene or hiding
                // only the native disc cannot stop it (GPU 2026-10-13).
                Suppress(nativeSunFlare, replaceSun, ref state.sunFlare);
                return state;
            }
            catch
            {
                // The caller has not received the token yet. If the second
                // renderer setter fails, restore the first before rethrowing.
                state.Restore();
                throw;
            }
        }

        private static void Suppress(Renderer native, bool replace, ref Renderer restore)
        {
            // Unity's null operator also rejects a destroyed native object.
            if (!replace || native == null)
                return;
            if (!native.enabled)
                return;
            restore = native;
            native.enabled = false;
        }

        internal void Restore()
        {
            if (sun != null)
                sun.enabled = true;
            if (moon != null)
                moon.enabled = true;
            if (sunFlare != null)
                sunFlare.enabled = true;
            sun = null;
            moon = null;
            sunFlare = null;
        }
    }
}
