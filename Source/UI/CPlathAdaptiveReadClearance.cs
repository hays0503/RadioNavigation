using System;
using System.Linq;
using UnityEngine;

namespace CPlath.AnimationImport
{
    /// <summary>Keep the reading grip outside the exported chest, without hiding it.</summary>
    public sealed class CPlathAdaptiveReadClearance
    {
        readonly Transform eye, instrument;
        readonly Transform[] upper, fore, hand;
        // ReadHold at zero reading: 25 mm still intersects; 30 mm first clears.
        // 40 mm is the candidate margin, subject to transition/mechanism gates.
        public const float ForwardMeters = .04f;
        // Original right-eye socket lies 30.65 mm from the head midline.
        // A measured 60 mm observer backoff reduces the eyepiece from 74% to
        // 29% of the vertical viewport at the game's 65-degree FOV. No scaling.
        public static readonly Vector3 AimObserverOffset = new Vector3(.030645642f, 0, .06f);
        // Rigid observer dolly, not a shortened reach or model scale. Keep the
        // entire instrument framed while making the micrometer ~1.4x larger.
        public static readonly Vector3 ReadObserverOffset = new Vector3(0, -.006f, -.10f);

        public CPlathAdaptiveReadClearance(Transform root, Transform eyeAnchor)
        {
            eye = eyeAnchor;
            if (!eye) throw new ArgumentNullException(nameof(eyeAnchor));
            var nodes = root.GetComponentsInChildren<Transform>(true);
            instrument = nodes.Single(t => t.name == "Sextant_Root");
            upper = new[] { "L", "R" }.Select(s => nodes.Single(t => t.name == "upperarm."+s)).ToArray();
            fore = new[] { "L", "R" }.Select(s => nodes.Single(t => t.name == "forearm."+s)).ToArray();
            hand = new[] { "L", "R" }.Select(s => nodes.Single(t => t.name == "hand."+s)).ToArray();
            if (instrument.parent != hand[1])
                throw new InvalidOperationException("Reading clearance requires the authored right-hand prop attachment");
        }

        public static float Weight(string clip, float sourceFrame)
        {
            float t;
            if (clip == "CPlath_ReadHold") return 1;
            if (clip == "CPlath_EnterRead") t = Mathf.Clamp01((sourceFrame-1)/19);
            else if (clip == "CPlath_ExitRead") t = 1-Mathf.Clamp01((sourceFrame-1)/16);
            else return 0;
            return t*t*(3-2*t);
        }

        // After graph evaluation, BEFORE the alidade-following left-arm solve.
        public void Apply(string clip, float sourceFrame)
        {
            float weight = Weight(clip, sourceFrame);
            if (weight <= 0) return;
            Vector3 shift = eye.forward*(ForwardMeters*weight);
            Vector3 left = hand[0].position+shift, right = hand[1].position+shift;
            // Sextant_Root is a child of hand.R. Moving the right wrist already
            // carries the prop: an additional root translation would double it
            // and would persist because the root's attachment is not animated.
            Solve(upper[0], fore[0], hand[0], left);
            Solve(upper[1], fore[1], hand[1], right);
        }

        public Quaternion ViewOffset(string clip, float sourceFrame)
        {
            float weight = Weight(clip, sourceFrame);
            if (weight <= 0) return Quaternion.identity;
            Vector3 direction = instrument.position-eye.position;
            if (direction.sqrMagnitude < .000001f)
                throw new InvalidOperationException("Reading inspection target coincides with the eye");
            Quaternion gaze = Quaternion.LookRotation(direction, eye.up);
            Quaternion localOffset = Quaternion.Inverse(eye.rotation)*gaze;
            return Quaternion.Slerp(Quaternion.identity, localOffset, weight);
        }

        public Vector3 ViewPosition(string clip, float sourceFrame)
        { return Vector3.Lerp(AimObserverOffset, ReadObserverOffset, Weight(clip, sourceFrame)); }

        static void Solve(Transform upper, Transform fore, Transform hand, Vector3 wrist)
        {
            Vector3 shoulder = upper.position, oldElbow = fore.position, oldWrist = hand.position;
            Quaternion u = upper.rotation, f = fore.rotation, h = hand.rotation;
            float a = Vector3.Distance(shoulder, oldElbow), b = Vector3.Distance(oldElbow, oldWrist);
            Vector3 direction = wrist-shoulder;
            float distance = direction.magnitude;
            if (distance <= Mathf.Abs(a-b)+.00001f || distance > (a+b)*.998f)
                throw new InvalidOperationException("Reading clearance grip exceeds arm reach");
            Vector3 axis = direction/distance;
            Vector3 hint = oldElbow-shoulder; hint -= axis*Vector3.Dot(hint, axis);
            if (hint.sqrMagnitude < 1e-8f) hint = Vector3.Cross(axis, Vector3.up);
            if (hint.sqrMagnitude < 1e-8f) hint = Vector3.Cross(axis, Vector3.right);
            float along = (a*a-b*b+distance*distance)/(2*distance);
            Vector3 elbow = shoulder+axis*along+hint.normalized*Mathf.Sqrt(Mathf.Max(0, a*a-along*along));
            upper.rotation = RotationBetween(oldElbow-shoulder, elbow-shoulder)*u;
            fore.rotation = RotationBetween(oldWrist-oldElbow, wrist-elbow)*f;
            hand.rotation = h;
        }

        static Quaternion RotationBetween(Vector3 from, Vector3 to)
        {
            // FromToRotation suppresses small rotations near the read seams;
            // atan2 keeps sub-millimetre wrist translations instead of snapping.
            Vector3 cross = Vector3.Cross(from, to);
            float dot = Vector3.Dot(from, to);
            if (cross.sqrMagnitude < 1e-20f)
                return dot >= 0 ? Quaternion.identity : Quaternion.FromToRotation(from, to);
            return Quaternion.AngleAxis(Mathf.Atan2(cross.magnitude, dot)*Mathf.Rad2Deg, cross.normalized);
        }
    }
}
