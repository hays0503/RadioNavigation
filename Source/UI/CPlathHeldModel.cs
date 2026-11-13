using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>First-person model/animation. Existing sextant still owns PIP and navigation.</summary>
    internal sealed class CPlathHeldModel
    {
        private const string BundleName = "cplath_sextant";
        private object bundle;
        private GameObject model;
        private CPlathModelRig rig;
        private CPlathAnimatedActor animated;
        private CPlathReadPointer readPointer;
        private bool readingRequested;
        private Action<int> pointerToggle;
        private MainCamera cachedMain;
        private Camera cachedInterior;
        private Transform[] modelNodes;
        private int appliedLayer = -1;
        private readonly HeldKeyEdge[] shadeEdges = { new HeldKeyEdge(), new HeldKeyEdge(), new HeldKeyEdge(), new HeldKeyEdge() };
        private static readonly KeyCode[] shadeKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };
        internal bool IsHeld { get; private set; }
        internal bool Aiming { get; private set; }
        internal string LastError { get; private set; }
        internal int LeftCount { get { return animated != null ? animated.LeftCount : rig != null ? rig.LeftCount : 0; } }
        internal int RightCount { get { return animated != null ? animated.RightCount : rig != null ? rig.RightCount : 0; } }
        internal bool NeedsUpdate { get { return IsHeld || animated != null && animated.NeedsUpdate; } }

        internal bool Take()
        {
            try
            {
                if (CPlathAnimatedActor.Installed)
                {
                    if (animated == null) animated = new CPlathAnimatedActor();
                    animated.Take();
                }
                else if (model == null) CreateModel();
                IsHeld = true; Aiming = false; readingRequested = false; LastError = null;
                for (int i = 0; i < shadeEdges.Length; i++) shadeEdges[i].Synchronize(Input.GetKey(shadeKeys[i]));
                Debug.Log("[RadioNav][CPlath] taken"); return true;
            }
            catch (Exception e)
            {
                LastError = e.ToString(); Stow();
                if (animated != null) animated.Destroy();
                animated = null;
                if (model != null) UnityEngine.Object.Destroy(model);
                model = null; rig = null;
                Debug.LogError("[RadioNav][CPlath] cannot take: " + e.Message); return false;
            }
        }

        private void CreateModel()
        {
            Type type = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
            if (type == null) throw new InvalidOperationException("AssetBundle API unavailable.");
            if (bundle == null) bundle = FindLoaded(type);
            if (bundle == null)
            {
                string path = Path.Combine(Application.persistentDataPath, "Mods", "RadioNavigation", "CPlath", BundleName);
                if (!File.Exists(path)) throw new FileNotFoundException("C. Plath model bundle missing", path);
                bundle = Method(type, "LoadFromFile", true, 1).Invoke(null, new object[] { path });
            }
            if (bundle == null) throw new InvalidOperationException("C. Plath bundle failed to load.");
            var prefab = Method(type, "LoadAsset", false, 1).Invoke(bundle,
                new object[] { "assets/sextantvalidation/prefabs/c_plath_uboat.prefab" }) as GameObject;
            if (prefab == null) throw new InvalidOperationException("C. Plath prefab missing from bundle.");
            model = UnityEngine.Object.Instantiate(prefab); model.name = "RadioNav_CPlath_Held";
            model.SetActive(false); modelNodes = model.GetComponentsInChildren<Transform>(true);
            appliedLayer = -1; rig = new CPlathModelRig(model.transform);
        }

        private static MethodInfo Method(Type type, string name, bool isStatic, int count)
        {
            BindingFlags flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            foreach (MethodInfo method in type.GetMethods(flags))
                if (method.Name == name && !method.IsGenericMethod && method.GetParameters().Length == count) return method;
            throw new MissingMethodException(type.FullName, name);
        }

        private static object FindLoaded(Type type)
        {
            var bundles = Method(type, "GetAllLoadedAssetBundles", true, 0).Invoke(null, null) as IEnumerable;
            if (bundles == null) return null;
            foreach (object candidate in bundles)
            {
                var obj = candidate as UnityEngine.Object;
                if (obj != null && obj.name == BundleName) return candidate;
            }
            return null;
        }

        internal void Stow()
        {
            readingRequested = false;
            if (readPointer != null) readPointer.Suspend();
            IsHeld = false; Aiming = false;
            if (animated != null) { animated.SetOptics(false); animated.SetDesired(false, false); }
            if (model != null) model.SetActive(false);
        }
        internal void Suspend()
        {
            readingRequested = false;
            if (readPointer != null) readPointer.Suspend();
            IsHeld = false; Aiming = false;
            if (animated != null) animated.Suspend();
            if (model != null) model.SetActive(false);
        }
        internal void SetReading(bool reading)
        {
            readingRequested = IsHeld && !Aiming && reading;
            if (!readingRequested && readPointer != null) readPointer.Suspend();
            if (animated != null) animated.SetDesired(IsHeld, readingRequested);
        }
        internal void SetAiming(bool aiming)
        {
            Aiming = IsHeld && aiming;
            if (Aiming)
            {
                readingRequested = false;
                if (readPointer != null) readPointer.Suspend();
            }
            if (animated != null) animated.SetOptics(Aiming);
            if (model != null) model.SetActive(IsHeld && !Aiming);
        }
        internal void Destroy()
        {
            Suspend(); if (animated != null) animated.Destroy(); animated = null;
            if (model != null) UnityEngine.Object.Destroy(model);
            model = null; rig = null; cachedInterior = null; cachedMain = null; modelNodes = null;
            // Keep the small immutable bundle loaded: other hot-reload copies may use it.
        }

        internal void SetBankCount(int count, bool left)
        {
            if (animated != null) animated.SetBankCount(count, left);
            if (rig != null) rig.SetBankCount(count, left);
        }

        internal bool PollIndependentShades(out bool changed)
        {
            changed = false;
            if (!IsHeld || (rig == null && animated == null)) return false;
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool left = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            for (int i = 0; i < shadeEdges.Length; i++)
            {
                bool pressed = shadeEdges[i].Advance(Input.GetKey(shadeKeys[i]));
                if (alt && pressed && !(left && i == 3))
                {
                    if (animated != null)
                    {
                        animated.ToggleShade(left ? 4 + i : i);
                        changed = true;
                    }
                    else if (rig != null)
                    {
                        rig.ToggleShade(left ? 4 + i : i);
                        changed = true;
                    }
                }
            }
            return alt;
        }

        private Camera ViewCamera(Camera exterior)
        {
            MainCamera main = MainCamera.Instance;
            if (main == null || !main.IsInsideBoat) return exterior;
            if (cachedMain != main || cachedInterior == null)
            {
                cachedMain = main;
                var interior = main.InteriorCamera;
                cachedInterior = interior != null ? interior.Camera : null;
            }
            return cachedInterior;
        }

        internal void UpdatePose(Camera exterior, double degrees, int left, int right, float dt)
        {
            if (animated != null) { TickAnimated(exterior, degrees, left, right, dt); return; }
            if (!IsHeld || model == null || rig == null) return;
            rig.SetBankCounts(left, right); rig.Apply(degrees, dt);
            if (Aiming) return;
            Camera camera = ViewCamera(exterior);
            if (camera == null) { model.SetActive(false); return; }
            if (!ApplyViewLayer(camera)) { model.SetActive(false); return; }
            Quaternion rotation = camera.transform.rotation * Quaternion.Euler(0f, 160f, -8f);
            // Exterior FPP uses a much larger near plane than the interior
            // view. Keep the real-size model in front of it without modifying
            // either game camera or moving the virtual interior itself.
            float distance = Mathf.Max(.65f, camera.nearClipPlane + .25f);
            Vector3 grip = camera.transform.TransformPoint(new Vector3(.15f * distance / .65f, -.08f * distance / .65f, distance));
            model.transform.SetPositionAndRotation(grip - rotation * rig.GripLocal, rotation);
            model.SetActive(true);
        }

        private void TickAnimated(Camera exterior, double degrees, int left, int right, float dt)
        {
            try
            {
                animated.SetBankCounts(left, right);
                Camera camera = ViewCamera(exterior);
                animated.Tick(camera, degrees, dt);
                TickReadPointer(camera, degrees);
            }
            catch (Exception e)
            {
                LastError = e.ToString(); Suspend();
                Debug.LogError("[RadioNav][CPlath] animated pose suspended: " + e.Message);
            }
        }

        private void TickReadPointer(Camera camera, double degrees)
        {
            var controller = SextantController.Instance;
            bool interactive = readingRequested && controller != null && controller.CanUseReadPointer(this);
            if (!interactive) { if (readPointer != null) readPointer.Suspend(); return; }
            if (readPointer == null)
            {
                if (animated.PointerModel == null) return;
                readPointer = animated.PointerModel.AddComponent<CPlathReadPointer>();
                if (pointerToggle == null) pointerToggle = TogglePointerShade;
            }
            readPointer.Tick(true, animated.Phase == CPlath.AnimationImport.CPlathAdaptiveAnimationPlayer.Phase.Reading,
                animated.PointerModel, camera, degrees, pointerToggle);
        }

        private void TogglePointerShade(int index)
        {
            var controller = SextantController.Instance;
            if (!readingRequested || animated == null || controller == null || !controller.CanUseReadPointer(this)) return;
            animated.ToggleShade(index);
            controller.SyncReadPointerShades(this);
        }

        private bool ApplyViewLayer(Camera camera)
        {
            bool inside = MainCamera.Instance != null && MainCamera.Instance.IsInsideBoat;
            int layer = LayerMask.NameToLayer(inside ? "Interior" : "Default");
            if (layer < 0 || (camera.cullingMask & (1 << layer)) == 0)
            {
                LastError = "Held model layer is absent from the actual view camera mask."; return false;
            }
            if (layer == appliedLayer) return true;
            foreach (Transform node in modelNodes) node.gameObject.layer = layer;
            appliedLayer = layer; return true;
        }
    }
}
