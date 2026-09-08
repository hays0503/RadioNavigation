using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PPv2 bloom-cut volumes for the sextant PIP cameras: one volume per
    /// camera on a dedicated layer present ONLY in that camera's
    /// volumeLayer mask, so each shade filter disables its own half's sun
    /// glow independently (the main camera never consumes those layers -
    /// real filter look). Extracted from SextantController (SRP: PPv2
    /// volume wiring is not device logic).
    /// </summary>
    internal sealed class SextantBloomCut
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const int LayerCandidateLeft = 7;
        private const int LayerCandidateRight = 6;

        private GameObject leftGo;
        private GameObject rightGo;
        private bool logged;

        /// <summary>Creates (once) the volume for one PIP half and adds its
        /// layer to that camera's volumeLayer mask.</summary>
        public void EnsureVolume(GameObject pipGo, bool rightSide)
        {
            try
            {
                PostProcessLayer pipLayer = pipGo.GetComponent<PostProcessLayer>();
                if (pipLayer == null)
                    return;

                GameObject volumeGo = rightSide ? rightGo : leftGo;
                if (volumeGo == null)
                {
                    int layer = rightSide ? LayerCandidateRight : LayerCandidateLeft;
                    volumeGo = new GameObject(rightSide ? "RadioNavSextantBloomCutRight" : "RadioNavSextantBloomCutLeft");
                    volumeGo.layer = layer;
                    PostProcessVolume volume = volumeGo.AddComponent<PostProcessVolume>();
                    volume.isGlobal = true;
                    volume.priority = 1000f;
                    PostProcessProfile profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                    profile.name = "RadioNavSextantBloomCut";
                    Bloom bloom = profile.AddSettings<Bloom>();
                    bloom.enabled.value = false;
                    volume.sharedProfile = profile;
                    volumeGo.SetActive(false); // active only while filter>0
                    if (rightSide)
                        rightGo = volumeGo;
                    else
                        leftGo = volumeGo;
                    Debug.Log(LogPrefix + " BLOOMCUT volume " + (rightSide ? "right" : "left")
                        + " on layer " + layer + " created (hidden)");
                }

                // Layer must be in THIS PIP camera's volumeLayer mask only;
                // the main camera's mask never contains it.
                pipLayer.volumeLayer |= (1 << volumeGo.layer);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " BLOOMCUT failed: " + e.Message);
            }
        }

        /// <summary>Bloom-cut per half: each PIP camera has its own volume,
        /// active only while its filter count is positive.</summary>
        public void Update(bool open, int leftFilters, int rightFilters)
        {
            if (rightGo != null)
            {
                bool rightOn = open && rightFilters > 0;
                rightGo.SetActive(rightOn);
                Debug.Log(LogPrefix + " BLOOMCUT right " + (rightOn ? "on" : "off") + " (filters=" + rightFilters + ")");
            }
            if (leftGo != null)
            {
                bool leftOn = open && leftFilters > 0;
                leftGo.SetActive(leftOn);
                Debug.Log(LogPrefix + " BLOOMCUT left " + (leftOn ? "on" : "off") + " (filters=" + leftFilters + ")");
            }
        }

        /// <summary>One-shot diagnostic: which layers/volumes ended up applied.</summary>
        public void LogAppliedOnce(Camera pipLeft, Camera pipRight, Camera main, int leftFilters, int rightFilters)
        {
            if (logged || (leftFilters <= 0 && rightFilters <= 0))
                return;
            logged = true;
            try
            {
                PostProcessLayer pipLeftLayer = pipLeft != null
                    ? pipLeft.GetComponent<PostProcessLayer>() : null;
                PostProcessLayer pipRightLayer = pipRight != null
                    ? pipRight.GetComponent<PostProcessLayer>() : null;
                PostProcessLayer mainLayer = main != null
                    ? main.GetComponent<PostProcessLayer>() : null;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} BLOOMCUT applied pipL={1} pipR={2} main={3} leftVol={4} rightVol={5}",
                    LogPrefix, pipLeftLayer != null, pipRightLayer != null,
                    mainLayer != null,
                    leftGo != null && leftGo.activeSelf,
                    rightGo != null && rightGo.activeSelf));
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantBloomCut.LogAppliedOnce"))
                    Debug.LogWarning("[RadioNav][Sextant] BLOOMCUT diag failed: " + e.Message);
            }
        }

        /// <summary>Hides both volumes (sextant closed).</summary>
        public void Hide()
        {
            if (leftGo != null)
                leftGo.SetActive(false);
            if (rightGo != null)
                rightGo.SetActive(false);
        }

        /// <summary>Destroys both volumes (controller teardown).</summary>
        public void Destroy()
        {
            if (leftGo != null)
                UnityEngine.Object.Destroy(leftGo);
            if (rightGo != null)
                UnityEngine.Object.Destroy(rightGo);
            leftGo = null;
            rightGo = null;
        }
    }
}
