using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class VolumeSpinnerHandler : SpinnerHandlerBase
    {
        protected override string DebugTag => "VolumeSpinner";

        protected override void OnAngleUpdated(float angle)
        {
            base.OnAngleUpdated(angle);
#if DEBUG
            Debug.Log("[RadioNav][" + DebugTag + "] angle=" + angle);
#endif
        }
    }
}
