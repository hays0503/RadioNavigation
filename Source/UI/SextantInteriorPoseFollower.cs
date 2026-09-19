using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Applies the PIP camera's world rotation to the interior clone after the
    /// VirtualSceneRenderer's LateUpdate (Route B). The VSR only writes the
    /// clone's localPosition, so without this the clone keeps the interior
    /// camera's fixed orientation and the alidade would not move the ocular.
    /// Runs late ([DefaultExecutionOrder]) so it wins over the VSR.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class SextantInteriorPoseFollower : MonoBehaviour
    {
        public Transform Source;
        public Transform Clone;

        private void LateUpdate()
        {
            if (Source == null || Clone == null)
                return;
            if (!SextantController.IsOpen)
                return;
            Clone.rotation = Source.rotation;
        }
    }
}
