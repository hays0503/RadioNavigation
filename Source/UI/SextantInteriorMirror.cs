using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Rendering;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Route B: gives each sextant PIP camera a dedicated interior clone
    /// camera plus its own VirtualSceneRenderer, so the game's SRP
    /// (DeepPipeline.CollectCameras) composites the boat interior into the
    /// PIP render target exactly the way it does for the main camera
    /// (shared g-buffer + single lighting/blit pass). Without a
    /// VirtualSceneRenderer on the PIP camera's GameObject the SRP collects
    /// only the PIP camera, so the ocular looks through the hull (in-game
    /// finding 2026-09-16).
    ///
    /// WHY THE VSR MUST STAY (2026-09-19): a live experiment that removed the
    /// clone and rendered the interior directly on the PIP camera (interior
    /// mask + RenderScene=true) produced a BLACK ocular: the interior lives
    /// in a separate VirtualScene whose transforms are driven by the
    /// VirtualSceneRenderer (vs.Target / OverrideLocalTransformation), so
    /// without a VSR the interior geometry is not positioned and nothing
    /// lands in the PIP frustum. The VSR is required for positioning; the
    /// corruption (§16.17) comes from the clone being a SECOND GBuffer writer
    /// into the pass's single _GBufferZ, which the next fix must address
    /// without removing the VSR.
    ///
    /// The clone camera is isolated on purpose: the SRP's guard reads
    /// VirtualSceneRenderer.VirtualCamera from the rendered camera's own
    /// GameObject, and a second renderer sharing the game's interior camera
    /// would fight it in LateUpdate (transform/clip) and disable it on
    /// OnDisable. The clone is a private copy; the main camera's interior
    /// pipeline is never touched.
    ///
    /// Reflection plumbing only (game's Roslyn contract does not expose the
    /// rendering types, same reason LighthouseDiagnostics reflects
    /// MainCamera.InteriorCamera). Pure BCL + UnityEngine.
    /// </summary>
    internal sealed class SextantInteriorMirror
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const string RenderingAssembly = "com.uboat.rendering";

        private sealed class Entry
        {
            public Camera PipCamera;
            public Camera CloneCamera;
            public GameObject CloneObject;
            public Component Vsr;
            public AdditionalCameraData CloneData;
            public AdditionalCameraData PipData;
            public int LastMask = -1;
        }

        private readonly Dictionary<Camera, Entry> entries = new Dictionary<Camera, Entry>();

        // Game interior graph, resolved once per session (scene objects are
        // stable while the sextant lives; Destroy() re-resolves on reopen).
        private bool graphResolved;
        private bool graphFailed;

        // Residual-light ambient-mode mirror gate: PlayerPrefs A/B switch,
        // default 1 (mirror on).
        private const string AmbientModeMirrorPrefsKey = "RadioNav.AmbientModeMirror";
        private bool ambientMirrorLogged;
        private object interiorCameraObj;   // UBOAT.Game.Rendering.InteriorCamera
        private Camera interiorCamera;
        private AdditionalCameraData interiorData;
        private object virtualSceneObj;     // UBOAT.Game.Scene.VirtualScene
        private Transform sceneTarget;

        private static Type vsrType;

        /// <summary>
        /// Adds the interior clone + VirtualSceneRenderer for one PIP camera.
        /// MUST run before that camera's first SRP render: DeepCameraData
        /// caches the VirtualSceneRenderer in its constructor
        /// (DeepPipeline.CollectCameras IL_003D), so a component added after
        /// the first Render() is ignored forever.
        /// </summary>
        public void Attach(Camera pipCamera)
        {
            if (pipCamera == null || entries.ContainsKey(pipCamera))
                return;
            if (!ResolveGraph())
                return;

            try
            {
                Entry entry = CreateEntry(pipCamera);
                if (entry == null)
                    return;
                entries[pipCamera] = entry;
                Debug.Log(LogPrefix + " INTERIOR mirror attached to " + pipCamera.name);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantInteriorMirror.Attach"))
                    Debug.LogWarning(LogPrefix + " interior mirror attach failed: " + e.Message);
            }
        }

        /// <summary>
        /// Per-frame gate + mask sync. Inside the boat the SRP composites the
        /// interior into the PIP; outside it must keep showing open sky/sea,
        /// so the renderer component is disabled.
        ///
        /// Since §16.22 the PIP root itself is the single GBuffer writer (the
        /// clone is suppressed from CollectCameras by PipCloneCameraPatch, so
        /// it must not write): the root gets RenderScene=true here, after
        /// SextantPipFactory.SyncRenderScene mirrored the view camera's false.
        /// The culling mask + pose come from SextantPipPose / the pose follower.
        /// </summary>
        public void Sync(bool inside)
        {
            if (entries.Count == 0)
                return;

            foreach (KeyValuePair<Camera, Entry> pair in entries)
            {
                Entry e = pair.Value;
                if (e.Vsr == null || e.CloneCamera == null)
                    continue;

                Behaviour behaviour = e.Vsr as Behaviour;
                if (behaviour != null && behaviour.enabled != inside)
                    behaviour.enabled = inside;

                SyncAmbientMode(e, inside);

                if (!inside)
                    continue;

                if (interiorCamera == null)
                    continue;
                int mask = interiorCamera.cullingMask;
                if (mask == 0)
                    continue;
                if (mask != e.LastMask)
                {
                    e.LastMask = mask;
                    e.CloneCamera.cullingMask = mask;
                    SextantPipPose.ApplyMaskToPasses(e.CloneData, mask);
                }

                // VirtualSceneRenderer.SetRenderingEnabled(true) writes
                // clearFlags=Nothing onto the component's localCamera, which
                // for us is the PIP camera itself (its GetComponent<Camera>()
                // target). Restore the mod's solid clear.
                if (e.PipCamera.clearFlags != CameraClearFlags.SolidColor)
                    e.PipCamera.clearFlags = CameraClearFlags.SolidColor;

                // Single-writer (§16.22): with the clone suppressed from the
                // pass, the root must render, otherwise the pass has zero
                // GBuffer writers and the ocular goes black (live 2026-09-19:
                // suppression alone + root RenderScene=false = black ocular).
                AdditionalCameraData pipData = e.PipCamera.GetComponent<AdditionalCameraData>();
                if (pipData?.RenderScene == false)
                    pipData.RenderScene = true;
            }
        }

        /// <summary>
        /// Residual-light fix (root cause): renders with the writer's ambient
        /// path. Inside, the game renders the interior WITHOUT custom ambient
        /// (InteriorCamera=false → baked light probes apply); the PIP default
        /// (true, like the exterior root) strips probe per-object data
        /// (pipeline perObjectData) and cools the image (stand: BRIGHT b_r
        /// +22.9 → -2.9). Outside, keeps the exterior root default (true).
        /// Gated by PlayerPrefs RadioNav.AmbientModeMirror (default 1).
        /// Extracted from Sync (complexity gate: Sync must stay under 15).
        /// </summary>
        private void SyncAmbientMode(Entry e, bool inside)
        {
            if (e.PipData == null)
                return;
            if (PlayerPrefs.GetInt(AmbientModeMirrorPrefsKey, 1) == 0)
                return;
            bool wantAmbient = !inside || (interiorData != null && interiorData.UseCustomAmbient);
            if (e.PipData.UseCustomAmbient == wantAmbient)
                return;
            e.PipData.UseCustomAmbient = wantAmbient;
            if (ambientMirrorLogged)
                return;
            ambientMirrorLogged = true;
            Debug.Log(LogPrefix + " AMBIENT-MODE mirrored onto PIP (inside=" + inside + ")");
        }

        /// <summary>
        /// The interior camera's culling mask, or 0 when the graph is not
        /// resolved. SextantPipPose uses it to switch the PIP between the
        /// exterior and interior mask.
        /// </summary>
        public int InteriorMask
        {
            get { return interiorCamera != null ? interiorCamera.cullingMask : 0; }
        }

        /// <summary>
        /// The game's interior camera (correctly positioned every frame by
        /// the game's own pipeline). The direct-render follower copies its
        /// world position onto the PIP root — unlike the mod's clone, whose
        /// position is computed FROM the PIP and would feed back garbage.
        /// </summary>
        internal Camera InteriorViewCamera
        {
            get { return interiorCamera; }
        }

        /// <summary>Disables every mirror renderer (sextant closed).</summary>
        public void SetEnabled(bool enabled)
        {
            foreach (KeyValuePair<Camera, Entry> pair in entries)
            {
                Behaviour behaviour = pair.Value.Vsr as Behaviour;
                if (behaviour != null && behaviour.enabled != enabled)
                    behaviour.enabled = enabled;
            }
        }

        /// <summary>Destroys clones + renderer components (camera teardown).</summary>
        public void Destroy()
        {
            foreach (KeyValuePair<Camera, Entry> pair in entries)
            {
                Entry e = pair.Value;
                if (e.Vsr != null)
                    UnityEngine.Object.Destroy(e.Vsr);
                if (e.CloneObject != null)
                    UnityEngine.Object.Destroy(e.CloneObject);
            }
            entries.Clear();
            ResetGraph();
        }

        // ---- creation ------------------------------------------------------

        private Entry CreateEntry(Camera pipCamera)
        {
            // Clone camera on its own GameObject, child of the interior
            // camera's parent (the "Player Ship Interior Locator" root). The
            // interior scene lives in that locator space (root at
            // (0,100,0), identity rotation), NOT in the ship's scene space:
            // VirtualSceneRenderer.LateUpdate computes
            // virtualCamera.localPosition in the ship's local frame
            // (vs.Target.InverseTransformPoint) and that exact value must be
            // applied in the locator space to land on the game InteriorCamera
            // world position (live-verified 2026-09-17: the same
            // (-0.91,0.88,1.27) is ship-local for the clone and
            // locator-local for InteriorCamera).
            Transform cloneParent = ResolveCloneParent();
            GameObject cloneGo = new GameObject(pipCamera.name + "InteriorClone");
            cloneGo.transform.SetParent(cloneParent, false);

            Camera cloneCam = cloneGo.AddComponent<Camera>();
            cloneCam.CopyFrom(interiorCamera);
            cloneCam.targetTexture = null;
            cloneCam.enabled = true;

            // Aim follow-up: the VSR only writes localPosition in LateUpdate,
            // so the clone keeps the interior camera's fixed orientation and
            // the alidade would not move the ocular. The follower applies the
            // PIP camera's world rotation after the VSR's LateUpdate.
            SextantInteriorPoseFollower follower = cloneGo.AddComponent<SextantInteriorPoseFollower>();
            follower.Source = pipCamera.transform;
            follower.Clone = cloneCam.transform;

            AdditionalCameraData cloneData = cloneGo.AddComponent<AdditionalCameraData>();
            cloneData.RenderScene = true;
            SextantRenderPasses.Setup(cloneData, interiorData, cloneCam.cullingMask);
            if (interiorData != null)
            {
                try { cloneData.ShadowDistance = interiorData.ShadowDistance; }
                catch (Exception e)
                {
                    if (WarnOnce.ShouldLog("SextantInteriorMirror.ShadowDistance"))
                        Debug.LogWarning(LogPrefix + " ShadowDistance mirror failed: " + e.Message);
                }
            }

            // The SRP fetches the renderer from the rendered camera's
            // GameObject (DeepCameraData ctor IL_002C), so it lives on the
            // PIP camera object, not on the clone. Only virtualCamera +
            // virtualScene are needed: CollectCameras reads VirtualCamera,
            // LateUpdate gates RenderingEnabled on get_VirtualScene() and
            // positions the clone through vs.Target. The renderer's
            // interiorCamera field is STATIC and already set by the game's
            // own instance - never touch it.
            Component vsr = pipCamera.gameObject.AddComponent(vsrType);
            if (!TrySetMember(vsr, "virtualCamera", cloneCam) ||
                !TrySetMember(vsr, "virtualScene", virtualSceneObj))
            {
                UnityEngine.Object.Destroy(vsr);
                UnityEngine.Object.Destroy(cloneGo);
                if (WarnOnce.ShouldLog("SextantInteriorMirror.Fields"))
                    Debug.LogWarning(LogPrefix + " VirtualSceneRenderer fields missing - no interior in PIP");
                return null;
            }

            Entry entry = new Entry();
            entry.PipCamera = pipCamera;
            entry.CloneCamera = cloneCam;
            entry.CloneObject = cloneGo;
            entry.Vsr = vsr;
            entry.CloneData = cloneData;
            entry.PipData = pipCamera.GetComponent<AdditionalCameraData>();
            return entry;
        }

        // ---- game graph resolution ----------------------------------------

        /// <summary>Interior-camera parent (the interior locator space);
        /// falls back to VirtualScene.Target (the ship) when absent.</summary>
        private Transform ResolveCloneParent()
        {
            Transform parent = interiorCamera != null ? interiorCamera.transform.parent : null;
            return parent != null ? parent : sceneTarget;
        }

        private bool ResolveGraph()
        {
            if (graphResolved)
                return !graphFailed;
            graphResolved = true;

            string fail = null;
            try
            {
                MainCamera main = ResolveCameraGraph(ref fail);
                if (fail == null)
                    ResolveSceneGraph(main, ref fail);
            }
            catch (Exception e)
            {
                fail = "exception: " + e.Message;
            }

            if (fail != null || interiorCamera == null || sceneTarget == null)
            {
                graphFailed = true;
                if (WarnOnce.ShouldLog("SextantInteriorMirror.ResolveGraph"))
                    Debug.LogWarning(LogPrefix + " interior graph resolve failed: " + (fail ?? "null-graph"));
                return false;
            }

            Transform cloneParent = ResolveCloneParent();
            Debug.Log(LogPrefix + " INTERIOR graph resolved: clone source='" +
                interiorCamera.name + "' parent='" +
                (cloneParent != null ? cloneParent.name : "null") + "'");
            return true;
        }

        /// <summary>Resolves MainCamera + InteriorCamera (+ its data); sets
        /// <paramref name="fail"/> on the first missing link.</summary>
        private MainCamera ResolveCameraGraph(ref string fail)
        {
            if (ResolveVSRType() == null)
                fail = "no-vsr-type";

            MainCamera main = MainCamera.Instance;
            if (fail == null && main == null)
                main = UnityEngine.Object.FindObjectOfType<MainCamera>();
            if (fail == null && main == null)
                fail = "no-main-camera";
            if (fail != null)
                return main;

            interiorCameraObj = GetMember(main, "InteriorCamera");
            if (interiorCameraObj == null)
            {
                fail = "no-interior-camera";
                return main;
            }

            interiorCamera = GetMember(interiorCameraObj, "Camera") as Camera;
            interiorData = GetMember(interiorCameraObj, "AdditionalCameraData") as AdditionalCameraData;
            if (interiorCamera == null)
                fail = "no-interior-camera-component";
            return main;
        }

        /// <summary>Resolves the interior VirtualScene + its Target transform.</summary>
        private void ResolveSceneGraph(MainCamera main, ref string fail)
        {
            virtualSceneObj = ResolveInteriorVirtualScene(main);
            if (virtualSceneObj == null)
            {
                fail = "no-virtual-scene";
                return;
            }

            sceneTarget = GetMember(virtualSceneObj, "Target") as Transform;
            if (sceneTarget == null)
                fail = "no-scene-target";
        }

        /// <summary>
        /// The VirtualSceneRenderer's serialized VirtualScene field is null in
        /// game (the scene arrives through the per-frame
        /// OverrideLocalTransformation override), so the canonical source is
        /// PlayerShip.Interior.VirtualScene (the same reference the camera
        /// controllers pass to the renderer).
        /// </summary>
        private static object ResolveInteriorVirtualScene(MainCamera main)
        {
            Type playerShipType = GameTypeCache.Resolve(
                "UBOAT.Game.Scene.Entities.PlayerShip", "com.uboat.game");
            if (playerShipType != null)
            {
                UnityEngine.Object playerShip = UnityEngine.Object.FindObjectOfType(playerShipType);
                object interior = GetMember(playerShip, "Interior");
                object scene = GetMember(interior, "VirtualScene");
                if (scene != null)
                    return scene;
            }

            object vsr = GetMember(main, "VirtualSceneRenderer");
            return GetMember(vsr, "virtualScene") ?? GetMember(vsr, "virtualSceneOverride");
        }

        private static Type ResolveVSRType()
        {
            if (vsrType != null)
                return vsrType;
            vsrType = GameTypeCache.Resolve("UBOAT.Game.Scene.Effects.VirtualSceneRenderer", RenderingAssembly);
            if (vsrType == null)
                vsrType = GameTypeCache.Resolve("UBOAT.Game.Scene.Effects.VirtualSceneRenderer",
                    RenderingAssembly, "com.uboat.game");
            if (vsrType == null && WarnOnce.ShouldLog("SextantInteriorMirror.Type"))
                Debug.LogWarning(LogPrefix + " VirtualSceneRenderer type not found - no interior in PIP");
            return vsrType;
        }

        private void ResetGraph()
        {
            graphResolved = false;
            graphFailed = false;
            interiorCameraObj = null;
            interiorCamera = null;
            interiorData = null;
            virtualSceneObj = null;
            sceneTarget = null;
        }

        // ---- reflection helpers -------------------------------------------

        private static object GetMember(object target, string memberName)
        {
            if (target == null)
                return null;
            MemberInfo member = TypeMemberCache.GetInstanceMember(target.GetType(), memberName);
            if (member is PropertyInfo property)
                return property.GetValue(target, null);
            if (member is FieldInfo field)
                return field.GetValue(target);
            return null;
        }

        private static bool TrySetMember(object target, string memberName, object value)
        {
            if (target == null)
                return false;
            MemberInfo member = TypeMemberCache.GetInstanceMember(target.GetType(), memberName);
            if (member is FieldInfo field)
            {
                field.SetValue(target, value);
                return true;
            }
            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }
            return false;
        }
    }
}
