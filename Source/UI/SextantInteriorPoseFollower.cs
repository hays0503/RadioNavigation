using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Fallback aim follower for the sextant's interior clone cameras.
    ///
    /// History: the clone is positioned AND rotated by the game's
    /// VirtualSceneRenderer in its own LateUpdate (measured 2026-09-30: clone
    /// euler == Interior Camera euler to 0.01 deg while the gaze was 53 deg
    /// away), so a script callback has to write the rotation afterwards.
    ///
    /// This component tried to do that from its own LateUpdate with
    /// [DefaultExecutionOrder(10000)], and it never took effect: invoking its
    /// LateUpdate by hand did not move the clone. The authoritative correction
    /// is therefore InteriorClonePosePatch, a Harmony postfix on
    /// VirtualSceneRenderer.LateUpdate itself, which cannot lose an ordering
    /// race. This component is kept as the belt-and-braces writer plus the
    /// evidence channel: CallCount/SkipCount/SkipNotOpen are readable live by
    /// reflection and settle whether the callback ever runs at all.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class SextantInteriorPoseFollower : MonoBehaviour
    {
        public Transform Source;
        public Transform Clone;

        private static int callCount;
        private static int skipCount;
        private static int skipNotOpenCount;
        private static float logTimer;

        /// <summary>LateUpdate invocations that reached the body.</summary>
        internal static int CallCount
        {
            get { return callCount; }
        }

        /// <summary>Invocations that bailed out (unwired transforms).</summary>
        internal static int SkipCount
        {
            get { return skipCount; }
        }

        /// <summary>Invocations that bailed out because the panel is closed.</summary>
        internal static int SkipNotOpenCount
        {
            get { return skipNotOpenCount; }
        }

        private void LateUpdate()
        {
            callCount++;
            if (Source == null || Clone == null)
            {
                skipCount++;
                return;
            }
            if (!SextantController.IsOpen)
            {
                skipNotOpenCount++;
                return;
            }
            Clone.rotation = Source.rotation;
            LogRateLimited();
        }

        private static void LogRateLimited()
        {
            logTimer -= Time.unscaledDeltaTime;
            if (logTimer > 0f)
                return;
            logTimer = 5f;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[RadioNav][Sextant] CLONEFOLLOW calls={0} skipUnwired={1} skipClosed={2}",
                callCount, skipCount, skipNotOpenCount));
        }
    }
}
