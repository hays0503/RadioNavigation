using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Puts the PIP root camera at the game's interior camera world position
    /// after the pipeline positioned it (SEXTANT_PLAN §16.22).
    ///
    /// Why: with the clone suppressed from CollectCameras (PipCloneCameraPatch)
    /// the PIP root is the pass's single GBuffer writer, so it must render
    /// from interior space. Copying the MOD CLONE's position was tried first
    /// and diverged to garbage (live 2026-09-19: PIP at [1915, 250822, 7891]):
    /// the clone's position is computed FROM the PIP transform by
    /// VSR.LateUpdate, so clone-to-PIP copying is a feedback loop. The game's
    /// InteriorCamera instead is positioned by the game's own pipeline
    /// independently of anything the mod moves, so copying FROM it cannot
    /// feed back. Rotation is deliberately untouched: Update already set the
    /// yaw/alidade rotation on the root and nothing moves it afterwards, so
    /// copying rotation would only destroy the alidade pitch on the right eye.
    ///
    /// Ordering: the interior camera is positioned by the game's LateUpdates
    /// (default order); this runs at 10000. Next frame's SextantPipPose.Update
    /// re-seeds the root pose from the view camera, so toggling inside/outside
    /// stays clean. Outside the boat this is a no-op (Update already restored
    /// the exterior pose).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class SextantPipInteriorPose : MonoBehaviour
    {
        public Camera Pip;
        public SextantInteriorMirror Mirror;

        private void LateUpdate()
        {
            if (!SextantController.IsOpen)
                return;
            if (Pip == null || Mirror == null)
                return;
            MainCamera main = MainCamera.Instance;
            if (main?.IsInsideBoat != true)
                return;
            Camera interiorView = Mirror.InteriorViewCamera;
            if (interiorView == null)
                return;
            Pip.transform.position = interiorView.transform.position;
        }
    }
}
