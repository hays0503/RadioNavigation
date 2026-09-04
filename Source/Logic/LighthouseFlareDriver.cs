using System;
using System.Reflection;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Drives the native HDRPLensFlare of a spawned lighthouse (P5.4 LBG
    /// slices): discovery by type name (assembly-agnostic reflection), the
    /// runtime near-fade/element-size fixups, the unified perceptual-model
    /// intensity (FlareIntensityScale x I_visual with the 60° loom gate and
    /// the 2°/8° hot cone), the device-view distance exception, the
    /// anti-culling bounds inflation and the transition-gated ON/OFF logs.
    /// Extracted from LighthouseBeamGlow (SRP: the flare pipeline is not
    /// the visibility controller's logic). One instance per lighthouse.
    /// </summary>
    internal sealed class LighthouseFlareDriver
    {
        private const string FlareComponentTypeName = "HDRPLensFlare";
        // Native flare is replaced by the screen-space dot beyond this range:
        // the dot path obeys the model's exact pixel caps (≤10px / ≤13px).
        private const float FlareMaxDistanceM = 1200f;
        private const float FlareIntensityScale = 2f;
        // User preference: the flare reads too small through the binoculars,
        // especially up close. Base sizes were the far pixel caps from the
        // visual model spec (glow 0.010 ≈ 11px @1080p, streak 0.012×0.006 ≈
        // 13px, star 0.015); +25% on top.
        private const float FlareSizeScale = 1.25f;

        private Component lensFlare;
        private PropertyInfo intensityProperty;
        private MeshFilter meshFilter;
        private object glowElement;
        private object streakElement;
        private object starElement;
        private bool wasActive;

        /// <summary>True when the prefab carries an HDRPLensFlare.</summary>
        public bool HasFlare { get; private set; }

        /// <summary>The flare component (null when absent) — diagnostics snapshot input.</summary>
        public Component LensFlare
        {
            get { return lensFlare; }
        }

        /// <summary>The reflection Intensity property — diagnostics snapshot input.</summary>
        public PropertyInfo IntensityProperty
        {
            get { return intensityProperty; }
        }

        /// <summary>
        /// Locates the HDRPLensFlare component on the spawned prefab (by
        /// type name via reflection — works whether the component was
        /// compiled into the game or into a plugin assembly) and applies
        /// the runtime fixups (near fade, scaled element sizes).
        /// </summary>
        public void Initialize(Transform rotator)
        {
            FindLensFlare(rotator);
            if (lensFlare.IsNotNull())
            {
                meshFilter = lensFlare.GetComponent<MeshFilter>();
                try
                {
                    var flareType = lensFlare.GetType();
                    var nearStart = flareType.GetField("NearFadeStartDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nearStart != null) nearStart.SetValue(lensFlare, 70f);
                    var nearEnd = flareType.GetField("NearFadeEndDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nearEnd != null) nearEnd.SetValue(lensFlare, 200f);
                    var flaresField = flareType.GetField("Flares", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (flaresField != null)
                    {
                        var flares = flaresField.GetValue(lensFlare) as Array;
                        if (flares != null && flares.Length > 2)
                        {
                            glowElement = flares.GetValue(0);
                            streakElement = flares.GetValue(1);
                            starElement = flares.GetValue(2);
                            ApplyElementSizes();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RadioNav][Lighthouse] flare runtime fix failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Per-frame flare update. All frame inputs arrive bundled in a
        /// single FlareFrame (SRP gate FlareDriverUpdate_TakesFlareFrame).
        /// </summary>
        public void Update(FlareFrame frame)
        {
            if (lensFlare.IsNull())
            {
                return;
            }

            GameObject flareObject = lensFlare.gameObject;
            if (!frame.isNight || frame.renderingCamera == null || frame.rotator.IsNull())
            {
                Disable(flareObject, WillLog(flareObject, false)
                    ? "night=" + frame.isNight + " cam=" + (frame.renderingCamera.IsNotNull()) + " rot=" + frame.rotator.IsNotNull()
                    : null, frame.renderingCamera, frame.worldPositionProvider);
                return;
            }

            float distance = (frame.renderingCamera.transform.position - frame.worldPosition).magnitude;
            // In the device view (periscope/binocular/UZO) the flare is the
            // only working renderer — the game itself has no distance limit
            // for its lighthouse flare (LighthouseLight.cs), and the
            // screen-space dot cannot be positioned there.
            if (!frame.deviceView && distance > FlareMaxDistanceM)
            {
                Disable(flareObject, WillLog(flareObject, false)
                    ? "beyond " + FlareMaxDistanceM.ToString("F0") +
                      "m (dist=" + distance.ToString("F0") + ")"
                    : null, frame.renderingCamera, frame.worldPositionProvider);
                return;
            }
            if (frame.deviceView && distance > FlareMaxDistanceM && frame.now >= frame.nextDiagTime)
            {
                Debug.Log("[RadioNav][Lighthouse] flare: device view keeps flare active beyond " +
                    FlareMaxDistanceM.ToString("F0") + "m (dist=" + distance.ToString("F0") +
                    " cam='" + frame.renderingCamera.name + "')");
            }

            // B_wide: same 60° loom window as the screen-space dot.
            if (frame.wideGate <= 0.01f)
            {
                Disable(flareObject, WillLog(flareObject, false)
                    ? "beam not aligned (wide=" + frame.wideGate.ToString("F2") + ")"
                    : null, frame.renderingCamera, frame.worldPositionProvider);
                return;
            }

            SetFlareActive(flareObject, true, WillLog(flareObject, true)
                ? "aligned wide=" + frame.wideGate.ToString("F2")
                : null, frame.renderingCamera, frame.worldPositionProvider);
            ApplyIntensity(flareObject, frame.renderingCamera, frame.rotator, distance, frame.wideGate, frame.verbose, frame.nextDiagTime);
        }

        /// <summary>True while the flare GameObject is active in hierarchy.</summary>
        public bool RenderingNow()
        {
            if (!HasFlare || lensFlare.IsNull() || !lensFlare.gameObject.activeInHierarchy)
            {
                return false;
            }
            MeshRenderer renderer = lensFlare.GetComponent<MeshRenderer>();
            return renderer.IsNotNull() && renderer.enabled && renderer.isVisible;
        }

        /// <summary>
        /// The HDRPLensFlare component rebuilds the flare mesh with bounds
        /// equal to OcclusionRadius every time it is enabled. Those small
        /// bounds get frustum-culled when the lighthouse is beyond the
        /// camera's far clip plane. Inflating the bounds makes Unity keep
        /// the flare renderer; the shader then keeps the quad inside the
        /// depth range (_InfinitelyFar), so the light stays visible from
        /// any distance in every camera mode.
        /// </summary>
        public void KeepInView()
        {
            if (meshFilter.IsNull() || !lensFlare.gameObject.activeInHierarchy)
            {
                return;
            }
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh.IsNull())
            {
                return;
            }
            Vector3 extents = new Vector3(1e9f, 1e9f, 1e9f);
            if (mesh.bounds.extents != extents)
            {
                mesh.bounds = new Bounds(Vector3.zero, extents * 2f);
                Debug.Log("[RadioNav][Lighthouse] glow: flare bounds inflated (HDRPLensFlare rebuilds them on enable)");
            }
        }

        private void FindLensFlare(Transform rotator)
        {
            if (rotator.IsNull())
                return;
            Component[] components = rotator.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component.IsNotNull() && component.GetType().Name == FlareComponentTypeName)
                {
                    lensFlare = component;
                    intensityProperty = component.GetType().GetProperty(
                        "Intensity", BindingFlags.Public | BindingFlags.Instance);
                    HasFlare = true;
                    Debug.Log("[RadioNav][Lighthouse] glow: HDRPLensFlare found on '" + component.name + "'");
                    return;
                }
            }
            HasFlare = false;
        }

        private void Disable(GameObject flareObject, string reason, Camera renderingCamera,
            Func<Vector3> worldPositionProvider)
        {
            SetFlareActive(flareObject, false, WillLog(flareObject, false) ? reason : null,
                renderingCamera, worldPositionProvider);
        }

        private bool WillLog(GameObject flareObject, bool enabled)
        {
            return flareObject.IsNotNull() && wasActive != enabled;
        }

        private void ApplyIntensity(GameObject flareObject, Camera renderingCamera, Transform rotator,
            float distance, float wideGate, bool verbose, float nextDiagTime)
        {
            if (intensityProperty.IsNull())
                return;
            Vector3 directionToCamera = (renderingCamera.transform.position - flareObject.transform.position).normalized;
            float hotAlign = Mathf.Abs(Vector3.Dot(directionToCamera, rotator.forward));
            float fogFlare = SkyVisibilityResolver.GetFogginess();
            double attenFlare = LighthouseVisualModel.DistanceAttenuation(distance);
            double transFlare = LighthouseVisualModel.Transmittance(distance, fogFlare);
            double hotFlare = LighthouseVisualModel.BeamIntensityFactor(hotAlign);
            float iVisual = (float)LighthouseVisualModel.VisualIntensity(distance, fogFlare, wideGate, hotAlign);
            float flareIntensityValue = FlareIntensityScale * iVisual;
            if (verbose && Time.unscaledTime >= nextDiagTime)
            {
                Debug.Log("[RadioNav][Lighthouse] flare model: dist=" + distance.ToString("F0") +
                    " A=" + attenFlare.ToString("F3") + " T=" + transFlare.ToString("F3") + " fog=" + fogFlare.ToString("F2") +
                    " wide=" + wideGate.ToString("F2") + " beam=" + hotFlare.ToString("F2") + " Ivis=" + iVisual.ToString("F3") + " -> flareI=" + flareIntensityValue.ToString("F3") +
                    " lampY=" + (rotator.IsNotNull() ? rotator.position.y.ToString("F1") : "NaN") + " farY=" + flareObject.transform.position.y.ToString("F1"));
            }
            intensityProperty.SetValue(lensFlare, flareIntensityValue, null);
        }

        /// <summary>
        /// Transition-only debug logging: reports flare state changes with
        /// the reason and the camera that was current at the moment of the
        /// change (periscope/binocular cameras render into a RenderTexture,
        /// so camera name matters for diagnosing visibility gaps).
        /// </summary>
        private void SetFlareActive(GameObject flareObject, bool active, string reason,
            Camera renderingCamera, Func<Vector3> worldPositionProvider)
        {
            bool previous = wasActive;
            flareObject.SetActive(active);
            wasActive = active;
            if (previous != active)
            {
                if (active)
                {
                    // HDRPLensFlare may rebuild its flare elements when
                    // enabled — re-apply the scaled sizes so the +25% holds
                    // in every enable cycle.
                    ApplyElementSizes();
                }
                string camName = renderingCamera.IsNotNull() ? renderingCamera.name : "null";
                float distance = renderingCamera.IsNotNull() && worldPositionProvider != null
                    ? (renderingCamera.transform.position - worldPositionProvider()).magnitude
                    : -1f;
                Debug.Log("[RadioNav][Lighthouse] glow: flare " + (active ? "ON" : "OFF") +
                    (string.IsNullOrEmpty(reason) ? "" : " [" + reason + "]") +
                    " cam='" + camName + "' dist=" + distance.ToString("F0"));
            }
        }

        /// <summary>
        /// Screen-space flare element sizes (fractions of screen height),
        /// scaled by FlareSizeScale (+25% user preference). Applied at
        /// Initialize and re-applied on every flare enable transition.
        /// </summary>
        private void ApplyElementSizes()
        {
            if (glowElement != null)
            {
                var gf = glowElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (gf != null) gf.SetValue(glowElement, new Vector2(0.010f * FlareSizeScale, 0.010f * FlareSizeScale));
            }
            if (streakElement != null)
            {
                var sf = streakElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null) sf.SetValue(streakElement, new Vector2(0.012f * FlareSizeScale, 0.006f * FlareSizeScale));
            }
            if (starElement != null)
            {
                var stf = starElement.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                if (stf != null) stf.SetValue(starElement, new Vector2(0.015f * FlareSizeScale, 0.015f * FlareSizeScale));
            }
        }
    }
}
