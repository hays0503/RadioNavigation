using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CPlath.AnimationImport;
using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Optional validated animated bundle; never owns existing PIP/input.</summary>
    internal sealed class CPlathAnimatedActor
    {
        const string BundleName = "cplath_animated";
        object bundle;
        GameObject model;
        CPlathAdaptiveAnimationPlayer player;
        CPlathAdaptiveMechanismRig mechanisms;
        CPlathAdaptiveReadClearance readClearance;
        AnimationClip[] clips;
        Transform[] nodes;
        Renderer[] renderers;
        readonly CPlathShadeState shadeState = new CPlathShadeState();
        SkinnedMeshRenderer nativeBody;
        bool nativeBodyWasEnabled, ownsNativeVisibility, initialized, optics;
        bool equipped, reading;
        Camera leasedCamera;
        float previousNear, leasedNear;
        internal bool NeedsUpdate { get { return initialized && model != null && (equipped || player.IsVisible); } }
        internal int LeftCount { get { return shadeState.LeftCount; } }
        internal int RightCount { get { return shadeState.RightCount; } }
        internal GameObject PointerModel { get { return initialized ? model : null; } }
        internal CPlathAdaptiveAnimationPlayer.Phase Phase { get { return player != null ? player.CurrentPhase : CPlathAdaptiveAnimationPlayer.Phase.Stowed; } }
        internal static bool Installed
        { get { return File.Exists(Path.Combine(Application.persistentDataPath, "Mods", "RadioNavigation", "CPlath", BundleName)); } }

        internal void Take()
        {
            if (model == null) Create();
            if (!initialized)
            {
                model.SetActive(true);
                player.Initialize(clips[0], clips[1], clips[2], clips[3], mechanisms.AnimationTimeScale);
                readClearance = new CPlathAdaptiveReadClearance(model.transform, player.EyeAnchor);
                initialized = true;
            }
            equipped = true; reading = optics = false;
            player.SetDesired(true, false);
        }

        void Create()
        {
            Type type = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
            if (type == null) throw new InvalidOperationException("AssetBundle API unavailable");
            if (bundle == null)
            {
                var loaded = Method(type, "GetAllLoadedAssetBundles", true, 0).Invoke(null, null) as IEnumerable;
                if (loaded != null) foreach (object candidate in loaded)
                {
                    var unity = candidate as UnityEngine.Object;
                    if (unity != null && unity.name == BundleName) { bundle = candidate; break; }
                }
            }
            if (bundle == null) bundle = Method(type, "LoadFromFile", true, 1).Invoke(null,
                new object[] { Path.Combine(Application.persistentDataPath, "Mods", "RadioNavigation", "CPlath", BundleName) });
            if (bundle == null) throw new InvalidOperationException("Animated C. Plath bundle failed to load");
            string[] assets = Method(type, "GetAllAssetNames", false, 0).Invoke(bundle, null) as string[];
            if (assets == null) throw new InvalidOperationException("Animated asset inventory unavailable");
            Func<string, UnityEngine.Object> load = suffix => Method(type, "LoadAsset", false, 1).Invoke(bundle,
                new object[] { assets.Single(a => a.EndsWith("/"+suffix, StringComparison.OrdinalIgnoreCase)) }) as UnityEngine.Object;
            var prefab = load("CPlath_Animated_UBOAT.prefab") as GameObject;
            var bindings = load("mechanism_bindings.json") as TextAsset;
            clips = new[] { "RaiseHoldLower", "EnterRead", "ReadHold", "ExitRead" }
                .Select(name => load("CPlath_"+name+".anim") as AnimationClip).ToArray();
            if (prefab == null || bindings == null || clips.Any(c => c == null)) throw new InvalidOperationException("Animated asset set incomplete");
            var configuration = CPlathAdaptiveBindingReader.Read(bindings.text);
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) throw new InvalidOperationException("Animated prefab is not script-free");
            model = UnityEngine.Object.Instantiate(prefab); model.name = "RadioNav_CPlath_Animated";
            nodes = model.GetComponentsInChildren<Transform>(true); renderers = model.GetComponentsInChildren<Renderer>(true);
            mechanisms = new CPlathAdaptiveMechanismRig(model.transform, configuration);
            player = model.AddComponent<CPlathAdaptiveAnimationPlayer>(); player.AutomaticUpdate = false;
            Debug.Log("[RadioNav][CPlath] calibrated bindings loaded, parser=MiniJson, joints=9, timeScale="+mechanisms.AnimationTimeScale);
        }

        internal void SetDesired(bool held, bool read)
        { equipped = held; reading = held && read; if (initialized) player.SetDesired(equipped, reading); }
        internal void SetOptics(bool active) { optics = active; }
        internal void SetBankCounts(int left, int right) { shadeState.SyncCounts(left, right); }
        internal void SetBankCount(int count, bool left) { shadeState.SelectBank(count, left); }
        internal void ToggleShade(int index) { shadeState.Toggle(index); }

        internal void Tick(Camera camera, double degrees, float dt)
        {
            if (!initialized) return;
            if (camera == null) throw new InvalidOperationException("Actual FPP view camera unavailable");
            player.SetDesired(equipped, reading && !optics); player.Advance(Mathf.Max(0, dt));
            float gripWeight = 1;
            if (player.CurrentPhase == CPlathAdaptiveAnimationPlayer.Phase.Raising || player.CurrentPhase == CPlathAdaptiveAnimationPlayer.Phase.Lowering)
            {
                float frame = (float)player.CurrentTime*24+1;
                gripWeight = CPlathAdaptiveMechanismRig.LeftGripWeight(player.CurrentClip.name, frame);
            }
            else if (!player.IsVisible) gripWeight = 0;
            readClearance.Apply(player.CurrentClip.name, (float)player.CurrentTime*24+1);
            mechanisms.Apply(degrees, shadeState.IsActive, dt, gripWeight);
            float viewFrame = (float)player.CurrentTime*24+1;
            player.AlignToCamera(camera.transform, readClearance.ViewOffset(player.CurrentClip.name, viewFrame),
                readClearance.ViewPosition(player.CurrentClip.name, viewFrame));
            bool visible = player.IsVisible && !optics;
            bool inside = MainCamera.Instance != null && MainCamera.Instance.IsInsideBoat;
            int layer = LayerMask.NameToLayer(inside ? "Interior" : "Default");
            if (layer < 0 || (camera.cullingMask & (1 << layer)) == 0) throw new InvalidOperationException("Animated actor layer missing from view camera");
            foreach (var node in nodes) node.gameObject.layer = layer;
            foreach (var renderer in renderers) renderer.enabled = visible;
            if (visible)
            {
                AcquireNativeBody(); LeaseNearPlane(camera);
            }
            else { RestoreNativeBody(); ReleaseNearPlane(); }
        }

        void AcquireNativeBody()
        {
            object main = MainCamera.Instance;
            var body = Read(Read(main, "FPPController"), "FPPBody") as SkinnedMeshRenderer;
            if (body == null) throw new InvalidOperationException("Native FPP body unavailable");
            if (nativeBody != body) { RestoreNativeBody(); nativeBody = body; }
            if (!ownsNativeVisibility)
            {
                nativeBodyWasEnabled = body.enabled; ownsNativeVisibility = true;
                player.BindNativeSkin(body.sharedMaterial);
            }
            body.enabled = false;
        }

        void RestoreNativeBody()
        {
            if (ownsNativeVisibility && nativeBody != null) nativeBody.enabled = nativeBodyWasEnabled;
            nativeBody = null; ownsNativeVisibility = false;
        }

        // Eye-registered real-size geometry cannot sit beyond the exterior FPP
        // near plane. Lease only the active view's near plane while visible;
        // restore on PIP, stow, mode/character change, disable and failure. Never
        // overwrite a value another camera owner changed after our last tick.
        void LeaseNearPlane(Camera camera)
        {
            if (leasedCamera != camera) { ReleaseNearPlane(); leasedCamera = camera; previousNear = camera.nearClipPlane; }
            else if (!Mathf.Approximately(camera.nearClipPlane, leasedNear)) previousNear = camera.nearClipPlane;
            leasedNear = Mathf.Min(previousNear, .01f); camera.nearClipPlane = leasedNear;
        }
        void ReleaseNearPlane()
        {
            if (leasedCamera != null && Mathf.Approximately(leasedCamera.nearClipPlane, leasedNear)) leasedCamera.nearClipPlane = previousNear;
            leasedCamera = null;
        }

        internal void Suspend()
        {
            equipped = reading = optics = false; RestoreNativeBody(); ReleaseNearPlane();
            if (model != null) model.SetActive(false);
            initialized = false;
        }
        internal void Destroy()
        {
            Suspend(); if (model != null) UnityEngine.Object.Destroy(model);
            model = null; player = null; mechanisms = null; readClearance = null; nodes = null; renderers = null;
        }
        static object Read(object instance, string name)
        {
            for (Type type = instance != null ? instance.GetType() : null; type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanRead) return property.GetValue(instance, null);
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(instance);
            }
            return null;
        }
        static MethodInfo Method(Type type, string name, bool isStatic, int count)
        {
            var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            return type.GetMethods(flags).Single(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == count);
        }
    }
}
