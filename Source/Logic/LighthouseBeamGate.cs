using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Dot/flare visibility gates behind LighthouseBeamGlow (P5.4 slice,
    /// SRP): beam cone factor, flare-takeover decision and screen bounds.
    /// The controller adapts camera/rotator vectors to plain doubles;
    /// the decisions live here and run offline in
    /// LighthouseBeamGateTests. Pure C#.
    /// </summary>
    public static class LighthouseBeamGate
    {
        // The beam is fully bright while it points at the player and fades
        // past this angle (moved from LighthouseBeamGlow).
        public const float VisibleAngleDeg = 60f;

        // Native flare is replaced by the screen-space dot beyond this range
        // in FPP (moved from LighthouseBeamGlow; mirrors
        // LighthouseFlareDriver.FlareMaxDistanceM kept there).
        public const float FlareTakeoverDistanceM = 1200f;

        /// <summary>
        /// Beam visibility factor from flat (XZ) beam-forward vs to-camera.
        /// 1 while the beam points at the player, smooth fade to 0 past
        /// VisibleAngleDeg; 1 when the camera is directly overhead.
        /// </summary>
        public static double BeamFactor(double forwardX, double forwardZ,
            double toCameraX, double toCameraZ)
        {
            double flatLenSq = toCameraX * toCameraX + toCameraZ * toCameraZ;
            if (flatLenSq < 0.0001)
                return 1.0;
            double invLen = 1.0 / Math.Sqrt(flatLenSq);
            double dot = forwardX * toCameraX * invLen + forwardZ * toCameraZ * invLen;
            if (dot > 1.0)
                dot = 1.0;
            else if (dot < -1.0)
                dot = -1.0;
            double angleDeg = Math.Acos(dot) * (180.0 / Math.PI);
            double t = 1.0 - angleDeg / VisibleAngleDeg;
            if (t > 1.0)
                t = 1.0;
            else if (t < 0.0)
                t = 0.0;
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>
        /// Mirror of the flare driver's activation decision (night, camera,
        /// distance cap, beam gate) plus far-clip culling: deterministic
        /// per frame, unlike renderer.isVisible which flickers while the
        /// bounds inflation is active.
        /// </summary>
        public static bool FlareTakesOver(bool hasFlare, bool lensReady, bool isNight,
            bool cameraReady, bool deviceView, double distanceM, double farClipM,
            double beamFactor)
        {
            if (!hasFlare || !lensReady || !isNight || !cameraReady)
                return false;
            // In the device view the flare is the only working renderer (no
            // distance limit, like the game's own lighthouse flare). In FPP
            // the dot takes over beyond FlareTakeoverDistanceM.
            if (!deviceView && distanceM > FlareTakeoverDistanceM)
                return false;
            if (beamFactor <= 0.01)
                return false;
            // Beyond the far clip the world-space flare may be culled, so
            // the screen-space dot takes over instead (cameraReady held
            // by the guard above).
            bool beyondFarClip = distanceM > farClipM * 0.9;
            return !beyondFarClip;
        }

        public static bool IsOnScreen(double x, double y, double width, double height)
        {
            return x >= 0.0 && x <= width && y >= 0.0 && y <= height;
        }
    }
}
