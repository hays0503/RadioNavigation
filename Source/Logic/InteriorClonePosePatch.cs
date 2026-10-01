using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Keeps the interior clone camera of each sextant PIP half aimed where the
    /// PIP camera of that half is aimed (author report 2026-09-30: "when I turn
    /// the sextant on, the PIP looks to the right, not along my gaze").
    ///
    /// ROOT CAUSE (measured live, all numbers are transform eulerAngles in the
    /// same paused frame):
    ///   main camera          (40.95, 236.47, 0.00)   <- the gaze
    ///   RadioNavSextantRightCamera
    ///                          (40.95, 236.47, 0.00)   <- correct already
    ///   ...RightCameraInteriorClone
    ///                          (39.25, 183.48, 358.85) <- 53 deg adrift
    ///   Interior Camera (game)
    ///                          (39.2, 183.48, ...)
    /// Inside the boat the ocular image is drawn by a per-half CLONE camera, not
    /// by the PIP camera itself: the SRP composites the interior VirtualScene
    /// through each PIP camera's own VirtualSceneRenderer, which drives the
    /// clone's transform from the GAME's Interior Camera in its LateUpdate. That
    /// camera is positioned with the player but its rotation does not carry the
    /// full look rotation, so the gap was a constant 49-53 deg (it tracked the
    /// player's position changes: 177.40 -> 183.51 together with 230 -> 233).
    ///
    /// The mod already had SextantInteriorPoseFollower (LateUpdate,
    /// DefaultExecutionOrder 10000) to correct this, and it was inert: calling
    /// its LateUpdate by hand did not move the clone. Rather than depend on
    /// script execution order against an engine component whose order is
    /// unknown, the correction is a Harmony POSTFIX on the writer itself - it
    /// runs immediately after VirtualSceneRenderer.LateUpdate has written the
    /// pose and before the frame is rendered, which is the only ordering that
    /// does not depend on the engine's ordering. Same technique as
    /// GameStarLayerSuppressor (postfix on TOD_Sky.LateUpdate).
    ///
    /// Anchoring is by COMPONENT TYPE NAME, never by object name and never
    /// through mod statics: a stale second copy of this assembly (the documented
    /// double-load shim/loader case) would otherwise read another copy's
    /// SextantController.Instance and silently no-op. The clone is recognised by
    /// carrying a "SextantInteriorPoseFollower", the PIP camera by carrying a
    /// "SextantPipInteriorPose".
    ///
    /// The target type lives in com.uboat.rendering, which the mod resolves by
    /// name like PipCloneCameraPatch does; a miss is non-fatal and logged.
    /// </summary>
    internal static class InteriorClonePosePatch
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const string VsrTypeName = "UBOAT.Game.Scene.Effects.VirtualSceneRenderer";
        private const string RenderingAssembly = "com.uboat.rendering";
        private const string VirtualCameraProperty = "VirtualCamera";
        private const string CloneMarkerTypeName = "SextantInteriorPoseFollower";
        private const string PipPoseTypeName = "SextantPipInteriorPose";

        /// <summary>Test-only A/B gate (same pattern as PipCloneCameraPatch):
        /// 0 leaves the clone on the engine's pose, 1 keeps the ocular on the
        /// gaze. Default ON.</summary>
        internal const string EnabledPrefsKey = "RadioNav.InteriorClonePose";

        private static MemberInfo virtualCameraMember;
        private static bool resolveFailedLogged;
        private static float logTimer;

        // Positive firing evidence: the __0 bug taught us that "no errors" is
        // not evidence - these counters are, readable live by reflection.
        private static int lateUpdateCalls;
        private static int appliedCount;
        private static int skipDisabledGate;
        private static int skipNoPipCamera;
        private static int skipNoClone;

        /// <summary>How many VirtualSceneRenderer.LateUpdate calls we saw.</summary>
        internal static int LateUpdateCalls
        {
            get { return lateUpdateCalls; }
        }

        /// <summary>How many frames the clone was actually re-aimed.</summary>
        internal static int AppliedCount
        {
            get { return appliedCount; }
        }

        /// <summary>Calls skipped by the PlayerPrefs A/B gate.</summary>
        internal static int SkipDisabledGate
        {
            get { return skipDisabledGate; }
        }

        /// <summary>Calls whose renderer is not on one of our PIP cameras.</summary>
        internal static int SkipNoPipCamera
        {
            get { return skipNoPipCamera; }
        }

        /// <summary>Calls whose VirtualCamera is not our interior clone.</summary>
        internal static int SkipNoClone
        {
            get { return skipNoClone; }
        }

        /// <summary>True when the postfix is installed.</summary>
        internal static bool Applied { get; private set; }

        /// <summary>
        /// Resolves VirtualSceneRenderer.LateUpdate and registers the postfix.
        /// Returns true when the patch was applied.
        /// </summary>
        public static bool Apply(Harmony harmony)
        {
            MethodInfo target = ResolveLateUpdate();
            if (target == null)
                return false;

            MethodInfo postfix = typeof(InteriorClonePosePatch).GetMethod(
                "ReaimInteriorClone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null)
                return false;

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Applied = true;
                Debug.Log(LogPrefix + " Patched " + VsrTypeName + ".LateUpdate (interior clone aim)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " interior clone aim patch failed: " + e.Message);
                return false;
            }
        }

        private static MethodInfo ResolveLateUpdate()
        {
            Type vsr = GameTypeCache.Resolve(VsrTypeName, RenderingAssembly);
            if (vsr == null)
            {
                LogResolveFailure("VirtualSceneRenderer type not found");
                return null;
            }
            MethodInfo method = vsr.GetMethod("LateUpdate",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                LogResolveFailure("VirtualSceneRenderer.LateUpdate not found");
            return method;
        }

        private static void LogResolveFailure(string what)
        {
            if (resolveFailedLogged)
                return;
            resolveFailedLogged = true;
            Debug.LogWarning(LogPrefix + " interior clone aim correction disabled: " + what
                + " (ocular will keep the engine's interior-camera pose inside the boat)");
        }

        /// <summary>
        /// Postfix: the renderer has just written the clone's transform from the
        /// game's Interior Camera. Put back the PIP camera's rotation, which is
        /// the rotation the alidade/observer chose. The position stays the
        /// engine's - that part is correct and comes from the same write.
        /// </summary>
        [HarmonyPostfix]
        public static void ReaimInteriorClone(Component __instance)
        {
            if (__instance == null)
                return;
            lateUpdateCalls++;
            if (PlayerPrefs.GetInt(EnabledPrefsKey, 1) == 0)
            {
                skipDisabledGate++;
                return;
            }

            Camera pip = PipCameraOf(__instance);
            if (pip == null)
            {
                skipNoPipCamera++;
                return;
            }
            Camera clone = CloneOf(__instance);
            if (clone == null)
            {
                skipNoClone++;
                return;
            }

            try
            {
                clone.transform.rotation = pip.transform.rotation;
                appliedCount++;
                LogRateLimited(pip, clone);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("InteriorClonePosePatch.Reaim"))
                    Debug.LogWarning(LogPrefix + " interior clone re-aim failed: " + e.Message);
            }
        }

        private static Camera PipCameraOf(Component vsr)
        {
            Camera camera = vsr.GetComponent<Camera>();
            if (camera == null)
                return null;
            return CarriesComponentNamed(camera.gameObject, PipPoseTypeName) ? camera : null;
        }

        private static Camera CloneOf(Component vsr)
        {
            if (virtualCameraMember == null)
                virtualCameraMember = TypeMemberCache.GetInstanceMember(
                    vsr.GetType(), VirtualCameraProperty);
            Camera clone;
            try
            {
                // MemberInfo has no GetValue; the concrete kinds do. Both are
                // accepted because the decompiled name may expose the virtual
                // camera as a property in one build and a field in another.
                if (virtualCameraMember is PropertyInfo property)
                    clone = property.GetValue(vsr, null) as Camera;
                else if (virtualCameraMember is FieldInfo field)
                    clone = field.GetValue(vsr) as Camera;
                else
                    clone = null;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("InteriorClonePosePatch.VirtualCamera"))
                    Debug.LogWarning(LogPrefix + " VirtualCamera read failed: " + e.Message);
                return null;
            }

            if (clone == null)
                return null;
            return CarriesComponentNamed(clone.gameObject, CloneMarkerTypeName) ? clone : null;
        }

        private static bool CarriesComponentNamed(GameObject go, string typeName)
        {
            if (go == null)
                return false;
            MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Name == typeName)
                    return true;
            }
            return false;
        }

        private static void LogRateLimited(Camera pip, Camera clone)
        {
            logTimer -= Time.unscaledDeltaTime;
            if (logTimer > 0f)
                return;
            logTimer = 1f;
            Vector3 pipEuler = pip.transform.eulerAngles;
            Vector3 cloneEuler = clone.transform.rotation.eulerAngles;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} CLONEAIM pip={1} clone={2} applied={3} lateUpdates={4} "
                + "skips(gate/pip/clone)={5}/{6}/{7}",
                LogPrefix, pipEuler, cloneEuler, appliedCount, lateUpdateCalls,
                skipDisabledGate, skipNoPipCamera, skipNoClone));
        }
    }
}
