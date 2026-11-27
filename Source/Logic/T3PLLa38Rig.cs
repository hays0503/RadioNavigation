using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    internal static class T3PLLa38AssetLoader
    {
        private const string BundleFileName = "t3plla38";
        private static object loadedBundle;
        private static GameObject cachedPrefab;
        private static Shader cachedStubShader;

        internal static bool TryLoadAssets(out GameObject prefab, out Shader stubCutoutShader)
        {
            prefab = cachedPrefab;
            stubCutoutShader = cachedStubShader;
            if (prefab != null && stubCutoutShader != null)
                return true;

            try
            {
                Type bundleType = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
                if (bundleType == null)
                    return false;

                object bundle = FindOrLoadBundle(bundleType);
                if (bundle == null)
                    return false;

                MethodInfo loadAsset = FindSingleArgMethod(bundleType, "LoadAsset", typeof(string));
                if (loadAsset == null)
                    return false;

                if (cachedPrefab == null)
                    cachedPrefab = loadAsset.Invoke(bundle, new object[] { "Assets/T3PLLa38/T3PLLa38.prefab" }) as GameObject;
                if (cachedStubShader == null)
                    cachedStubShader = loadAsset.Invoke(bundle, new object[] { "Assets/T3PLLa38/Shaders/RadioNav_StubCutout.shader" }) as Shader;

                prefab = cachedPrefab;
                stubCutoutShader = cachedStubShader;
                return prefab != null && stubCutoutShader != null;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioNav][T3PLLa38] Bundle load failed: " + ex.Message);
                return false;
            }
        }

        private static MethodInfo FindSingleArgMethod(Type targetType, string name, Type argType)
        {
            MethodInfo[] methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.IsGenericMethodDefinition || m.Name != name)
                    continue;
                ParameterInfo[] pars = m.GetParameters();
                if (pars.Length == 1 && pars[0].ParameterType == argType)
                    return m;
            }
            return null;
        }

        private static object FindOrLoadBundle(Type bundleType)
        {
            if (loadedBundle != null)
                return loadedBundle;

            MethodInfo getAll = bundleType.GetMethod("GetAllLoadedAssetBundles", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (getAll != null)
            {
                IEnumerable all = getAll.Invoke(null, null) as IEnumerable;
                if (all != null)
                {
                    foreach (object cand in all)
                    {
                        Object uo = cand as Object;
                        if (uo?.name == BundleFileName)
                        {
                            loadedBundle = cand;
                            return loadedBundle;
                        }
                    }
                }
            }

            string bundlePath = Path.Combine(Application.persistentDataPath, "Mods", "RadioNavigation", "T3PLLa38", BundleFileName);
            if (!File.Exists(bundlePath))
            {
                Debug.LogError("[RadioNav][T3PLLa38] AssetBundle missing: " + bundlePath);
                return null;
            }

            MethodInfo loadFromFile = bundleType.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (loadFromFile == null)
                return null;

            loadedBundle = loadFromFile.Invoke(null, new object[] { bundlePath });
            return loadedBundle;
        }
    }

    /// <summary>
    /// Mounted 3D T3PLLa38 receiver rig: handles mechanical control actuation, emission glow,
    /// tube warm-up, voltmeter needle, collider lookup, and bidirectional sync with the 2D radio panel.
    /// </summary>
    public sealed class T3PLLa38Rig : MonoBehaviour
    {
        private const string RigObjectName = "RadioNav_T3PLLa38";
        private const int InteriorLayer = 23;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Dictionary<Collider, T3ControlKind> colliderMap = new Dictionary<Collider, T3ControlKind>();
        private readonly List<Collider> controlColliders = new List<Collider>();
        private readonly Dictionary<string, Transform> nodes = new Dictionary<string, Transform>();

        private Material dialMat;
        private Material voltmeterMat;
        private Material filamentMat;
        private Light dialLight;
        private Light tubeLightLeft;
        private Light tubeLightRight;
        private ZoneHandler cachedZone;
        private ToggleHandler cachedToggle;
        private UnityEngine.UI.Toggle cachedToggleComp;
        private GroundHandler cachedGround;
        private VolumeSpinnerHandler cachedVolume;
        private FeedbackSpinnerHandler cachedFeedback;
        private bool subscribedToRadio;

        private bool powerOn;
        private float warmup01;
        private int bandIndex;
        private float dialBlenderDeg;
        private float volumeDeg;
        private float feedbackDeg;
        private float reglerPeilenDeg;
        private float reglerSeiteDeg = 60f;
        private bool seiteMode;
        private bool rundEmpfangOn;
        private float hTrimmerDeg;
        private int battTestIndex;
        private readonly List<Renderer> lidRenderers = new List<Renderer>();
        private Collider lidCollider;
        private bool lidAttached;
        private bool lidOpen = true;
        private bool doorTubesOpen;
        private bool capBatteryOpen;
        private bool coverAntennaOpen;
        private float lidCurrentDeg = 162f;
        private float doorCurrentDeg;
        private float capCurrentDeg;
        private float coverCurrentDeg;

        internal bool PowerOn => powerOn;
        internal int BandIndex => bandIndex;
        internal float DialBlenderDeg => dialBlenderDeg;

        internal static T3PLLa38Rig Install(Transform radioRoom, T3StubCutoutBoxes boxes)
        {
            if (radioRoom == null)
                return null;

            GameObject prefab;
            Shader stubShader;
            if (!T3PLLa38AssetLoader.TryLoadAssets(out prefab, out stubShader))
                return null;

            Transform old = radioRoom.Find(RigObjectName);
            if (old != null)
            {
                old.gameObject.SetActive(false);
                Object.Destroy(old.gameObject);
            }

            GameObject go = Object.Instantiate(prefab, radioRoom, false);
            go.name = RigObjectName;
            go.transform.localPosition = new Vector3(boxes.MountX, boxes.MountY, boxes.MountZ);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * boxes.MountScale;
            SetLayerRecursive(go.transform, InteriorLayer);

            T3PLLa38Rig rig = go.AddComponent<T3PLLa38Rig>();
            rig.Initialize(radioRoom);
            return rig;
        }

        private void Initialize(Transform radioRoom)
        {
            CacheNodesAndColliders();
            CloneEmissiveMaterials();
            ConfigureLightingAndProbes(radioRoom);
            SetCollidersEnabled(false);
            SyncFromModState();
            ApplyAllTransformsImmediate();
        }

        public void OnEnable()
        {
            TrySubscribeRadioEvents();
        }

        public void OnDisable()
        {
            UnsubscribeRadioEvents();
        }

        public void OnDestroy()
        {
            UnsubscribeRadioEvents();
        }

        public void Update()
        {
            if (!subscribedToRadio)
                TrySubscribeRadioEvents();

            float targetWarm = powerOn ? 1f : 0f;
            float rate = powerOn ? 0.55f : 1.4f;
            if (Math.Abs(warmup01 - targetWarm) > 0.001f)
                warmup01 = Mathf.MoveTowards(warmup01, targetWarm, rate * Time.unscaledDeltaTime);

            StepDoorHinges();
            UpdateVoltmeterNeedle();
            UpdateLighting();
        }

        internal void SetCollidersEnabled(bool enabledState)
        {
            for (int i = 0; i < controlColliders.Count; i++)
            {
                Collider col = controlColliders[i];
                if (col == null)
                    continue;
                if (col == lidCollider)
                    col.enabled = enabledState && lidAttached;
                else
                    col.enabled = enabledState;
            }
        }

        internal bool TryResolveControl(Collider col, out T3ControlKind kind)
        {
            if (col == null)
            {
                kind = T3ControlKind.None;
                return false;
            }
            return colliderMap.TryGetValue(col, out kind);
        }

        internal void AdjustContinuous(T3ControlKind kind, float deltaNorm)
        {
            if (kind == T3ControlKind.DialCursor || kind == T3ControlKind.TuningCrank)
            {
                float scale = kind == T3ControlKind.TuningCrank ? 22f : 145f;
                SetDialFrom3D(dialBlenderDeg + deltaNorm * scale);
                return;
            }
            if (kind == T3ControlKind.Volume)
            {
                SetVolumeDeg(volumeDeg + deltaNorm * 160f, true);
                return;
            }
            if (kind == T3ControlKind.Feedback)
            {
                SetFeedbackDeg(feedbackDeg + deltaNorm * 150f, true);
                return;
            }
            if (kind == T3ControlKind.ReglerPeilen)
            {
                reglerPeilenDeg = T3PLLa38ControlMath.Clamp(reglerPeilenDeg + deltaNorm * 160f, -T3PLLa38ControlMath.ReglerPeilenMaxDeg, T3PLLa38ControlMath.ReglerPeilenMaxDeg);
                ApplyBlenderY("Ctrl_Regler_Peilen", reglerPeilenDeg);
                return;
            }
            if (kind == T3ControlKind.ReglerSeite)
            {
                reglerSeiteDeg = T3PLLa38ControlMath.Clamp(reglerSeiteDeg + deltaNorm * 220f, 0f, T3PLLa38ControlMath.ReglerSeiteMaxDeg);
                ApplyBlenderY("Ctrl_Regler_Seite", reglerSeiteDeg);
                return;
            }
            if (kind == T3ControlKind.HTrimmer)
            {
                hTrimmerDeg = T3PLLa38ControlMath.Clamp(hTrimmerDeg + deltaNorm * 160f, T3PLLa38ControlMath.HTrimmerMinDeg, T3PLLa38ControlMath.HTrimmerMaxDeg);
                ApplyBlenderY("Ctrl_H_Trimmer", hTrimmerDeg);
            }
        }

        internal void ClickOrStepControl(T3ControlKind kind, int stepDir)
        {
            if (TryStepSelectorOrSwitch(kind, stepDir))
                return;
            ToggleCoverOrDoor(kind);
        }

        private bool TryStepSelectorOrSwitch(T3ControlKind kind, int stepDir)
        {
            if (kind == T3ControlKind.PowerSwitch)
            {
                SetPowerFrom3D(!powerOn);
                return true;
            }
            if (kind == T3ControlKind.BandSelector)
            {
                SetBandFrom3D((bandIndex + (stepDir >= 0 ? 1 : -1) + 5) % 5);
                return true;
            }
            if (kind == T3ControlKind.BattRohrenSwitch)
            {
                int len = T3PLLa38ControlMath.BattTestAnglesBlenderDeg.Length;
                battTestIndex = (battTestIndex + (stepDir >= 0 ? 1 : -1) + len) % len;
                ApplyBlenderY("Ctrl_Batterie_Rohren", T3PLLa38ControlMath.BattTestAnglesBlenderDeg[battTestIndex]);
                return true;
            }
            if (kind == T3ControlKind.RundEmpfang)
            {
                rundEmpfangOn = !rundEmpfangOn;
                ApplyBlenderZ("Ctrl_Rund_Empfang", T3PLLa38ControlMath.ComputeRundSwitchBlenderDeg(rundEmpfangOn));
                return true;
            }
            if (kind == T3ControlKind.SeitePeilen)
            {
                seiteMode = !seiteMode;
                ApplyBlenderY("Ctrl_Seite_Peilen", T3PLLa38ControlMath.ComputeSeitePeilenBlenderDeg(seiteMode));
                return true;
            }
            return false;
        }

        private void ToggleCoverOrDoor(T3ControlKind kind)
        {
            if (kind == T3ControlKind.LidFront)
            {
                ToggleFrontLid();
                return;
            }
            if (kind == T3ControlKind.DoorTubes)
            {
                doorTubesOpen = !doorTubesOpen;
                if (doorTubesOpen && lidAttached && !lidOpen)
                    lidOpen = true;
                return;
            }
            if (kind == T3ControlKind.CapBattery)
                capBatteryOpen = !capBatteryOpen;
            else if (kind == T3ControlKind.CoverTopAntenna)
                coverAntennaOpen = !coverAntennaOpen;
        }

        private void ToggleFrontLid()
        {
            if (!lidAttached)
            {
                doorTubesOpen = false;
                lidAttached = true;
                lidOpen = false;
                ApplyLidVisibility();
            }
            else if (!lidOpen)
            {
                lidOpen = true;
            }
            else
            {
                lidAttached = false;
                ApplyLidVisibility();
            }
        }

        internal string FormatReadout(T3ControlKind kind, string language)
        {
            string title = T3PLLa38ControlMath.GetControlLabel(kind, language);
            string detail = FormatControlValue(kind, language);
            return string.IsNullOrEmpty(detail) ? title : title + ": " + detail;
        }

        private string FormatControlValue(T3ControlKind kind, string language)
        {
            string tuning = FormatTuningOrLevelValue(kind);
            return tuning != null ? tuning : FormatSwitchOrDoorValue(kind, language);
        }

        private string FormatTuningOrLevelValue(T3ControlKind kind)
        {
            if (kind == T3ControlKind.DialCursor || kind == T3ControlKind.TuningCrank)
            {
                float panelAngle = T3PLLa38ControlMath.DialDegToCursorPanelAngle(dialBlenderDeg);
                float freq = DialConfig.AngleToFrequency(panelAngle + Constants.CursorOffsetDeg, bandIndex);
                return DialConfig.FormatFrequency(freq, bandIndex);
            }
            if (kind == T3ControlKind.Volume)
            {
                float volUnit = (volumeDeg / T3PLLa38ControlMath.VolumeMaxDeg + 1f) * 0.5f;
                return Mathf.RoundToInt(Mathf.Clamp01(volUnit) * 100f) + "%";
            }
            if (kind == T3ControlKind.Feedback)
            {
                float fbUnit = (feedbackDeg / T3PLLa38ControlMath.FeedbackMaxDeg + 1f) * 0.5f;
                return Mathf.RoundToInt(Mathf.Clamp01(fbUnit) * 100f) + "%";
            }
            if (kind == T3ControlKind.ReglerPeilen)
            {
                int deg = Mathf.RoundToInt(reglerPeilenDeg);
                return (deg > 0 ? "+" : string.Empty) + deg + "°";
            }
            if (kind == T3ControlKind.ReglerSeite)
                return T3PLLa38ControlMath.ComputeReglerSeiteScale(reglerSeiteDeg) + " / 20";
            if (kind == T3ControlKind.HTrimmer)
                return T3PLLa38ControlMath.ComputeHeaterVolts(powerOn, hTrimmerDeg).ToString("F2") + " V";
            return null;
        }

        private string FormatSwitchOrDoorValue(T3ControlKind kind, string language)
        {
            if (kind == T3ControlKind.PowerSwitch) return powerOn ? "EIN" : "AUS";
            if (kind == T3ControlKind.BandSelector) return T3PLLa38ControlMath.BandRoman[T3PLLa38ControlMath.ClampInt(bandIndex, 0, 4)];
            if (kind == T3ControlKind.BattRohrenSwitch)
                return T3PLLa38ControlMath.BattTestNames[T3PLLa38ControlMath.ClampInt(battTestIndex, 0, T3PLLa38ControlMath.BattTestNames.Length - 1)];
            if (kind == T3ControlKind.RundEmpfang) return rundEmpfangOn ? "Rundempfang" : "Peilen";
            if (kind == T3ControlKind.SeitePeilen) return seiteMode ? "Seite" : "Peilen";
            if (kind == T3ControlKind.LidFront)
                return T3PLLa38ControlMath.FormatFrontLidState(lidAttached, lidOpen, language);
            if (kind == T3ControlKind.DoorTubes)
                return T3PLLa38ControlMath.FormatOpenState(doorTubesOpen, language);
            if (kind == T3ControlKind.CapBattery)
                return T3PLLa38ControlMath.FormatOpenState(capBatteryOpen, language);
            if (kind == T3ControlKind.CoverTopAntenna)
                return T3PLLa38ControlMath.FormatOpenState(coverAntennaOpen, language);
            return string.Empty;
        }

        private void CacheNodesAndColliders()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (!nodes.ContainsKey(t.name))
                    nodes[t.name] = t;
                RegisterColliderNode(t);
            }

            Transform lidNode;
            if (nodes.TryGetValue("Lid_Front", out lidNode) && lidNode != null)
            {
                Renderer[] rList = lidNode.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rList.Length; i++)
                    lidRenderers.Add(rList[i]);
            }
            ApplyLidVisibility();
        }

        private void RegisterColliderNode(Transform t)
        {
            BoxCollider bc = t.GetComponent<BoxCollider>();
            if (bc == null && t.name == "Handle_Side_Left_Body_Surface")
                bc = AddFallbackBoxCollider(t);
            if (bc == null)
                return;

            T3ControlKind kind = ResolveAncestorKind(t);
            if (kind == T3ControlKind.None)
                return;

            Vector3 raw = bc.size;
            float padX, padY, padZ;
            T3PLLa38ControlMath.PadControlColliderSize(kind, raw.x, raw.y, raw.z, out padX, out padY, out padZ);
            bc.size = new Vector3(padX, padY, padZ);
            colliderMap[bc] = kind;
            controlColliders.Add(bc);
            if (t.parent?.name == "Lid_Front")
                lidCollider = bc;
        }

        private static T3ControlKind ResolveAncestorKind(Transform t)
        {
            Transform cur = t.parent != null ? t.parent : t;
            for (int depth = 0; depth < 3 && cur != null; depth++)
            {
                T3ControlKind kind = T3PLLa38ControlMath.ClassifyControlNode(cur.name);
                if (kind != T3ControlKind.None)
                    return kind;
                cur = cur.parent;
            }
            return T3ControlKind.None;
        }

        private static BoxCollider AddFallbackBoxCollider(Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return null;

            BoxCollider bc = t.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.015f);
            bc.enabled = false;
            return bc;
        }

        private void ApplyLidVisibility()
        {
            for (int i = 0; i < lidRenderers.Count; i++)
            {
                if (lidRenderers[i] != null)
                    lidRenderers[i].enabled = lidAttached;
            }
            if (lidCollider != null)
            {
                bool active = controlColliders.Count > 0 && controlColliders[0]?.enabled == true;
                lidCollider.enabled = lidAttached && active;
            }
        }

        private void CloneEmissiveMaterials()
        {
            MeshRenderer[] mrs = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                Material[] mats = mr.sharedMaterials;
                if (mats == null)
                    continue;

                bool changed = false;
                for (int j = 0; j < mats.Length; j++)
                {
                    Material m = mats[j];
                    if (m == null)
                        continue;
                    if (m.name.StartsWith("T3_Dial", StringComparison.Ordinal))
                    {
                        if (dialMat == null) dialMat = new Material(m);
                        mats[j] = dialMat;
                        changed = true;
                    }
                    else if (m.name.StartsWith("T3_VoltmeterFace", StringComparison.Ordinal))
                    {
                        if (voltmeterMat == null) voltmeterMat = new Material(m);
                        mats[j] = voltmeterMat;
                        changed = true;
                    }
                    else if (m.name.StartsWith("T3_Filament", StringComparison.Ordinal))
                    {
                        if (filamentMat == null) filamentMat = new Material(m);
                        mats[j] = filamentMat;
                        changed = true;
                    }
                }
                if (changed)
                    mr.sharedMaterials = mats;
            }
        }

        private void ConfigureLightingAndProbes(Transform radioRoom)
        {
            LightProbeProxyVolume roomLppv = radioRoom.GetComponentInChildren<LightProbeProxyVolume>(true);
            MeshRenderer[] mrs = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                mr.receiveShadows = true;
                mr.shadowCastingMode = ShadowCastingMode.On;
                if (roomLppv != null)
                {
                    mr.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                    mr.lightProbeProxyVolumeOverride = roomLppv.gameObject;
                }
            }

            float frontSignZ = -1f;
            Transform doorNode;
            if (nodes.TryGetValue("Door_Tubes", out doorNode) && doorNode != null)
            {
                Vector3 inRig = transform.InverseTransformPoint(doorNode.position);
                if (Mathf.Abs(inRig.z) > 0.01f)
                    frontSignZ = inRig.z >= 0f ? 1f : -1f;
            }

            dialLight = CreatePointLight("T3_DialBacklight", new Vector3(0f, 0.21f, frontSignZ * 0.115f), new Color(1.0f, 0.72f, 0.34f), 0.45f);
            tubeLightLeft = CreatePointLight("T3_TubeGlow_L", new Vector3(-0.072f, 0.340f, frontSignZ * 0.068f), new Color(1.0f, 0.36f, 0.05f), 0.28f);
            tubeLightRight = CreatePointLight("T3_TubeGlow_R", new Vector3(0.072f, 0.340f, frontSignZ * 0.068f), new Color(1.0f, 0.36f, 0.05f), 0.28f);
        }

        private Light CreatePointLight(string objName, Vector3 localPos, Color color, float range)
        {
            GameObject lightGo = new GameObject(objName);
            lightGo.layer = InteriorLayer;
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = localPos;
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << InteriorLayer;
            return l;
        }

        private void TrySubscribeRadioEvents()
        {
            RadioHandler panel = RadioHandler.Instance;
            if (panel == null)
                return;

            cachedZone = panel.GetComponentInChildren<ZoneHandler>(true);
            cachedToggle = panel.GetComponentInChildren<ToggleHandler>(true);
            cachedToggleComp = cachedToggle != null ? cachedToggle.GetComponent<UnityEngine.UI.Toggle>() : null;
            cachedGround = panel.GetComponentInChildren<GroundHandler>(true);
            cachedVolume = panel.GetComponentInChildren<VolumeSpinnerHandler>(true);
            cachedFeedback = panel.GetComponentInChildren<FeedbackSpinnerHandler>(true);

            if (cachedZone != null) cachedZone.OnZoneChanged += OnModZoneChanged;
            if (cachedToggle != null) cachedToggle.OnValueChanged += OnModPowerChanged;
            if (cachedVolume != null) cachedVolume.OnValueChanged += OnModVolumeChanged;
            if (cachedFeedback != null) cachedFeedback.OnValueChanged += OnModFeedbackChanged;
            RadioHandler.OnFrequencyChanged += OnModFrequencyChanged;
            subscribedToRadio = true;

            SyncFromModState();
        }

        private void UnsubscribeRadioEvents()
        {
            if (!subscribedToRadio)
                return;
            if (cachedZone != null) cachedZone.OnZoneChanged -= OnModZoneChanged;
            if (cachedToggle != null) cachedToggle.OnValueChanged -= OnModPowerChanged;
            if (cachedVolume != null) cachedVolume.OnValueChanged -= OnModVolumeChanged;
            if (cachedFeedback != null) cachedFeedback.OnValueChanged -= OnModFeedbackChanged;
            RadioHandler.OnFrequencyChanged -= OnModFrequencyChanged;
            subscribedToRadio = false;
        }

        private void SyncFromModState()
        {
            powerOn = RadioPowerBus.IsRadioOn;
            warmup01 = powerOn ? 1f : 0f;
            if (cachedZone != null)
                bandIndex = T3PLLa38ControlMath.ClampInt(cachedZone.CurrentIndex, 0, 4);
            if (RadioHandler.Instance != null)
                dialBlenderDeg = T3PLLa38ControlMath.CursorPanelAngleToDialDeg(RadioHandler.Instance.CursorAngle);
            if (cachedVolume != null)
                volumeDeg = T3PLLa38ControlMath.SpinnerAngleToSymmetricDeg(cachedVolume.Angle, T3PLLa38ControlMath.VolumeMaxDeg);
            if (cachedFeedback != null)
                feedbackDeg = T3PLLa38ControlMath.SpinnerAngleToSymmetricDeg(cachedFeedback.Angle, T3PLLa38ControlMath.FeedbackMaxDeg);
            ApplyAllTransformsImmediate();
        }

        private void OnModPowerChanged(bool on)
        {
            powerOn = on;
            ApplyBlenderZ("Ctrl_Power_Aus_Ein", powerOn ? T3PLLa38ControlMath.PowerSwitchAngleDeg : -T3PLLa38ControlMath.PowerSwitchAngleDeg);
        }

        private void OnModZoneChanged(int index)
        {
            bandIndex = T3PLLa38ControlMath.ClampInt(index, 0, 4);
            ApplyBandTransform();
        }

        private void OnModVolumeChanged(float spinnerAngle)
        {
            volumeDeg = T3PLLa38ControlMath.SpinnerAngleToSymmetricDeg(spinnerAngle, T3PLLa38ControlMath.VolumeMaxDeg);
            ApplyBlenderY("Ctrl_Lautstaerke", volumeDeg);
        }

        private void OnModFeedbackChanged(float spinnerAngle)
        {
            feedbackDeg = T3PLLa38ControlMath.SpinnerAngleToSymmetricDeg(spinnerAngle, T3PLLa38ControlMath.FeedbackMaxDeg);
            ApplyBlenderY("Ctrl_Rueckkopplung", feedbackDeg);
        }

        private void OnModFrequencyChanged(float freqKHz, int zoneIdx)
        {
            if (RadioHandler.Instance != null)
                dialBlenderDeg = T3PLLa38ControlMath.CursorPanelAngleToDialDeg(RadioHandler.Instance.CursorAngle);
            bandIndex = T3PLLa38ControlMath.ClampInt(zoneIdx, 0, 4);
            ApplyDialTransform();
            ApplyBandTransform();
        }

        private void SetPowerFrom3D(bool on)
        {
            powerOn = on;
            ApplyBlenderZ("Ctrl_Power_Aus_Ein", powerOn ? T3PLLa38ControlMath.PowerSwitchAngleDeg : -T3PLLa38ControlMath.PowerSwitchAngleDeg);

            if (powerOn && cachedGround?.IsGroundEffectivelyActive() == false)
            {
                RadioPowerBus.SetGroundActive(true);
                if (cachedToggleComp != null)
                    cachedToggleComp.interactable = true;
            }
            if (cachedToggleComp != null)
                cachedToggleComp.isOn = powerOn;
            RadioPowerBus.SetRadioOn(powerOn);
        }

        private void SetBandFrom3D(int index)
        {
            bandIndex = T3PLLa38ControlMath.ClampInt(index, 0, 4);
            ApplyBandTransform();
            cachedZone?.SetZone(bandIndex);
        }

        private void SetDialFrom3D(float targetDialDeg)
        {
            dialBlenderDeg = T3PLLa38ControlMath.Clamp(targetDialDeg, -T3PLLa38ControlMath.DialHalfSpanDeg, T3PLLa38ControlMath.DialHalfSpanDeg);
            ApplyDialTransform();
            if (RadioHandler.Instance != null)
                RadioHandler.Instance.CursorAngle = T3PLLa38ControlMath.DialDegToCursorPanelAngle(dialBlenderDeg);
        }

        private void SetVolumeDeg(float targetDeg, bool pushToMod)
        {
            volumeDeg = T3PLLa38ControlMath.Clamp(targetDeg, -T3PLLa38ControlMath.VolumeMaxDeg, T3PLLa38ControlMath.VolumeMaxDeg);
            ApplyBlenderY("Ctrl_Lautstaerke", volumeDeg);
            if (pushToMod && cachedVolume != null)
                cachedVolume.SetAngle(T3PLLa38ControlMath.SymmetricDegToSpinnerAngle(volumeDeg, T3PLLa38ControlMath.VolumeMaxDeg));
        }

        private void SetFeedbackDeg(float targetDeg, bool pushToMod)
        {
            feedbackDeg = T3PLLa38ControlMath.Clamp(targetDeg, -T3PLLa38ControlMath.FeedbackMaxDeg, T3PLLa38ControlMath.FeedbackMaxDeg);
            ApplyBlenderY("Ctrl_Rueckkopplung", feedbackDeg);
            if (pushToMod && cachedFeedback != null)
                cachedFeedback.SetAngle(T3PLLa38ControlMath.SymmetricDegToSpinnerAngle(feedbackDeg, T3PLLa38ControlMath.FeedbackMaxDeg));
        }

        private void ApplyAllTransformsImmediate()
        {
            ApplyBlenderZ("Ctrl_Power_Aus_Ein", T3PLLa38ControlMath.ComputePowerSwitchBlenderDeg(powerOn));
            ApplyBandTransform();
            ApplyDialTransform();
            ApplyBlenderY("Ctrl_Lautstaerke", volumeDeg);
            ApplyBlenderY("Ctrl_Rueckkopplung", feedbackDeg);
            ApplyBlenderY("Ctrl_Regler_Peilen", reglerPeilenDeg);
            ApplyBlenderY("Ctrl_Regler_Seite", reglerSeiteDeg);
            ApplyBlenderY("Ctrl_Seite_Peilen", T3PLLa38ControlMath.ComputeSeitePeilenBlenderDeg(seiteMode));
            ApplyBlenderZ("Ctrl_Rund_Empfang", T3PLLa38ControlMath.ComputeRundSwitchBlenderDeg(rundEmpfangOn));
            ApplyBlenderY("Ctrl_H_Trimmer", hTrimmerDeg);
            ApplyBlenderY("Ctrl_Batterie_Rohren", T3PLLa38ControlMath.BattTestAnglesBlenderDeg[battTestIndex]);
            ApplyBlenderZ("Ctrl_Crank_Handle", T3PLLa38ControlMath.CrankHandleOpenDeg);
            lidCurrentDeg = lidOpen ? 162f : 0f;
            doorCurrentDeg = doorTubesOpen ? 95f : 0f;
            capCurrentDeg = capBatteryOpen ? 105f : 0f;
            coverCurrentDeg = coverAntennaOpen ? 90f : 0f;
            ApplyDoorRotations();
            UpdateVoltmeterNeedle();
            UpdateLighting();
        }

        private void StepDoorHinges()
        {
            float dt = Time.unscaledDeltaTime * 280f;
            lidCurrentDeg = Mathf.MoveTowards(lidCurrentDeg, lidOpen ? 162f : 0f, dt);
            doorCurrentDeg = Mathf.MoveTowards(doorCurrentDeg, doorTubesOpen ? 95f : 0f, dt);
            capCurrentDeg = Mathf.MoveTowards(capCurrentDeg, capBatteryOpen ? 105f : 0f, dt);
            coverCurrentDeg = Mathf.MoveTowards(coverCurrentDeg, coverAntennaOpen ? 90f : 0f, dt);
            ApplyDoorRotations();
        }

        private void ApplyDoorRotations()
        {
            ApplyBlenderZ("Lid_Front", -lidCurrentDeg);
            ApplyBlenderX("Door_Tubes", doorCurrentDeg);
            ApplyBlenderZ("Cap_Battery", capCurrentDeg);
            ApplyBlenderX("Cover_Top_Antenna", T3PLLa38ControlMath.ComputeTopAntennaCoverBlenderDeg(coverCurrentDeg));
        }

        private void ApplyBandTransform()
        {
            ApplyBlenderY("Ctrl_Band_Selector", T3PLLa38ControlMath.BandIndexToBlenderAngle(bandIndex));
            Transform slider;
            if (nodes.TryGetValue("Ctrl_Cursor_Band_Slider", out slider) && slider != null)
            {
                Vector3 lp = slider.localPosition;
                lp.z = T3PLLa38ControlMath.BandIndexToSliderLocalZ(bandIndex);
                slider.localPosition = lp;
            }
        }

        private void ApplyDialTransform()
        {
            ApplyBlenderY("Ctrl_Dial_Cursor", dialBlenderDeg);
            ApplyBlenderY("Ctrl_Tuning_Crank", dialBlenderDeg * T3PLLa38ControlMath.TuningCrankGearRatio);
        }

        private void UpdateVoltmeterNeedle()
        {
            float needleDeg = T3PLLa38ControlMath.ComputeVoltmeterNeedleBlenderDeg(powerOn, battTestIndex, hTrimmerDeg);
            ApplyBlenderY("Needle_Voltmeter", needleDeg * warmup01 + (-50.5f) * (1f - warmup01));
        }

        private void UpdateLighting()
        {
            T3LightingState ls = T3PLLa38ControlMath.ComputeLighting(powerOn, warmup01, hTrimmerDeg);
            dialMat?.SetColor(EmissionColorId, new Color(1.0f, 0.85f, 0.55f) * ls.DialEmission);
            voltmeterMat?.SetColor(EmissionColorId, new Color(1.0f, 0.78f, 0.42f) * ls.VoltmeterEmission);
            filamentMat?.SetColor(EmissionColorId, new Color(1.0f, 0.36f, 0.05f) * ls.FilamentEmission);
            if (dialLight != null)
                dialLight.intensity = ls.PointLightIntensity;

            float doorFactor = doorTubesOpen ? Mathf.Clamp01(doorCurrentDeg / 35f) : 0f;
            float tubeInt = ls.TubeLightIntensity * (0.04f + 0.96f * doorFactor);
            if (tubeLightLeft != null)
                tubeLightLeft.intensity = tubeInt;
            if (tubeLightRight != null)
                tubeLightRight.intensity = tubeInt;
        }

        private void ApplyBlenderX(string nodeName, float deg)
        {
            Transform t;
            if (nodes.TryGetValue(nodeName, out t) && t != null)
                t.localRotation = Quaternion.Euler(deg, 0f, 0f);
        }

        private void ApplyBlenderY(string nodeName, float deg)
        {
            Transform t;
            if (nodes.TryGetValue(nodeName, out t) && t != null)
                t.localRotation = Quaternion.Euler(0f, -deg, 0f);
        }

        private void ApplyBlenderZ(string nodeName, float deg)
        {
            Transform t;
            if (nodes.TryGetValue(nodeName, out t) && t != null)
                t.localRotation = Quaternion.Euler(0f, 0f, -deg);
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }
    }
}
