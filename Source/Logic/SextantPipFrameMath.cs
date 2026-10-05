using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Converts a world-space camera target into the local frame used by the
    /// interior PIP. The game's FPP camera is composed as parent * local,
    /// while the interior scene camera uses that local/container orientation.
    /// Applying the same inverse-parent rotation to both beams preserves their
    /// relative alidade angle.
    /// </summary>
    public static class SextantPipFrameMath
    {
        public static Quaternion ToParentFrame(
            Quaternion parentWorldRotation, Quaternion worldRotation)
        {
            return Quaternion.Inverse(parentWorldRotation) * worldRotation;
        }
    }
}
