using System;
using System.Linq;
using UnityEngine;

namespace CPlath.AnimationImport
{
    /// <summary>Uses measured FBX bindings, never the previous glTF axis constants.</summary>
    public sealed class CPlathAdaptiveMechanismRig
    {
        [Serializable] public sealed class Binding
        {
            public string name, kind;
            public Vector3 axis;
            public Quaternion authoredRotation;
            public float authoredDegrees, activeDegrees, parkedDegrees;
        }
        [Serializable] public sealed class Configuration
        { public Binding[] joints; public float animationTimeScale; public Vector3 clearanceLocal; }
        readonly Binding[] bindings;
        readonly Transform[] joints;
        readonly Transform upper, fore, hand;
        readonly Transform instrument;
        readonly Vector3 palmLocalAxis;
        readonly Vector3 clearanceLocal;
        readonly int index, drum;
        public double Reading { get; private set; }
        public float AnimationTimeScale { get; private set; }
        public float ReachWithdrawalMillimeters { get; private set; }
        public float WristBendDegrees { get; private set; }
        float ergonomicSwivel, releasedGrip;
        public static float LeftGripWeight(string clipName, float sourceFrame)
        {
            if (clipName != "CPlath_RaiseHoldLower") return 1;
            float t = sourceFrame <= 16 ? Mathf.Clamp01((sourceFrame-8)/8) :
                sourceFrame >= 56 ? Mathf.Clamp01((68-sourceFrame)/12) : 1;
            return t*t*(3-2*t);
        }

        public CPlathAdaptiveMechanismRig(Transform root, string json)
            : this(root, JsonUtility.FromJson<Configuration>(json))
        {
        }

        public CPlathAdaptiveMechanismRig(Transform root, Configuration data)
        {
            if (data == null || data.joints == null || data.joints.Length != 9)
                throw new ArgumentException("Nine calibrated mechanical joints required", nameof(data));
            bindings = data.joints;
            AnimationTimeScale = data.animationTimeScale > 0 ? data.animationTimeScale : 1;
            var nodes = root.GetComponentsInChildren<Transform>(true);
            instrument = nodes.Single(n => n.name == "Sextant_Root");
            clearanceLocal = data.clearanceLocal;
            joints = bindings.Select(b => nodes.Single(n => n.name == b.name)).ToArray();
            index = Array.FindIndex(bindings, b => b.kind == "index");
            drum = Array.FindIndex(bindings, b => b.kind == "drum");
            if (index < 0 || drum < 0) throw new ArgumentException("Missing calibrated alidade/drum");
            foreach (var b in bindings)
                if (Mathf.Abs(b.axis.sqrMagnitude-1) > .001f) throw new ArgumentException("Invalid calibrated axis");
            upper = nodes.Single(n => n.name == "upperarm.L");
            fore = nodes.Single(n => n.name == "forearm.L");
            hand = nodes.Single(n => n.name == "hand.L");
            palmLocalAxis = hand.InverseTransformPoint(nodes.Single(n => n.name == "fingers1.L").position).normalized;
        }

