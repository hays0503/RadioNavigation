using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    public class VolumeSpinnerHandler : SpinnerHandlerBase
    {
        protected override string DebugTag => "VolumeSpinner";

        protected override void OnAngleUpdated(float angle)
        {
#if DEBUG
            Debug.Log("[TestUI][" + DebugTag + "] angle=" + angle);
#endif
        }
    }
}
