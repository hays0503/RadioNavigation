using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-frame inputs for SextantGlareCoordinator.Update: everything the
    /// glare decision reads, gathered by the controller from its adapters.
    /// A struct so the handoff allocates nothing per frame.
    /// </summary>
    public struct GlareFrame
    {
        public bool Disabled;
        public Camera RightCamera;
        public bool OwnOk;
        public Vector3 OwnDir;
        public float OwnAlt;
        public float OwnAz;
        public bool GameOk;
        public Vector3 GameDir;
        public int RightFilterCount;
        public Image GlareImage;
        public Image FilterDimLeft;
        public Image FilterDimRight;
        public Text PainText;
        public Action<float> SunDriftLog;
        public Action PainStarIdReset;
    }

    /// <summary>
    /// Shade-filter glare + pain state machine behind SextantController
    /// (SRP: glare application is not device logic). Owns the pain latch,
    /// thresholds and filter-dim application; the sun-disc positioning,
    /// readout and drift-log fan out through the frame. Moved verbatim
    /// (hysteresis, dim factors, log strings). Unity-bound, so no unit
    /// tests — covered by live smoke (filters/pain/sun-disc).
    /// </summary>
    public sealed class SextantGlareCoordinator
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const float PainOnGlare = 0.6f;
        private const float PainOffGlare = 0.5f;

        // static readonly (not const): const folds away under the C# 7.0
        // game compiler and produced CS0162 unreachable-code warnings.
        internal static readonly bool GlareEnabled = true;

        private bool pain;
        private bool sunDiscSourceLogged;

        public bool IsPained => pain;

        public void ResetPain()
        {
            pain = false;
        }

        public void Update(float dt, GlareFrame frame)
        {
            if (frame.Disabled)
            {
                ApplyGlare(0f, frame.GlareImage, frame.PainText, frame.PainStarIdReset);
                ZeroDim(frame.FilterDimLeft);
                ZeroDim(frame.FilterDimRight);
                if (frame.SunDriftLog != null)
                    frame.SunDriftLog(dt);
                return;
            }

            float g = 0f;
            if (frame.RightCamera != null && frame.OwnOk)
            {
                float angle = Vector3.Angle(frame.RightCamera.transform.forward, frame.OwnDir);
                g = (float)SextantGlare.Glare(angle, frame.OwnAlt, frame.RightFilterCount, frame.RightCamera.fieldOfView);
                // Disc must cover the GAME sun sprite (SUNDRIFT sep ~1.6°),
                // so position it on the game direction when available.
                SunDiscRenderer.SunDirection = frame.GameOk ? frame.GameDir : frame.OwnDir;
                if (!sunDiscSourceLogged && SunDiscRenderer.Active)
                {
                    sunDiscSourceLogged = true;
                    float sep = frame.GameOk ? Vector3.Angle(frame.GameDir, frame.OwnDir) : -1f;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "{0} SUNDISC src={1} sep={2:F3} own(alt={3:F2} az={4:F1}) game={5}",
                        LogPrefix, frame.GameOk ? "game" : "own", sep, frame.OwnAlt, frame.OwnAz,
                        frame.GameOk ? SextantVectorLog.V3(frame.GameDir) : " n/a"));
                }
            }
            ApplyGlare(g, frame.GlareImage, frame.PainText, frame.PainStarIdReset);
            if (frame.SunDriftLog != null)
                frame.SunDriftLog(dt);
        }

        public void ApplyDim(Image left, Image right, int leftCount, int rightCount, bool glareEnabled)
        {
            if (!glareEnabled)
                return;
            if (left.IsNotNull())
            {
                Color c = left.color;
                c.a = SextantReadout.FilterDimAlpha(leftCount);
                left.color = c;
            }
            if (right.IsNotNull())
            {
                Color c = right.color;
                c.a = SextantReadout.FilterDimAlpha(rightCount);
                right.color = c;
            }
        }

        private static void ZeroDim(Image image)
        {
            if (image.IsNotNull())
            {
                Color c = image.color;
                c.a = 0f;
                image.color = c;
            }
        }

        private void ApplyGlare(float g, Image glareImage, Text painText, Action painStarIdReset)
        {
            if (glareImage.IsNotNull())
            {
                Color c = glareImage.color;
                c.a = g * 0.9f;
                glareImage.color = c;
            }
            if (!pain && g > PainOnGlare)
            {
                pain = true;
                if (painStarIdReset != null)
                    painStarIdReset();
                if (painText.IsNotNull()) painText.enabled = true;
                // White flash: the lookout's dark adaptation is gone (P4).
                StarsMesh.NotifyWhiteFlash();
            }
            else if (pain && g < PainOffGlare)
            {
                pain = false;
                if (painText.IsNotNull()) painText.enabled = false;
            }
        }
    }
}