        // Called after the animation graph evaluates, before eye registration.
        // The graph restores the original arm pose each tick; offsets never accumulate.
        public void Apply(double degrees, Func<int, bool> shadeActive, float deltaTime, float leftGripWeight)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) throw new ArgumentException("Finite reading required");
            Reading = Math.Max(0, Math.Min(130, degrees));
            joints[index].localRotation = bindings[index].authoredRotation;
            Matrix4x4 oldIndex = joints[index].localToWorldMatrix;
            Vector3 wrist = hand.position;
            Quaternion wristRotation = hand.rotation;
            Rotate(index, (float)(Reading/2));
            Rotate(drum, (float)(-360*(Reading-Math.Floor(Reading))));
            int shade = 0;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].kind != "shade") continue;
                // Calibration lists horizon 1..3 then index 1..4. UI state is
                // index 1..4 then horizon 1..3, matching existing filter input.
                int stateIndex = shade < 3 ? shade+4 : shade-3;
                float angle = shadeActive(stateIndex) ? bindings[i].activeDegrees : bindings[i].parkedDegrees;
                Quaternion target = bindings[i].authoredRotation * Quaternion.AngleAxis(angle-bindings[i].authoredDegrees, bindings[i].axis);
                joints[i].localRotation = Quaternion.RotateTowards(joints[i].localRotation, target, 240*Mathf.Max(0, deltaTime));
                shade++;
            }
            ReachWithdrawalMillimeters = 0;
            float weight = Mathf.Clamp01(leftGripWeight);
            if (weight <= 0) return;
            // At large readings the elbow must travel outwards with the drum.
            // Keeping the old elbow pole folds the wrist to ~86 degrees at 130.
            // This changes only the elbow plane, never the grasp/finger target.
            float highArc = Mathf.Clamp01(((float)Reading-70)/60);
            ergonomicSwivel = -70*highArc*highArc*(3-2*highArc)*weight;
            releasedGrip = 0;
            Matrix4x4 change = joints[index].localToWorldMatrix * oldIndex.inverse;
            Vector3 desiredWrist = Vector3.Lerp(wrist, change.MultiplyPoint3x4(wrist), weight);
            Quaternion desiredRotation = Quaternion.Slerp(wristRotation, change.rotation * wristRotation, weight);
            // A measured 43-candidate contact probe found that a 0.5 mm move
            // below the fixed foot clears the local fingertip contact. Use
            // 1 mm with smooth entry/exit, only around that part of the sweep.
            float enterClearance = Mathf.Clamp01(((float)Reading-75)/5);
            float exitClearance = Mathf.Clamp01((95-(float)Reading)/5);
            float footClearance = enterClearance*enterClearance*(3-2*enterClearance)*
                                  exitClearance*exitClearance*(3-2*exitClearance);
            desiredWrist += instrument.TransformDirection(clearanceLocal)*(.001f*footClearance*weight);
            float reach = (Vector3.Distance(upper.position, fore.position)+Vector3.Distance(fore.position, hand.position))*.998f;
            float actuation = Mathf.Clamp01(Mathf.Abs((float)Reading-bindings[index].authoredDegrees*2)/5);
            actuation = actuation*actuation*(3-2*actuation);
            float distanceToGrip = Vector3.Distance(upper.position, desiredWrist);
            float excess = Mathf.Max(0, distanceToGrip-reach);
            // Begin release before the arm locks straight, but leave the authored
            // default-angle clips unchanged. Withdraw below, not through, the rim.
            float releaseDistance = Mathf.Max(0, distanceToGrip-reach+.02f*actuation);
            float releaseT = Mathf.Clamp01(releaseDistance/.04f);
            float release = releaseT*releaseT*(3-2*releaseT);
            // During a high-angle read transition, full contact may require an
            // impossible wrist even before arm reach is exhausted. Release and
            // re-approach below the rim instead of forcing that posture.
            float a = Vector3.Distance(upper.position, fore.position);
            float b = Vector3.Distance(fore.position, hand.position);
            float d = Mathf.Clamp(distanceToGrip, Mathf.Abs(a-b)+.00001f, reach);
            float along = (a*a-b*b+d*d)/(2*d);
            float height = Mathf.Sqrt(Mathf.Max(0, a*a-along*along));
            float foreCone = Mathf.Atan2(height, d-along)*Mathf.Rad2Deg;
            float palmCone = Vector3.Angle(desiredWrist-upper.position, desiredRotation*palmLocalAxis);
            float minimumWristBend = Mathf.Abs(palmCone-foreCone);
            float wristRelease = Mathf.Clamp01((minimumWristBend-35)/20);
            wristRelease = wristRelease*wristRelease*(3-2*wristRelease)*highArc*highArc*(3-2*highArc)*weight;
            release = Mathf.Max(release, wristRelease);
            if (release > 0)
            {
                if (clearanceLocal.sqrMagnitude < .99f) throw new InvalidOperationException("Measured clearance direction required for unreachable grip");
                desiredWrist += instrument.TransformDirection(clearanceLocal)*(.30f*release);
                desiredRotation = Quaternion.Slerp(desiredRotation, wristRotation, release);
                // Do not rotate a still-touching fingertip into the knob.
                // Start relaxing only after 30 mm of withdrawal; finish at
                // 270 mm, safely outside the micrometer's grasp envelope. The
                // broad ramp also avoids a rapid wrist snap as the arc changes.
                float relax = Mathf.Clamp01((release-.10f)/.80f);
                releasedGrip = relax*relax*(3-2*relax);
                ReachWithdrawalMillimeters = excess*1000;
            }
            SolveLeftArm(desiredWrist, desiredRotation);
        }

        void Rotate(int i, float absoluteDegrees)
        {
            joints[i].localRotation = bindings[i].authoredRotation * Quaternion.AngleAxis(absoluteDegrees-bindings[i].authoredDegrees, bindings[i].axis);
        }

        void SolveLeftArm(Vector3 wrist, Quaternion rotation)
        {
            Vector3 shoulder = upper.position, oldElbow = fore.position, oldWrist = hand.position;
            Quaternion upperRotation = upper.rotation, foreRotation = fore.rotation;
            float a = Vector3.Distance(shoulder, oldElbow), b = Vector3.Distance(oldElbow, oldWrist);
            Vector3 direction = wrist-shoulder;
            float distance = direction.magnitude, maximum = (a+b)*.998f;
            if (distance <= Mathf.Abs(a-b)+.00001f) throw new InvalidOperationException("Left wrist too close to shoulder");
            Vector3 axis = direction/distance;
            if (distance > maximum)
            {
                ReachWithdrawalMillimeters = Mathf.Max(ReachWithdrawalMillimeters, (distance-maximum)*1000);
                distance = maximum; wrist = shoulder+axis*distance;
            }
            Vector3 hint = oldElbow-shoulder;
            hint -= axis*Vector3.Dot(hint, axis);
            if (hint.sqrMagnitude < .00000001f) hint = Vector3.Cross(axis, Vector3.forward);
            if (hint.sqrMagnitude < .00000001f) hint = Vector3.Cross(axis, Vector3.up);
            hint = Quaternion.AngleAxis(ergonomicSwivel, axis)*hint;
            float along = (a*a-b*b+distance*distance)/(2*distance);
            Vector3 elbow = shoulder+axis*along+hint.normalized*Mathf.Sqrt(Mathf.Max(0, a*a-along*along));
            upper.rotation = Quaternion.FromToRotation(oldElbow-shoulder, elbow-shoulder)*upperRotation;
            fore.rotation = Quaternion.FromToRotation(oldWrist-oldElbow, wrist-elbow)*foreRotation;
            hand.rotation = rotation;
            // When the grip is out of reach the existing solver releases it.
            // A released hand should relax along the forearm, not keep the
            // drum's orientation at the end of an almost-straight arm.
            if (releasedGrip > 0)
            {
                Quaternion relaxed = Quaternion.FromToRotation(hand.TransformDirection(palmLocalAxis), hand.position-fore.position)*rotation;
                hand.rotation = Quaternion.Slerp(rotation, relaxed, releasedGrip);
            }
            WristBendDegrees = Vector3.Angle(hand.position-fore.position, hand.TransformDirection(palmLocalAxis));
        }
    }
}
