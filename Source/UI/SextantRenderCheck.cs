using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// RT pixel probe for the sextant PIP cameras: once ~1 s after open,
    /// and once more ~1 s after the alidade first passes 20 deg - proves
    /// the cameras render and that the right RT content follows the pose
    /// (frozen = same colors). Extracted from SextantController (SRP:
    /// diagnostics is not device logic). Owns its probe texture and
    /// state machine; the controller feeds delta time and the arc.
    /// </summary>
    internal sealed class SextantRenderCheck
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        private float timer = 1.0f;
        private bool done;
        private bool secondPending;
        private bool secondDone;
        private Texture2D probeTex;

        /// <summary>Advances the probe state machine; call from Update.</summary>
        public void Update(float dt, double arcAngle, RenderTexture leftRt, RenderTexture rightRt)
        {
            if (done && !secondPending)
                return;

            if (!done)
            {
                timer -= dt;
                if (timer > 0f)
                    return;
                done = true;
                Probe("open", leftRt, rightRt);
                return;
            }

            if (arcAngle > 20.0 && !secondPending && !secondDone)
            {
                secondPending = true;
                timer = 1.0f;
            }
            if (secondPending)
            {
                timer -= dt;
                if (timer > 0f)
                    return;
                secondPending = false;
                secondDone = true;
                Probe("arc20", leftRt, rightRt);
            }
        }

        /// <summary>Resets the probe for a fresh open (first check re-runs).</summary>
        public void ResetForOpen()
        {
            done = false;
            secondPending = false;
            timer = 1.0f;
        }

        private void Probe(string tag, RenderTexture leftRt, RenderTexture rightRt)
        {
            if (leftRt == null || rightRt == null || !leftRt.IsCreated() || !rightRt.IsCreated())
            {
                Debug.LogWarning(LogPrefix + " RENDERCHECK rt missing");
                return;
            }
            if (probeTex == null)
                probeTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture prev = RenderTexture.active;
                Color lt = SampleRt(leftRt, 0.9f);
                Color lm = SampleRt(leftRt, 0.5f);
                Color lb = SampleRt(leftRt, 0.1f);
                Color rtTop = SampleRt(rightRt, 0.9f);
                Color rm = SampleRt(rightRt, 0.5f);
                Color rb = SampleRt(rightRt, 0.1f);
                RenderTexture.active = prev;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} RENDERCHECK {1} left(t/m/b)=#{2}({3:F2})/#{4}({5:F2})/#{6}({7:F2}) right(t/m/b)=#{8}({9:F2})/#{10}({11:F2})/#{12}({13:F2})",
                    LogPrefix, tag,
                    ColorUtility.ToHtmlStringRGB(lt), lt.a,
                    ColorUtility.ToHtmlStringRGB(lm), lm.a,
                    ColorUtility.ToHtmlStringRGB(lb), lb.a,
                    ColorUtility.ToHtmlStringRGB(rtTop), rtTop.a,
                    ColorUtility.ToHtmlStringRGB(rm), rm.a,
                    ColorUtility.ToHtmlStringRGB(rb), rb.a));
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " RENDERCHECK failed: " + e.Message);
            }
        }

        private Color SampleRt(RenderTexture target, float v)
        {
            RenderTexture.active = target;
            probeTex.ReadPixels(new Rect(target.width / 2, (int)(target.height * v), 1, 1), 0, 0);
            return probeTex.GetPixel(0, 0);
        }

        /// <summary>Releases the probe texture (controller teardown).</summary>
        public void Destroy()
        {
            if (probeTex != null)
                UnityEngine.Object.Destroy(probeTex);
            probeTex = null;
        }
    }
}
