using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Reads FPP walking/sprinting flags directly from the active character without allocations.</summary>
    internal static class CPlathFppMotionSampler
    {
        private static readonly int WalkingHash = Animator.StringToHash("walking");
        private static readonly int RunHash = Animator.StringToHash("Run");

        internal static void Sample(out bool walking, out bool running)
        {
            walking = false;
            running = false;
            MainCamera main = MainCamera.Instance;
            if (!(bool)main || main.CurrentMode != CameraMode.FPP)
                return;
            FPPCameraController fpp = main.FPPController;
            if (fpp == null)
                return;
            var character = fpp.Character;
            if (!(bool)character)
                return;
            var navigator = character.Navigator;
            if ((bool)navigator && navigator.IsOnOffMeshLink)
                return;
            Animator animator = character.Animator;
            if (!(bool)animator || !animator.isActiveAndEnabled)
                return;
            walking = animator.GetBool(WalkingHash);
            running = walking && animator.GetBool(RunHash);
        }
    }
}
