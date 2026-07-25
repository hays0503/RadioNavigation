using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class FeedbackSpinnerHandler : SpinnerHandlerBase
    {
        protected override string DebugTag => "FeedbackSpinner";

        protected override void OnAngleUpdated(float angle)
        {
            base.OnAngleUpdated(angle);
#if DEBUG
            Debug.Log("[RadioNav][" + DebugTag + "] angle=" + angle);
#endif
        }
    }
}
