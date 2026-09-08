using System;
using PlayWay.Water;
using UBOAT.Game.Scene.Camera;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PIP camera lifecycle for the sextant ocular (P5.4 slice 4a, SRP):
    /// RenderTexture + camera creation (periscope pattern: AttachedCameras
    /// + SRP passes + WaterCamera + bloom-cut volume + render probe),
    /// main-camera water registration, and teardown. Extracted from
    /// SextantController — the controller keeps pose, input and readout.
    /// Creation order and log strings are unchanged from the move.
    /// </summary>
    public sealed class SextantPipFactory
    {
        // RT size stays with creation (was SextantController.RtWidth/Height).
        public const int RtWidth = 512;
        public const int RtHeight = 512;

        private const string LogPrefix = "[RadioNav][Sextant]";

        public Camera LeftCamera { get; private set; }

        public Camera RightCamera { get; private set; }

        public RenderTexture LeftRt { get; private set; }

        public RenderTexture RightRt { get; private set; }

        public bool WaterAttached => registeredWaterData != null;

        private UBOAT.Game.Scene.Rendering.AdditionalCameraData registeredWaterData;
        private bool waterAttachWarned;
        private SextantEffectMirror effectMirror;

        // Live source references fed to the mirror each frame.
        private Component sourceSsmsFog;
        private Component sourceSsms;

        internal void Ensure(Camera viewCamera, float fovDivisor,
            RawImage leftViewImage, RawImage rightViewImage,
            SextantEffectMirror mirror, SextantBloomCut bloomCut,
            SextantController owner)
        {
            if (LeftCamera != null && RightCamera != null)
                return;

            effectMirror = mirror;

            LeftRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            LeftRt.name = "RadioNavSextantLeftRT";
            RightRt = new RenderTexture(RtWidth, RtHeight, 32, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RightRt.name = "RadioNavSextantRightRT";

            LeftCamera = ConfigurePipCamera("RadioNavSextantLeftCamera", LeftRt,
                viewCamera, fovDivisor, false, bloomCut, owner);
            RightCamera = ConfigurePipCamera("RadioNavSextantRightCamera", RightRt,
                viewCamera, fovDivisor, true, bloomCut, owner);

            if (leftViewImage != null) leftViewImage.texture = LeftRt;
            if (rightViewImage != null) rightViewImage.texture = RightRt;

            Debug.Log(LogPrefix + " PIP cameras created, RT=" + RtWidth + "x" + RtHeight);
        }

        public int AttachedCount()
        {
            if (registeredWaterData == null || registeredWaterData.AttachedCameras == null)
                return 0;
            try
            {
                return registeredWaterData.AttachedCameras.Count;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantController.AttachedCameras"))
                    Debug.LogWarning("[RadioNav][Sextant] AttachedCameras read failed: " + e.Message);
                return 0;
            }
        }

        // Water renders itself only for cameras listed on the main camera's
        // AdditionalCameraData (periscope pattern, Periscope.cs:581).
        // Must be MainCamera, not the transient viewCamera (device probe).
        public void AttachToMain()
        {
            if (LeftCamera == null && RightCamera == null)
                return;
            MainCamera main = MainCamera.Instance;
            if (main == null)
                main = UnityEngine.Object.FindObjectOfType<MainCamera>();
            if (main == null)
                return;

            UBOAT.Game.Scene.Rendering.AdditionalCameraData data =
                main.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            if (data == null)
            {
                if (!waterAttachWarned)
                {
                    waterAttachWarned = true;
                    Debug.LogWarning(LogPrefix + " main camera lacks AdditionalCameraData - no water in PIP");
                }
                return;
            }

            registeredWaterData = data;
            // The game's list itself can be null after an update — guard the
            // deref, not just the component.
            if (data.AttachedCameras == null)
            {
                if (WarnOnce.ShouldLog("SextantController.AttachedCamerasList"))
                    Debug.LogWarning(LogPrefix + " AdditionalCameraData.AttachedCameras is null - no water in PIP");
                return;
            }
            if (LeftCamera != null)
            {
                data.AttachedCameras.Remove(LeftCamera);
                data.AttachedCameras.Add(LeftCamera);
            }
            if (RightCamera != null)
            {
                data.AttachedCameras.Remove(RightCamera);
                data.AttachedCameras.Add(RightCamera);
            }
        }

        public void Detach()
        {
            if (registeredWaterData != null && registeredWaterData.AttachedCameras != null)
            {
                if (LeftCamera != null)
                    registeredWaterData.AttachedCameras.Remove(LeftCamera);
                if (RightCamera != null)
                    registeredWaterData.AttachedCameras.Remove(RightCamera);
            }
            registeredWaterData = null;
        }

        public void Destroy()
        {
            Detach();
            if (LeftCamera != null) UnityEngine.Object.Destroy(LeftCamera.gameObject);
            if (RightCamera != null) UnityEngine.Object.Destroy(RightCamera.gameObject);
            LeftCamera = null;
            RightCamera = null;
            if (LeftRt != null)
            {
                LeftRt.Release();
                UnityEngine.Object.Destroy(LeftRt);
                LeftRt = null;
            }
            if (RightRt != null)
            {
                RightRt.Release();
                UnityEngine.Object.Destroy(RightRt);
                RightRt = null;
            }
        }

        public void RefreshMirroredState()
        {
            if (effectMirror != null)
                effectMirror.RefreshSsms(sourceSsmsFog, sourceSsms);
        }

        private Camera ConfigurePipCamera(string name, RenderTexture target,
            Camera viewCamera, float fovDivisor, bool rightSide,
            SextantBloomCut bloomCut, SextantController owner)
        {
            GameObject go = new GameObject(name);
            if (viewCamera == null)
            {
                if (WarnOnce.ShouldLog("SextantController.ConfigurePipCamera"))
                    Debug.LogWarning(LogPrefix + " viewCamera is null, cannot configure " + name);
                UnityEngine.Object.Destroy(go);
                return null;
            }
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(viewCamera);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = target;
            cam.fieldOfView = viewCamera.fieldOfView / fovDivisor;
            cam.enabled = false;
            // Sun disc layer (7): rendered only by the PIP cameras; the bit
            // is absent from every stock camera mask, so the main view
            // never sees the disc.
            cam.cullingMask |= SunDiscRenderer.SunDiscLayerMask;

            // The game's render pipeline renders water only for cameras
            // registered on the main camera's AdditionalCameraData
            // (periscope pattern, Periscope.cs:581).
            UBOAT.Game.Scene.Rendering.AdditionalCameraData pipAdd =
                go.AddComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
            pipAdd.RenderScene = true;
            SextantRenderPasses.Setup(pipAdd,
                viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>(),
                cam.cullingMask);
            try
            {
                var mainAdd = viewCamera.GetComponent<UBOAT.Game.Scene.Rendering.AdditionalCameraData>();
                if (mainAdd != null)
                    pipAdd.ShadowDistance = mainAdd.ShadowDistance;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantController.ShadowDistance"))
                    Debug.LogWarning("[RadioNav][Sextant] ShadowDistance mirror failed: " + e.Message);
            }

            // 1:1 mirror of the main camera's rendering stack: PPv2
            // PostProcessLayer + legacy PostProcessingBehaviour + SSMSGlobalFog
            // + SSMS, copied in the exact component order of the FPP camera,
            // so the SRP renders the PIP views with the same fog/post look.
            ResolveMirrorSources(viewCamera);
            effectMirror.AddMirroredEffectComponents(viewCamera, go);

            // PlayWay renders the sea only for cameras carrying a WaterCamera
            // (waterCamerasCache registry; HullEffectsRenderer.GetWaterCamera
            // null-check skips water otherwise). Pattern: EntitySelectionCamera
            // (disabled camera, manual Render(), WaterCamera in prefab).
            try
            {
                WaterCamera waterCam = go.AddComponent<WaterCamera>();
                Debug.Log(LogPrefix + " WATERCAM added to " + name + ": " + (waterCam != null));

                // A raw WaterCamera is empty: PlayWay needs the config the
                // main camera's component carries (Water reference, custom
                // water render list, enable flags). Mirror it 1:1 so the sea
                // is drawn for the PIP camera as well.
                if (waterCam != null)
                {
                    WaterCamera mainWater = viewCamera.GetComponent<WaterCamera>();
                    if (mainWater != null)
                    {
                        effectMirror.MirrorComponent(mainWater, waterCam);
                        Debug.Log(LogPrefix + " WATERCAM copied to " + name + " from main camera");
                    }
                    else
                    {
                        Debug.LogWarning(LogPrefix + " WATERCAM mirror source missing, raw component on " + name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " WATERCAM add failed: " + e.Message);
            }

            cam.depthTextureMode = viewCamera.depthTextureMode;

            // Bloom-cut: PPv2 volume on a PIP-only layer. While a filter is
            // installed the sun's glow is disabled inside that half's lens;
            // the main camera's PostProcessLayer never consumes this layer,
            // so the FPP view keeps its normal bloom.
            bloomCut.EnsureVolume(go, rightSide);

            SextantCameraProbe probe = go.AddComponent<SextantCameraProbe>();
            probe.tracked = cam;
            probe.owner = owner;

            return cam;
        }

        private void ResolveMirrorSources(Camera viewCamera)
        {
            if (sourceSsmsFog != null || viewCamera == null)
                return;
            sourceSsmsFog = FindComponent(viewCamera, "SSMSGlobalFog");
            sourceSsms = FindComponent(viewCamera, "SSMS");
        }

        private static Component FindComponent(Camera cam, string typeName)
        {
            MonoBehaviour[] comps = cam.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().Name == typeName)
                    return comps[i];
            }
            return null;
        }
    }
}
