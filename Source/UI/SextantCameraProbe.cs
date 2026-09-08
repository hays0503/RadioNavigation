using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Render probe (moved from SextantController, SRP: debug telemetry is
    /// not device logic): OnPreRender fires only when the camera actually
    /// renders, so these logs prove whether the SRP re-renders the PIP
    /// cameras every frame and with which pose (frozen forward = cached
    /// matrices).
    /// </summary>
    internal sealed class SextantCameraProbe : MonoBehaviour
    {
        public Camera tracked;
        public SextantController owner;

        private void OnPreRender()
        {
            if (tracked == null || owner == null)
                return;
            if (Time.frameCount < 10 || Time.frameCount % 60 == 0)
            {
                Vector3 f = tracked.transform.forward;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[RadioNav][Sextant] PRERENDER {0} frame={1} fwd=({2:F2},{3:F2},{4:F2}) arc={5:F2}",
                    tracked.name, Time.frameCount, f.x, f.y, f.z, owner.arcAngle));
            }
        }
    }
}
