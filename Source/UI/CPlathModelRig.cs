using System;
using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Calibrated glTFast model bases. Never infer a zero from an actuated pose.</summary>
    internal sealed class CPlathModelRig
    {
        private readonly Transform index, drum;
        private readonly Quaternion indexZero, drumZero;
        private readonly Transform[] shades = new Transform[7];
        private readonly Quaternion[] shadeZero = new Quaternion[7];
        private readonly CPlathShadeState shadeState = new CPlathShadeState();
        private readonly float[] activeAngle = { 76f, 76f, 78f, 80f, 0f, 0f, 0f };
        internal readonly Vector3 GripLocal;
        internal int LeftCount { get { return shadeState.LeftCount; } }
        internal int RightCount { get { return shadeState.RightCount; } }
        internal double Reading { get; private set; }

        internal CPlathModelRig(Transform wrapper)
        {
            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform t in wrapper.GetComponentsInChildren<Transform>(true)) nodes.Add(t.name, t);
            index = nodes["IndexArm_Pivot"]; drum = nodes["Micrometer_Drum_Pivot"];
            indexZero = index.localRotation * Quaternion.AngleAxis(-23.375f / 2f, Vector3.forward);
            drumZero = drum.localRotation * Quaternion.AngleAxis(135f, Vector3.right);
            GripLocal = wrapper.InverseTransformPoint(nodes["Grip_Point"].position);
            for (int i = 0; i < shades.Length; i++) BindShade(nodes, i);
            SetBankCounts(0, 0); Apply(23.375, 1f);
        }

        private void BindShade(Dictionary<string, Transform> nodes, int i)
        {
            bool isIndex = i < 4;
            int number = isIndex ? i + 1 : i - 3;
            shades[i] = nodes[(isIndex ? "Index_Shade_" : "Horizon_Shade_") + number.ToString("00") + "_Pivot"];
            float authored = isIndex ? i * 2f : new[] { 87f, 89f, 90f }[i - 4];
            shadeZero[i] = shades[i].localRotation * Quaternion.AngleAxis(authored, Vector3.up);
        }

        internal void SetBankCounts(int left, int right)
        {
            shadeState.SyncCounts(left, right);
        }

        internal void SetBankCount(int count, bool left)
        {
            shadeState.SelectBank(count, left);
        }

        internal void ToggleShade(int shade)
        {
            shadeState.Toggle(shade);
        }

        internal bool ShadeActive(int shade) { return shadeState.IsActive(shade); }

        internal void Apply(double degrees, float dt)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return;
            Reading = CPlathKinematics.Clamp(degrees);
            index.localRotation = indexZero * Quaternion.AngleAxis((float)CPlathKinematics.IndexDegrees(Reading), Vector3.forward);
            drum.localRotation = drumZero * Quaternion.AngleAxis((float)CPlathKinematics.DrumDegrees(Reading), Vector3.right);
            for (int i = 0; i < 7; i++)
            {
                float parked = i < 4 ? 0f : 90f;
                var target = shadeZero[i] * Quaternion.AngleAxis(-(shadeState.IsActive(i) ? activeAngle[i] : parked), Vector3.up);
                shades[i].localRotation = Quaternion.RotateTowards(shades[i].localRotation, target, 240f * Mathf.Max(0, dt));
            }
        }
    }
}
