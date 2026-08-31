using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.Serialization;

namespace UBOAT.Mods.RadioNavigation
{
    [NonSerializedInGameState]
    public class Main : IUserMod
    {
        // DI assigns this field during mod initialization; explicit default
        // keeps standalone Roslyn builds free of CS0649.
        [Inject] private static ResourceManager resourceManager = null;
        [NonSerializedInGameState] private ModBootstrapper bootstrapper;

        public void OnLoaded()
        {
            if (bootstrapper == null)
                bootstrapper = new ModBootstrapper(resourceManager);
            bootstrapper.OnLoaded();
        }
    }
}
