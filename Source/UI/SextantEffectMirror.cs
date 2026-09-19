using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Mirror of the main camera's rendering stack onto the sextant PIP
    /// cameras (periscope camera pattern): the PPv2 PostProcessLayer and the
    /// SSMS fog components are copied from the FPP camera so the SRP renders
    /// the PIPs with the same post look.
    ///
    /// The legacy PPv1 stack (PostProcessingBehaviour) was deliberately NOT
    /// mirrored (2026-09-18: on the PIPs it blew bright lamps into giant
    /// bloom balls). That verdict predates the §16.17 root cause (the PIP
    /// pass was corrupted by the double GBuffer writer then), and the eyes
    /// verifiably grade through a live PPv1 ("UBOOT Postprocessing (FPP NG)"
    /// on the main camera, "UBOOT Postprocessing (Interior Camera)" on the
    /// interior camera — both enabled). Since the residual-light track
    /// (step 4) the PIPs mirror the live PPv1 profile BY REFERENCE
    /// (per-frame SyncPostProfile — the game swaps profiles per camera
    /// mode), gated by PlayerPrefs RadioNav.Ppv1Mirror (default 1).
    ///
    /// Extracted from SextantController (SRP: reflection plumbing is not
    /// device logic). Owns the member cache, the mirrored SSMS references
    /// the controller refreshes each frame, and the PPv2 auto-exposure sync.
    /// </summary>
    internal sealed class SextantEffectMirror
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        private readonly Dictionary<Type, MemberInfo[]> memberCache =
            new Dictionary<Type, MemberInfo[]>();

        // PPv2 auto-exposure sync: PPv2 keeps the adapted exposure PER
        // PostProcessLayer (AutoExposureRenderer's m_CurrentAutoExposure /
        // m_AutoExposurePool) and the game uses the effect (BrighterNights
        // adjusts its minLuminance; it is enabled at night). A fresh PIP
        // layer would run its own unadapted exposure and blow bright objects
        // up for tens of seconds, so the main layer's state is copied onto
        // the PIP layers every frame.
        private const string ExposureCurrentFieldName = "m_CurrentAutoExposure";
        private const string ExposurePoolFieldName = "m_AutoExposurePool";

        private PostProcessLayer sourcePostLayer;
        private readonly List<PostProcessLayer> targetPostLayers = new List<PostProcessLayer>();
        private readonly List<Component> targetPpv1s = new List<Component>();
        private const string Ppv1MirrorPrefsKey = "RadioNav.Ppv1Mirror";
        private bool ppv1Logged;
        private string ppv1LastProfile;
        private AutoExposureRenderer sourceAutoExposure;
        private readonly List<AutoExposureRenderer> targetAutoExposures = new List<AutoExposureRenderer>();
        private bool autoExposureLogged;
        private int autoExposureMisses;

        // Interior ambient term of the SSMS global fog. The game recomputes
        // it every frame from the interior light probes
        // (FPPCameraController.cs:1042, FreeFlyCameraController.cs:547,
        // GenericCameraController.cs:476, SectionViewCameraController.cs:903)
        // and the value drives the fog's ambient contribution in the
        // light-combine stage. The mirrored PIP SSMSGlobalFog is a one-off
        // clone, so without this sync it keeps the creation-time ambient ->
        // the light-combine term divides by a stale/zero ambient and the
        // ocular's HDR channel overflows to Inf/NaN (RenderDoc 2026-09-18:
        // t8=_GBufferZ sentinel + overflowed B -> black tiles with a colored
        // fringe). Member is looked up by reflection because SSMSGlobalFog
        // lives in the external SSMS package, not in the mod's references.
        private const string InteriorAmbientFieldName = "interiorAmbientColor";
        private MemberInfo sourceInteriorAmbientMember;
        private MemberInfo pipInteriorAmbientMember;
        private bool interiorAmbientResolveLogged;

        /// <summary>Mirrored SSMS components on the PIP cameras (null until mirrored).</summary>
        public Component PipSsmsFog;
        public Component PipSsms;

        /// <summary>
        /// Copies the PostProcessLayer/SSMS components of the source camera
        /// onto the target GameObject, preserving the component order so the
        /// effect chain matches the reference render. The legacy PPv1
        /// PostProcessingBehaviour is intentionally skipped (see class doc).
        /// </summary>
        public void AddMirroredEffectComponents(Camera sourceCamera, GameObject go)
        {
            if (sourceCamera == null)
                return;

            MonoBehaviour[] comps = sourceCamera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null)
                    continue;
                Type t = comps[i].GetType();
                string name = t.Name;
                if (name != "PostProcessLayer" && name != "SSMSGlobalFog" && name != "SSMS"
                    && name != "PostProcessingBehaviour")
                    continue;

                Component source = comps[i];
                Component dst = go.AddComponent(t);
                MemberInfo[] members = GetMirrorMembersCached(t);
                CopyMembers(source, dst, members);
                if (name == "PostProcessLayer")
                {
                    CopyPrivatePostLayerFields(source, dst);
                    if (sourcePostLayer == null)
                        sourcePostLayer = source as PostProcessLayer;
                    PostProcessLayer targetLayer = dst as PostProcessLayer;
                    if (targetLayer != null)
                        targetPostLayers.Add(targetLayer);
                }
                else if (name == "SSMSGlobalFog")
                    PipSsmsFog = dst;
                else if (name == "SSMS")
                    PipSsms = dst;
                else if (name == "PostProcessingBehaviour")
                {
                    // PPv1 profile arrives per-frame (SyncPostProfile): the
                    // game swaps profiles per camera mode, a creation-time
                    // copy would go stale. Enabled seeded true (matches the
                    // live source cameras); the sync owns it afterwards.
                    // Typed as Behaviour: the PPv1 assembly is not in the
                    // mod's compile references, so the profile travels as an
                    // object through the mirrored member (like SSMS).
                    Behaviour ppv1 = dst as Behaviour;
                    if (ppv1 != null)
                    {
                        ppv1.enabled = true;
                        targetPpv1s.Add(dst);
                    }
                }
            }
        }

        /// <summary>Refreshes the mirrored SSMS fields from the live sources.</summary>
        public void RefreshSsms(Component sourceSsmsFog, Component sourceSsms)
        {
            if (PipSsmsFog != null && sourceSsmsFog != null)
                CopyMembers(sourceSsmsFog, PipSsmsFog, GetMirrorMembersCached(sourceSsmsFog.GetType()));
            if (PipSsms != null && sourceSsms != null)
                CopyMembers(sourceSsms, PipSsms, GetMirrorMembersCached(sourceSsms.GetType()));
        }

        /// <summary>
        /// Per-frame sync of the legacy PPv1 profile reference from the
        /// source camera onto the mirrored PIP layers (residual-light track,
        /// step 4). The game swaps PPv1 profiles per camera mode (FPP /
        /// SectionView / Generic swap interior/exterior/periscope profiles),
        /// so the reference is re-synced every frame instead of copied once.
        /// Without this the ocular misses the eyes' warm grade
        /// ("UBOOT Postprocessing (FPP NG)" on the main camera) and neutral
        /// surfaces render cold/blue. Gated by PlayerPrefs
        /// RadioNav.Ppv1Mirror (default 1); gate off disables the PIP layers
        /// (pre-fix look) for A/B without redeploying.
        /// </summary>
        public void SyncPostProfile(Component sourcePpv1)
        {
            if (PlayerPrefs.GetInt(Ppv1MirrorPrefsKey, 1) == 0)
            {
                SetPpv1Enabled(false);
                return;
            }
            if (!TryGetProfile(sourcePpv1, out object profile, out bool sourceEnabled))
            {
                SetPpv1Enabled(false);
                return;
            }
            for (int i = 0; i < targetPpv1s.Count; i++)
            {
                Component target = targetPpv1s[i];
                if (target == null)
                    continue;
                if (!TrySetProfile(target, profile))
                    continue;
                Behaviour behaviour = target as Behaviour;
                if (behaviour != null && behaviour.enabled != sourceEnabled)
                    behaviour.enabled = sourceEnabled;
            }
            UnityEngine.Object profileObject = profile as UnityEngine.Object;
            string name = profileObject != null ? profileObject.name : "null";
            if (!ppv1Logged || ppv1LastProfile != name)
            {
                ppv1Logged = true;
                ppv1LastProfile = name;
                Debug.Log(LogPrefix + " PPV1 profile mirrored: " + name);
            }
        }

        private void SetPpv1Enabled(bool enabled)
        {
            for (int i = 0; i < targetPpv1s.Count; i++)
            {
                Behaviour behaviour = targetPpv1s[i] as Behaviour;
                if (behaviour != null && behaviour.enabled != enabled)
                    behaviour.enabled = enabled;
            }
        }

        private bool TryGetProfile(Component source, out object profile, out bool enabled)
        {
            profile = null;
            enabled = false;
            if (source == null)
                return false;
            Behaviour behaviour = source as Behaviour;
            if (behaviour != null)
                enabled = behaviour.enabled;
            FieldInfo field = FindProfileField(source);
            if (field == null)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.Ppv1ProfileField"))
                    Debug.LogWarning(LogPrefix + " PostProcessingBehaviour.profile not found - PPv1 not synced");
                return false;
            }
            try
            {
                profile = field.GetValue(source);
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.Ppv1ProfileRead"))
                    Debug.LogWarning(LogPrefix + " PPv1 profile read failed: " + e.Message);
                return false;
            }
        }

        private bool TrySetProfile(Component target, object profile)
        {
            FieldInfo field = FindProfileField(target);
            if (field == null)
                return false;
            try
            {
                if (Equals(field.GetValue(target), profile))
                    return true;
                field.SetValue(target, profile);
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.Ppv1ProfileWrite"))
                    Debug.LogWarning(LogPrefix + " PPv1 profile write failed: " + e.Message);
                return false;
            }
        }

        private FieldInfo FindProfileField(Component component)
        {
            if (component == null)
                return null;
            MemberInfo[] members = GetMirrorMembersCached(component.GetType());
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].Name == "profile" && members[i] is FieldInfo field)
                    return field;
            }
            return null;
        }

        /// <summary>
        /// Per-frame sync of the SSMS global fog's interior ambient term onto
        /// the mirrored PIP fog. The game recomputes that term from the
        /// interior light probes every frame (FPPCameraController.cs:1042 and
        /// the other camera controllers), but the PIP SSMSGlobalFog is cloned
        /// once with public members only - so the term stayed at its
        /// creation-time value and the PIP fog kept a stale ambient. Extended
        /// to a field lookup because the member is not part of the public
        /// mirror surface.
        /// </summary>
        public void SyncInteriorAmbient(Component sourceSsmsFog)
        {
            if (sourceSsmsFog == null || PipSsmsFog == null)
                return;
            if (sourceInteriorAmbientMember == null)
                sourceInteriorAmbientMember = TypeMemberCache.GetInstanceMember(
                    sourceSsmsFog.GetType(), InteriorAmbientFieldName);
            if (pipInteriorAmbientMember == null)
                pipInteriorAmbientMember = TypeMemberCache.GetInstanceMember(
                    PipSsmsFog.GetType(), InteriorAmbientFieldName);
            if (sourceInteriorAmbientMember == null || pipInteriorAmbientMember == null)
            {
                if (!interiorAmbientResolveLogged)
                {
                    interiorAmbientResolveLogged = true;
                    if (WarnOnce.ShouldLog("SextantEffectMirror.InteriorAmbientResolve"))
                        Debug.LogWarning(LogPrefix + " SSMSGlobalFog." + InteriorAmbientFieldName
                            + " not found - interior ambient not synced");
                }
                return;
            }

            try
            {
                object value = ReadMemberValue(sourceInteriorAmbientMember, sourceSsmsFog);
                if (value == null)
                    return;
                WriteMemberValue(pipInteriorAmbientMember, PipSsmsFog, value);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.InteriorAmbientCopy"))
                    Debug.LogWarning(LogPrefix + " interior ambient sync failed: " + e.Message);
            }
        }

        private static object ReadMemberValue(MemberInfo member, object instance)
        {
            FieldInfo f = member as FieldInfo;
            if (f != null)
                return f.GetValue(instance);
            return ((PropertyInfo)member).GetValue(instance, null);
        }

        private static void WriteMemberValue(MemberInfo member, object instance, object value)
        {
            FieldInfo f = member as FieldInfo;
            if (f != null)
                f.SetValue(instance, value);
            else
                ((PropertyInfo)member).SetValue(instance, value, null);
        }

        /// <summary>
        /// Per-frame sync of the PPv2 auto-exposure: copies the main camera
        /// layer's adapted exposure state (pool + current texture) onto every
        /// mirrored PIP layer. No-op while the game's auto-exposure is
        /// disabled (its state never appears on the source then).
        /// </summary>
        public void SyncAutoExposure()
        {
            if (sourcePostLayer == null || targetPostLayers.Count == 0)
                return;

            if (sourceAutoExposure == null)
                sourceAutoExposure = ResolveAutoExposure(sourcePostLayer);
            if (sourceAutoExposure == null)
                return; // bundles not built yet or auto-exposure not in the profile

            Type rendererType = sourceAutoExposure.GetType();
            FieldInfo poolField = TypeMemberCache.GetInstanceMember(rendererType, ExposurePoolFieldName) as FieldInfo;
            FieldInfo currentField = TypeMemberCache.GetInstanceMember(rendererType, ExposureCurrentFieldName) as FieldInfo;
            if (poolField == null && currentField == null)
            {
                CountAutoExposureMiss("no auto-exposure fields on " + rendererType.Name);
                return;
            }

            // Inactive auto-exposure (not in the current volumes): nothing to
            // sync, and no miss to report.
            if (!HasAutoExposureState(sourceAutoExposure, poolField, currentField))
                return;

            bool copied = false;
            for (int i = 0; i < targetPostLayers.Count; i++)
            {
                PostProcessLayer layer = targetPostLayers[i];
                if (layer == null)
                    continue;
                AutoExposureRenderer target = ResolveTargetAutoExposure(i, layer);
                if (target == null)
                    continue;
                copied |= CopyAutoExposureState(sourceAutoExposure, target, poolField, currentField);
            }

            if (copied)
            {
                autoExposureMisses = 0;
                if (!autoExposureLogged && SextantVectorLog.Enabled)
                {
                    autoExposureLogged = true;
                    Debug.Log(LogPrefix + " AUTOEXPOSURE synced onto " + targetPostLayers.Count + " PIP layer(s)");
                }
            }
            else
            {
                CountAutoExposureMiss("no auto-exposure textures to copy");
            }
        }

        private AutoExposureRenderer ResolveTargetAutoExposure(int index, PostProcessLayer layer)
        {
            while (targetAutoExposures.Count <= index)
                targetAutoExposures.Add(null);
            AutoExposureRenderer resolved = targetAutoExposures[index];
            if (resolved == null)
            {
                resolved = ResolveAutoExposure(layer);
                targetAutoExposures[index] = resolved;
            }
            return resolved;
        }

        private static AutoExposureRenderer ResolveAutoExposure(PostProcessLayer layer)
        {
            try
            {
                PostProcessBundle bundle;
                if (!layer.TryGetBundle<AutoExposure>(out bundle) || bundle == null)
                    return null;
                return bundle.renderer as AutoExposureRenderer;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.AutoExposureResolve"))
                    Debug.LogWarning(LogPrefix + " auto-exposure resolve failed: " + e.Message);
                return null;
            }
        }

        private static bool HasAutoExposureState(AutoExposureRenderer renderer, FieldInfo poolField, FieldInfo currentField)
        {
            if (currentField != null && currentField.GetValue(renderer) as RenderTexture != null)
                return true;
            return HasPoolTextures(poolField == null ? null : poolField.GetValue(renderer) as RenderTexture[][]);
        }

        private static bool HasPoolTextures(RenderTexture[][] pool)
        {
            if (pool == null)
                return false;
            for (int eye = 0; eye < pool.Length; eye++)
            {
                if (pool[eye] == null)
                    continue;
                for (int i = 0; i < pool[eye].Length; i++)
                {
                    if (pool[eye][i] != null)
                        return true;
                }
            }
            return false;
        }

        private static bool CopyAutoExposureState(AutoExposureRenderer source, AutoExposureRenderer target,
            FieldInfo poolField, FieldInfo currentField)
        {
            bool copied = false;
            if (poolField != null)
            {
                RenderTexture[][] sourcePool = poolField.GetValue(source) as RenderTexture[][];
                RenderTexture[][] targetPool = poolField.GetValue(target) as RenderTexture[][];
                if (sourcePool != null && targetPool != null)
                {
                    int eyes = Math.Min(sourcePool.Length, targetPool.Length);
                    for (int eye = 0; eye < eyes; eye++)
                    {
                        if (sourcePool[eye] == null || targetPool[eye] == null)
                            continue;
                        int count = Math.Min(sourcePool[eye].Length, targetPool[eye].Length);
                        for (int i = 0; i < count; i++)
                            copied |= CopyRenderTexture(sourcePool[eye][i], targetPool[eye][i]);
                    }
                }
            }
            if (currentField != null)
            {
                copied |= CopyRenderTexture(currentField.GetValue(source) as RenderTexture,
                    currentField.GetValue(target) as RenderTexture);
            }
            return copied;
        }

        /// <summary>
        /// Miss counter for the exposure sync: the first frames after the
        /// sextant opens legitimately have no textures yet, so only a
        /// sustained failure (5 s at 60 fps) dumps the resolve state once.
        /// </summary>
        private void CountAutoExposureMiss(string reason)
        {
            autoExposureMisses++;
            if (autoExposureMisses == 300)
                LogAutoExposureDiag(reason);
        }

        private void LogAutoExposureDiag(string reason)
        {
            if (!WarnOnce.ShouldLog("SextantEffectMirror.AutoExposureDiag"))
                return;
            string state = "srcLayer=" + (sourcePostLayer != null)
                + " targets=" + targetPostLayers.Count
                + " srcAE=" + (sourceAutoExposure != null);
            if (sourceAutoExposure != null)
            {
                Type type = sourceAutoExposure.GetType();
                FieldInfo pool = TypeMemberCache.GetInstanceMember(type, ExposurePoolFieldName) as FieldInfo;
                FieldInfo current = TypeMemberCache.GetInstanceMember(type, ExposureCurrentFieldName) as FieldInfo;
                state += " curF=" + (current != null) + " poolF=" + (pool != null)
                    + " srcCur=" + DescribeExposureRt(current == null ? null : current.GetValue(sourceAutoExposure) as RenderTexture)
                    + " srcPool=" + DescribeAutoExposurePool(pool == null ? null : pool.GetValue(sourceAutoExposure) as RenderTexture[][])
                    + " srcSet(" + DescribeAutoExposureSettings(sourcePostLayer) + ")";
            }
            for (int i = 0; i < targetPostLayers.Count; i++)
            {
                PostProcessLayer layer = targetPostLayers[i];
                if (layer == null)
                {
                    state += " t" + i + "=null";
                    continue;
                }
                AutoExposureRenderer target = ResolveTargetAutoExposure(i, layer);
                if (target == null)
                {
                    state += " t" + i + "=noRenderer(bundles=" + layer.haveBundlesBeenInited + ")";
                    continue;
                }
                FieldInfo targetPool = TypeMemberCache.GetInstanceMember(target.GetType(), ExposurePoolFieldName) as FieldInfo;
                FieldInfo targetCurrent = TypeMemberCache.GetInstanceMember(target.GetType(), ExposureCurrentFieldName) as FieldInfo;
                state += " t" + i + "=cur(" + DescribeExposureRt(targetCurrent == null ? null : targetCurrent.GetValue(target) as RenderTexture)
                    + ") pool(" + DescribeAutoExposurePool(targetPool == null ? null : targetPool.GetValue(target) as RenderTexture[][]) + ")"
                    + " set(" + DescribeAutoExposureSettings(layer) + ")";
            }
            Debug.LogWarning(LogPrefix + " auto-exposure sync diag (" + reason + "): " + state);
        }

        private static string DescribeAutoExposurePool(RenderTexture[][] pool)
        {
            if (pool == null)
                return "null";
            string text = "eyes=" + pool.Length;
            for (int eye = 0; eye < pool.Length; eye++)
                text += " [" + eye + "]=" + DescribeExposurePool(pool[eye]);
            return text;
        }

        private static string DescribeExposurePool(RenderTexture[] pool)
        {
            if (pool == null)
                return "null";
            string text = "len=" + pool.Length;
            for (int i = 0; i < pool.Length; i++)
                text += " [" + i + "]=" + DescribeExposureRt(pool[i]);
            return text;
        }

        private static string DescribeExposureRt(RenderTexture rt)
        {
            if (rt == null)
                return "null";
            return rt.width + "x" + rt.height + ":" + rt.format + ":created=" + rt.IsCreated();
        }

        private static string DescribeAutoExposureSettings(PostProcessLayer layer)
        {
            try
            {
                PostProcessBundle bundle;
                if (!layer.TryGetBundle<AutoExposure>(out bundle) || bundle == null)
                    return "noBundle";
                PostProcessEffectSettings settings = bundle.settings;
                if (settings == null)
                    return "noSettings";
                return "enabled=" + settings.enabled.value + " active=" + settings.active;
            }
            catch (Exception e)
            {
                return "err=" + e.GetType().Name;
            }
        }

        /// <summary>
        /// GPU copy of one exposure texture. Same size+format → CopyTexture
        /// (no render-state change); a package update changing the descriptor
        /// falls back to Blit, which converts formats.
        /// </summary>
        private static bool CopyRenderTexture(RenderTexture source, RenderTexture target)
        {
            if (source == null || target == null || !source.IsCreated() || !target.IsCreated())
                return false;
            try
            {
                if (source.width == target.width && source.height == target.height &&
                    source.format == target.format)
                {
                    Graphics.CopyTexture(source, target);
                }
                else
                {
                    RenderTexture previous = RenderTexture.active;
                    Graphics.Blit(source, target);
                    RenderTexture.active = previous;
                }
                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.ExposureCopy"))
                    Debug.LogWarning(LogPrefix + " exposure copy failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Copies all public writable members of the source component onto
        /// the target (used for the PlayWay WaterCamera config: a raw added
        /// component is empty, the PIP camera needs the main camera's water
        /// reference and enable flags).
        /// </summary>
        public void MirrorComponent(Component source, Component target)
        {
            CopyMembers(source, target, GetMirrorMembersCached(source.GetType()));
        }

        private MemberInfo[] GetMirrorMembersCached(Type t)
        {
            MemberInfo[] cached;
            if (!memberCache.TryGetValue(t, out cached))
            {
                cached = GetMirrorMembers(t);
                memberCache[t] = cached;
            }
            return cached;
        }

        private static MemberInfo[] GetMirrorMembers(Type t)
        {
            var list = new List<MemberInfo>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsStatic)
                    continue;
                list.Add(f);
            }
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0)
                    continue;
                // UnityEngine.Object identity members (name/hideFlags) are not
                // component settings: copying `name` renames the target
                // GameObject — the PIP cameras ended up named "Main Camera",
                // breaking every name-based lookup and log line (2026-09-17).
                if (p.DeclaringType == typeof(UnityEngine.Object))
                    continue;
                list.Add(p);
            }
            return list.ToArray();
        }

        private static void CopyMembers(Component from, Component to, MemberInfo[] members)
        {
            if (members == null)
                return;
            for (int i = 0; i < members.Length; i++)
            {
                try
                {
                    FieldInfo f = members[i] as FieldInfo;
                    if (f != null)
                        f.SetValue(to, f.GetValue(from));
                    else
                        ((PropertyInfo)members[i]).SetValue(to,
                            ((PropertyInfo)members[i]).GetValue(from, null), null);
                }
                catch (Exception e)
                {
                    // A game update renaming post-stack members must surface
                    // once instead of diverging the PIP look silently.
                    if (WarnOnce.ShouldLog("SextantEffectMirror.CopyMembers." + members[i].Name))
                        Debug.LogWarning("[RadioNav][Sextant] CopyMembers failed for '" + members[i].Name + "': " + e.Message);
                }
            }
        }

        private static void CopyPrivatePostLayerFields(Component from, Component to)
        {
            Type t = from.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.Name == "volumeLayer" || f.Name == "m_Resources")
                {
                    try { f.SetValue(to, f.GetValue(from)); }
                    catch (Exception e)
                    {
                        if (WarnOnce.ShouldLog("SextantEffectMirror.CopyPrivatePostLayerFields." + f.Name))
                            Debug.LogWarning("[RadioNav][Sextant] post-layer field '" + f.Name + "' copy failed: " + e.Message);
                    }
                }
            }
            try
            {
                PostProcessResources[] allResources = Resources.FindObjectsOfTypeAll<PostProcessResources>();
                PostProcessLayer layer = to as PostProcessLayer;
                if (allResources != null && allResources.Length > 0 && layer != null)
                    layer.Init(allResources[0]);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantEffectMirror.CopyPrivatePostLayerFields.Init"))
                    Debug.LogWarning("[RadioNav][Sextant] post-layer Init failed: " + e.Message);
            }
        }
    }
}
