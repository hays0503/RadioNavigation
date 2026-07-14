using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    public class FeedbackSpinnerHandler : SpinnerHandlerBase
    {
        protected override string DebugTag => "FeedbackSpinner";

        protected override void OnAngleUpdated(float angle)
        {
            Debug.Log("[TestUI][" + DebugTag + "] angle=" + angle);
        }
    }
}
