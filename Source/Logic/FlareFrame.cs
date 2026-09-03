using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One frame of input for LighthouseFlareDriver.Update: the night state,
    /// the rendering camera, the rotator, the world position, the wide beam
    /// gate and the diagnostics throttle inputs. Bundled so the per-frame
    /// call-site contract stays a single parameter while the contents
    /// evolve freely (SRP gate FlareDriverUpdate_TakesFlareFrame).
    /// Plain data, no behavior.
    /// </summary>
    public struct FlareFrame
    {
        public bool isNight;
        public Camera renderingCamera;
        public Transform rotator;
        public Vector3 worldPosition;
        public float wideGate;
        public bool deviceView;
        public bool verbose;
        public float now;
        public float nextDiagTime;
        public Func<Vector3> worldPositionProvider;
    }
}
