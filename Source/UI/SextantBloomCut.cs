using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PPv2 bloom-cut volumes for the sextant PIP cameras: one volume per
    /// camera on a dedicated layer present ONLY in that camera's
    /// volumeLayer mask (the main camera never consumes those layers, so
    /// the eyes' bloom is untouched).
    ///
    /// Always active while the sextant is open: the PIP renders 512x512,
    /// so the game's bloom (tuned for 1080p) produces halos ~2x larger in
    /// screen-relative terms around every bright lamp and - worse - NaN
    /// blocks in the mip chain from over-bright sources, which the ocular
    /// shows as hard-edged black squares (live screenshots, 2026-09-18).
    /// The shade-filter case (sun glare) is subsumed: bloom stays off for
    /// the whole open period. Extracted from SextantController (SRP: PPv2
    /// volume wiring is not device logic).
    /// </summary>
    internal sealed class SextantBloomCut
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const int LayerCandidateLeft = 7;
        private const int LayerCandidateRight = 6;

        private GameObject leftGo;
        private GameObject rightGo;
        // sharedProfile is a ScriptableObject, NOT owned by the volume
        // GameObject — it must be destroyed explicitly or it leaks per open.
        private PostProcessProfile leftProfile;
        private PostProcessProfile rightProfile;
        private bool logged;
        // Edge-only logging: Update runs per frame, the active flag rarely
        // changes (CS0169-style noise otherwise).
        private bool leftLogged;
        private bool rightLogged;

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
                    volumeGo.SetActive(false); // active while the sextant is open
                    if (rightSide)
                    {
                        rightGo = volumeGo;
                        rightProfile = profile;
                    }
                    else
                    {
                        leftGo = volumeGo;
                        leftProfile = profile;
                    }
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

        /// <summary>Bloom-cut while open: both halves lose bloom for the whole
        /// open period (halos + NaN black blocks at 512x512). Filter counts
        /// are kept for the diagnostic log only.</summary>
        public void Update(bool open, int leftFilters, int rightFilters)
        {
            if (rightGo != null)
            {
                bool rightOn = open;
                rightGo.SetActive(rightOn);
                if (rightOn != rightLogged)
                {
                    rightLogged = rightOn;
                    Debug.Log(LogPrefix + " BLOOMCUT right " + (rightOn ? "on" : "off")
                        + " (filters=" + rightFilters + ")");
                }
            }
            if (leftGo != null)
            {
                bool leftOn = open;
                leftGo.SetActive(leftOn);
                if (leftOn != leftLogged)
                {
                    leftLogged = leftOn;
                    Debug.Log(LogPrefix + " BLOOMCUT left " + (leftOn ? "on" : "off")
                        + " (filters=" + leftFilters + ")");
                }
            }
        }

        /// <summary>One-shot diagnostic: which layers/volumes ended up applied.
        /// Logs on the first open frame (bloom-cut is always-on while open, so
        /// zero filters is the normal case, not a reason to skip).</summary>
        public void LogAppliedOnce(Camera pipLeft, Camera pipRight, Camera main, int leftFilters, int rightFilters)
        {
            if (logged)
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
                    "{0} BLOOMCUT applied pipL={1} pipR={2} main={3} leftVol={4} rightVol={5} filtersL={6} filtersR={7}",
                    LogPrefix, pipLeftLayer != null, pipRightLayer != null,
                    mainLayer != null,
                    leftGo != null && leftGo.activeSelf,
                    rightGo != null && rightGo.activeSelf,
                    leftFilters, rightFilters));
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantBloomCut.LogAppliedOnce"))
                    Debug.LogWarning("[RadioNav][Sextant] BLOOMCUT diag failed: " + e.Message);
            }
        }

        /// <summary>Hides both volumes (sextant closed) and re-arms the
        /// edge logs for the next open.</summary>
        public void Hide()
        {
            if (leftGo != null)
                leftGo.SetActive(false);
            if (rightGo != null)
                rightGo.SetActive(false);
            leftLogged = false;
            rightLogged = false;
        }

        /// <summary>Destroys both volumes (controller teardown).</summary>
        public void Destroy()
        {
            if (leftGo != null)
                UnityEngine.Object.Destroy(leftGo);
            if (rightGo != null)
                UnityEngine.Object.Destroy(rightGo);
            if (leftProfile != null)
                UnityEngine.Object.Destroy(leftProfile);
            if (rightProfile != null)
                UnityEngine.Object.Destroy(rightProfile);
            leftGo = null;
            rightGo = null;
            leftProfile = null;
            rightProfile = null;
        }
    }
}
