using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Camera-local source references, also readable by name across
    /// the game's two mod assembly copies. Never installed on Main Camera.</summary>
    internal sealed class SextantCelestialSources : MonoBehaviour
    {
        public Renderer NativeSun;
        public Renderer NativeSunFlare;
        public Renderer NativeMoon;
        public Renderer MeasuringSun;
        public Renderer MeasuringMoon;
        public int SunIsolatedFrame = -100;
    }
}
