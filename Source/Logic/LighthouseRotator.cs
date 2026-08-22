using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Rotates its GameObject around its local Y axis every frame. Attached
    /// at runtime by LighthousePlacer to the spawned prefab child named by
    /// the profile's RotatorObjectName (the lantern/beam assembly of the
    /// lighthouse model), so the parent orientation does not matter.
    /// </summary>
    public sealed class LighthouseRotator : MonoBehaviour
    {
        public float SpeedDegreesPerSecond = 18f;

        private void Update()
        {
            transform.Rotate(0f, SpeedDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
