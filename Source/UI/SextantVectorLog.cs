using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sextant vector telemetry (SEXTANT_PLAN diagnostics): periodic dumps
    /// of the PIP/view camera poses, the arc state and the sun vectors
    /// (own Meeus vs game TOD_Sky), plus the manual-render heartbeat.
    /// Extracted from SextantController (SRP: logging is not device
    /// logic). Owns the hold/rotate/heartbeat timers; the controller
    /// feeds it the cameras, the arc and the sun reads each call.
    /// </summary>
    internal sealed class SextantVectorLog
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        // static readonly (not const): a const bool folds away in the C# 7.0
        // game compiler and produced CS0162 unreachable-code warnings at the
        // call sites; readonly keeps the gate checks live.
        public static readonly bool Enabled = true;
        private const float HoldIntervalSeconds = 0.5f;
        private const float RotateMinDeltaDeg = 0.5f;

        private float holdTimer;
        private float rotateTimer;
        private float lastYaw;
        private float manualRenderTimer;

        private struct CameraVectors
        {
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
            public Vector3 Position;
            public Vector3 Euler;
        }

        private struct SunVectors
        {
            public bool Own;
            public Vector3 OwnDir;
            public float OwnAlt;
            public float OwnAz;
            public bool Game;
            public Vector3 GameDir;
            public float GameAlt;
        }

        /// <summary>
        /// Hold telemetry from the arc input handler: decrements the hold
        /// timer and returns true when the periodic hold dump is due
        /// (the caller logs "hold"; a fresh key press logs "press" itself
        /// and still resets the timer via this call).
        /// </summary>
        public bool OnArcInput(float dt)
        {
            holdTimer -= dt;
            if (holdTimer <= 0f)
            {
                holdTimer = HoldIntervalSeconds;
                return true;
            }
            return false;
        }

        /// <summary>Resets the hold cadence after a key press.</summary>
        public void OnArcPressed()
        {
            holdTimer = HoldIntervalSeconds;
        }

        /// <summary>Rotate telemetry: true when the view camera yaws
        /// enough since the last dump (the caller logs "rotate"). The yaw
        /// baseline tracks every call, mirroring the original
        /// ResolveViewCamera behaviour.</summary>
        public bool OnViewRotate(Camera viewCamera)
        {
            if (viewCamera == null)
                return false;
            float yaw = viewCamera.transform.eulerAngles.y;
            bool due = false;
            rotateTimer -= Time.unscaledDeltaTime;
            if (rotateTimer <= 0f &&
                Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) >= RotateMinDeltaDeg)
            {
                rotateTimer = HoldIntervalSeconds;
                due = true;
            }
            lastYaw = yaw;
            return due;
        }

        /// <summary>1 Hz MANUALRENDER heartbeat inside UpdatePipPose.</summary>
        public void OnManualRender(double arcAngle)
        {
            manualRenderTimer -= Time.unscaledDeltaTime;
            if (manualRenderTimer <= 0f)
            {
                manualRenderTimer = 1f;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} MANUALRENDER frame={1} arc={2:F2}",
                    LogPrefix, Time.frameCount, arcAngle));
            }
        }

        /// <summary>
        /// Full state dump: left camera (fixed horizon), right camera
        /// (eyes + alidade angle), the player view camera, derived angles
        /// and the sun vectors.
        /// </summary>
        public void Log(string trigger, Camera pipLeft, Camera pipRight, Camera viewCamera,
            double arcAngle,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt, float ownSunAz,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt)
        {
            var l = ReadCameraVectors(pipLeft);
            var r = ReadCameraVectors(pipRight);
            var v = ReadCameraVectors(viewCamera);
            var sun = new SunVectors
            {
                Own = ownSunOk, OwnDir = ownSunDir, OwnAlt = ownSunAlt, OwnAz = ownSunAz,
                Game = gameSunOk, GameDir = gameSunDir, GameAlt = gameSunAlt
            };
            double q = SextantArc.Quantize(arcAngle);
            float angLR = pipLeft != null && pipRight != null
                ? Vector3.Angle(l.Forward, r.Forward) : 0f;
            float dotLR = pipLeft != null && pipRight != null
                ? Vector3.Dot(l.Forward, r.Forward) : 0f;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECL {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(l.Position), V3(l.Euler), V3(l.Forward), V3(l.Up), V3(l.Right)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECR {1} pos={2} eul={3} fwd={4} up={5} rgt={6}",
                LogPrefix, trigger, V3(r.Position), V3(r.Euler), V3(r.Forward), V3(r.Up), V3(r.Right)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECV {1} pos={2} eul={3} fwd={4}",
                LogPrefix, trigger, V3(v.Position), V3(v.Euler), V3(v.Forward)));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} VECD {1} arc={2:F6} quant={3:F6} fmt={4} angLR={5:F3} dotLR={6:F6}",
                LogPrefix, trigger, arcAngle, q, SextantArc.Format(q), angLR, dotLR));
            LogSunLines(trigger, r.Forward, sun, pipRight);
        }

        /// <summary>Compact vector formatting shared with the SUNDISC log.</summary>
        public static string V3(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z);
        }

        private static CameraVectors ReadCameraVectors(Camera cam)
        {
            if (cam == null)
                return default(CameraVectors);
            return new CameraVectors
            {
                Forward = cam.transform.forward,
                Up = cam.transform.up,
                Right = cam.transform.right,
                Position = cam.transform.position,
                Euler = cam.transform.eulerAngles,
            };
        }

        private void LogSunLines(string trigger, Vector3 rightFwd, SunVectors sun, Camera pipRight)
        {
            if (sun.Own || sun.Game)
            {
                float angSun = sun.Own ? Vector3.Angle(rightFwd, sun.OwnDir) : -1f;
                float sep = sun.Own && sun.Game ? Vector3.Angle(sun.OwnDir, sun.GameDir) : -1f;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} VECSUN {1} own{2} alt={3:F2} az={4:F1} game{5} galt={6:F2} angSunR={7:F3} sep={8:F3}",
                    LogPrefix, trigger, sun.Own ? V3(sun.OwnDir) : " n/a", sun.OwnAlt, sun.OwnAz,
                    sun.Game ? V3(sun.GameDir) : " n/a", sun.GameAlt, angSun, sep));
            }
            if (sun.Own && pipRight != null)
            {
                // Sun projected into the right camera's viewport: proves the
                // rendered picture matches the astro math (center = 0.5,0.5).
                Vector3 sunNdc = pipRight.WorldToViewportPoint(pipRight.transform.position + sun.OwnDir * 1000f);
                float nx = (sunNdc.x - 0.5f) * 2f;
                float ny = (sunNdc.y - 0.5f) * 2f;
                float tanHalf = Mathf.Tan(pipRight.fieldOfView * 0.5f * Mathf.Deg2Rad);
                if (tanHalf < 0.001f) tanHalf = 0.176f;
                float dxDeg = Mathf.Atan2(nx * tanHalf, 1f) * Mathf.Rad2Deg;
                float dyDeg = Mathf.Atan2(ny * tanHalf, 1f) * Mathf.Rad2Deg;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} VECSP {1} ndc=({2:F3},{3:F3}) offDeg=({4:F2},{5:F2}) fov={6:F1}",
                    LogPrefix, trigger, sunNdc.x, sunNdc.y, dxDeg, dyDeg,
                    pipRight.fieldOfView));
            }
        }
    }
}
