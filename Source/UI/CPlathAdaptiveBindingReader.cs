using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CPlath.AnimationImport;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Parse the bundle schema without Unity's type serializer. Roslyn-loaded
    /// mod types are not reliably populated by JsonUtility in the game player.
    /// The existing MiniJson parser already runs in this mod's game assembly.
    /// </summary>
    internal static class CPlathAdaptiveBindingReader
    {
        internal static CPlathAdaptiveMechanismRig.Configuration Read(string json)
        {
            var root = MiniJson.Parse(json);
            var rows = Required(root, "joints") as List<object>;
            if (rows == null || rows.Count != 9) throw new FormatException("C. Plath bindings require nine joint objects");
            var data = new CPlathAdaptiveMechanismRig.Configuration {
                joints = rows.Select(ReadJoint).ToArray(),
                animationTimeScale = Number(root, "animationTimeScale"),
                clearanceLocal = Vector(Object(Required(root, "clearanceLocal")))
            };
            if (data.animationTimeScale <= 0 || Mathf.Abs(data.clearanceLocal.sqrMagnitude-1) > .001f)
                throw new FormatException("C. Plath timing/clearance calibration is invalid");
            if (data.joints.Select(j => j.name).Distinct(StringComparer.Ordinal).Count() != 9 ||
                data.joints.Count(j => j.kind == "index") != 1 || data.joints.Count(j => j.kind == "drum") != 1 ||
                data.joints.Count(j => j.kind == "shade") != 7)
                throw new FormatException("C. Plath joint identities/roles are invalid");
            return data;
        }

        static CPlathAdaptiveMechanismRig.Binding ReadJoint(object value)
        {
            var row = Object(value); var rotation = Object(Required(row, "authoredRotation"));
            var joint = new CPlathAdaptiveMechanismRig.Binding {
                name = Text(row, "name"), kind = Text(row, "kind"), axis = Vector(Object(Required(row, "axis"))),
                authoredRotation = new Quaternion(Number(rotation, "x"), Number(rotation, "y"), Number(rotation, "z"), Number(rotation, "w")),
                authoredDegrees = Number(row, "authoredDegrees"), activeDegrees = Number(row, "activeDegrees"), parkedDegrees = Number(row, "parkedDegrees")
            };
            float q = Quaternion.Dot(joint.authoredRotation, joint.authoredRotation);
            if (Mathf.Abs(joint.axis.sqrMagnitude-1) > .001f || Mathf.Abs(q-1) > .001f)
                throw new FormatException("C. Plath joint axis/rotation is invalid: "+joint.name);
            return joint;
        }

        static Dictionary<string, object> Object(object value)
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new FormatException("C. Plath binding object required");
            return result;
        }
        static object Required(Dictionary<string, object> row, string name)
        {
            object value;
            if (!row.TryGetValue(name, out value) || value == null) throw new FormatException("C. Plath binding field missing: "+name);
            return value;
        }
        static string Text(Dictionary<string, object> row, string name)
        {
            var value = Required(row, name) as string;
            if (string.IsNullOrEmpty(value)) throw new FormatException("C. Plath binding string required: "+name);
            return value;
        }
        static float Number(Dictionary<string, object> row, string name)
        {
            var raw = Required(row, name);
            if (!(raw is double)) throw new FormatException("C. Plath binding number required: "+name);
            float value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("C. Plath binding number is nonfinite: "+name);
            return value;
        }
        static Vector3 Vector(Dictionary<string, object> row)
        { return new Vector3(Number(row, "x"), Number(row, "y"), Number(row, "z")); }
    }
}
