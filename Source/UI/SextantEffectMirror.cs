using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// 1:1 mirror of the main camera's rendering stack onto the sextant PIP
    /// cameras (periscope camera pattern): effect components are copied from
    /// the FPP camera so the SRP renders them with the same fog/post look.
    /// Extracted from SextantController (SRP: reflection plumbing is not
    /// device logic). Owns the member cache and the mirrored SSMS references
    /// the controller refreshes each frame.
    /// </summary>
    internal sealed class SextantEffectMirror
    {
        private readonly Dictionary<Type, MemberInfo[]> memberCache =
            new Dictionary<Type, MemberInfo[]>();

        /// <summary>Mirrored SSMS components on the PIP cameras (null until mirrored).</summary>
        public Component PipSsmsFog;
        public Component PipSsms;

        /// <summary>
        /// Copies the PostProcessLayer/PostProcessingBehaviour/SSMS components
        /// of the source camera onto the target GameObject, preserving the
        /// exact component order so the SRP/effect chain matches the
        /// reference render 1:1.
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
                if (name != "PostProcessLayer" && name != "PostProcessingBehaviour" &&
                    name != "SSMSGlobalFog" && name != "SSMS")
                    continue;

                Component source = comps[i];
                Component dst = go.AddComponent(t);
                MemberInfo[] members = GetMirrorMembersCached(t);
                CopyMembers(source, dst, members);
                if (name == "PostProcessLayer")
                    CopyPrivatePostLayerFields(source, dst);
                else if (name == "SSMSGlobalFog")
                    PipSsmsFog = dst;
                else if (name == "SSMS")
                    PipSsms = dst;
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
                catch { }
            }
        }

        private static void CopyPrivatePostLayerFields(Component from, Component to)
        {
            Type t = from.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.Name == "volumeLayer" || f.Name == "m_Resources")
                {
                    try { f.SetValue(to, f.GetValue(from)); } catch { }
                }
            }
            try
            {
                PostProcessResources[] allResources = Resources.FindObjectsOfTypeAll<PostProcessResources>();
                PostProcessLayer layer = to as PostProcessLayer;
                if (allResources != null && allResources.Length > 0 && layer != null)
                    layer.Init(allResources[0]);
            }
            catch { }
        }
    }
}
