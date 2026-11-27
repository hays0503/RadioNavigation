using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Hides the vanilla T3 PLLa 38 stub geometry baked inside 'Radio Room 2' and 'Meter (5)'
    /// without altering the surrounding speaker box, bulkhead wall, clock, or transceiver.
    /// Uses RadioNav/Standard (DWS, StubCutout) cloned materials and trims RR.BoxCollider[3] upward.
    /// </summary>
    internal static class RadioDFStubRemover
    {
        private const string StubMeshChildName = "Radio Room 2";
        private const string MeterMeshChildName = "Meter (5)";

        private static readonly int StubCutEnabledId = Shader.PropertyToID("_StubCutEnabled");
        private static readonly int StubObjToRadioId = Shader.PropertyToID("_StubObjToRadio");
        private static readonly int StubCutMinId = Shader.PropertyToID("_StubCutMin");
        private static readonly int StubCutMaxId = Shader.PropertyToID("_StubCutMax");
        private static readonly int StubWireCutEnabledId = Shader.PropertyToID("_StubWireCutEnabled");
        private static readonly int StubWireMinId = Shader.PropertyToID("_StubWireMin");
        private static readonly int StubWireMaxId = Shader.PropertyToID("_StubWireMax");

        internal static bool Apply(Transform radioRoom, BoxCollider t3Collider, Shader cutoutShader, out T3StubCutoutBoxes boxes)
        {
            boxes = default(T3StubCutoutBoxes);
            if (radioRoom == null || t3Collider == null || cutoutShader == null)
                return false;

            boxes = T3PLLa38ControlMath.ComputeSlotGeometry(
                t3Collider.center.x, t3Collider.center.y, t3Collider.center.z,
                t3Collider.size.x, t3Collider.size.y, t3Collider.size.z);

            ApplyCutoutToChild(radioRoom, radioRoom.Find(StubMeshChildName), cutoutShader, boxes);
            ApplyCutoutToChild(radioRoom, radioRoom.Find(MeterMeshChildName), cutoutShader, boxes);
            TrimT3ColliderToUpperSpeaker(t3Collider, boxes);
            return true;
        }

        private static void ApplyCutoutToChild(Transform radioRoom, Transform target, Shader cutoutShader, T3StubCutoutBoxes boxes)
        {
            if (target == null)
                return;

            MeshRenderer mr = target.GetComponent<MeshRenderer>();
            if (mr == null)
                return;

            Material[] srcMats = mr.sharedMaterials;
            if (srcMats == null || srcMats.Length == 0)
                return;

            Matrix4x4 objToRadio = radioRoom.worldToLocalMatrix * target.localToWorldMatrix;
            Vector4 cutMin = new Vector4(boxes.MinX, boxes.MinY, boxes.MinZ, 0f);
            Vector4 cutMax = new Vector4(boxes.MaxX, boxes.MaxY, boxes.MaxZ, 0f);
            Vector4 wireMin = new Vector4(boxes.WireMinX, boxes.WireMinY, boxes.WireMinZ, 0f);
            Vector4 wireMax = new Vector4(boxes.WireMaxX, boxes.WireMaxY, boxes.WireMaxZ, 0f);

            var cache = new Dictionary<Material, Material>(srcMats.Length);
            Material[] cloned = new Material[srcMats.Length];
            for (int i = 0; i < srcMats.Length; i++)
            {
                Material src = srcMats[i];
                if (src == null)
                {
                    cloned[i] = null;
                    continue;
                }

                Material inst;
                if (!cache.TryGetValue(src, out inst))
                {
                    inst = new Material(src);
                    inst.name = src.name + " (T3StubCut)";
                    inst.shader = cutoutShader;
                    inst.SetFloat(StubCutEnabledId, 1f);
                    inst.SetMatrix(StubObjToRadioId, objToRadio);
                    inst.SetVector(StubCutMinId, cutMin);
                    inst.SetVector(StubCutMaxId, cutMax);
                    inst.SetFloat(StubWireCutEnabledId, 1f);
                    inst.SetVector(StubWireMinId, wireMin);
                    inst.SetVector(StubWireMaxId, wireMax);
                    cache[src] = inst;
                }
                cloned[i] = inst;
            }

            mr.sharedMaterials = cloned;
        }

        private static void TrimT3ColliderToUpperSpeaker(BoxCollider t3Collider, T3StubCutoutBoxes boxes)
        {
            // Shrink only once if not already trimmed.
            if (t3Collider.size.y <= boxes.SpeakerColSizeY + 0.02f)
                return;

            Vector3 c = t3Collider.center;
            Vector3 s = t3Collider.size;
            t3Collider.center = new Vector3(c.x, boxes.SpeakerColCenterY, c.z);
            t3Collider.size = new Vector3(s.x, boxes.SpeakerColSizeY, s.z);
        }
    }
}
