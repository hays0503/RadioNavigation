// Embedded source, NOT compiled into SourceCheck itself. Only the real player's
// Unity boundary is doubled; these are not animation/rendering/math simulations.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null); }
        public static bool operator !(Object value) { return ReferenceEquals(value, null); }
    }

    public static class Boundary
    {
        public static List<Object> Components = new List<Object>();
        public static Animations.AnimationMixerPlayable Mixer;
        public static int Evaluations;
    }

    public class Component : Object
    {
        public Transform transform;
        public T GetComponentInChildren<T>(bool includeInactive) where T : Object { return Boundary.Components.OfType<T>().FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Object { return Boundary.Components.OfType<T>().ToArray(); }
    }
    public class MonoBehaviour : Component { }
    public class Transform : Object
    {
        public Vector3 position, lossyScale = Vector3.one;
        public Quaternion rotation;
        public Vector3 InverseTransformPoint(Vector3 value) { throw new NotSupportedException("Camera math is outside this probe"); }
        public Vector3 TransformPoint(Vector3 value) { throw new NotSupportedException("Camera math is outside this probe"); }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { throw new NotSupportedException("Camera math is outside this probe"); }
    }
    public struct Vector3
    {
        public static Vector3 one { get { return new Vector3(); } }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { throw new NotSupportedException("Camera math is outside this probe"); } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { throw new NotSupportedException("Camera math is outside this probe"); }
    }
    public struct Quaternion
    {
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Inverse(Quaternion q) { throw new NotSupportedException("Camera math is outside this probe"); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { throw new NotSupportedException("Camera math is outside this probe"); }
        public static Vector3 operator *(Quaternion a, Vector3 b) { throw new NotSupportedException("Camera math is outside this probe"); }
    }
    public class AnimationClip : Object { public float length; }
    public class Material : Object { }
    public class Renderer : Component { public bool enabled = true; }
    public class SkinnedMeshRenderer : Renderer { public Material sharedMaterial; }
    public enum AnimatorCullingMode { AlwaysAnimate }
    public class Animator : Component { public bool enabled, applyRootMotion; public AnimatorCullingMode cullingMode; }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
    public static class Time { public static float deltaTime; }
}

namespace UnityEngine.Playables
{
    public enum DirectorUpdateMode { Manual }
    public struct PlayableGraph
    {
        private sealed class State { internal bool Valid = true; }
        private State state;
        public static PlayableGraph Create(string name) { return new PlayableGraph { state = new State() }; }
        public void SetTimeUpdateMode(DirectorUpdateMode mode) { }
        public void Connect(UnityEngine.Animations.AnimationClipPlayable clip, int a, UnityEngine.Animations.AnimationMixerPlayable mixer, int b) { mixer.Clips[b] = clip; }
        public void Play() { }
        public void Evaluate(float delta)
        {
            if (!IsValid()) throw new InvalidOperationException("Graph is not valid");
            UnityEngine.Boundary.Evaluations++;
        }
        public bool IsValid() { return state != null && state.Valid; }
        public void Destroy() { state.Valid = false; }
    }
}

namespace UnityEngine.Animations
{
    public class AnimationClipPlayable
    {
        public UnityEngine.AnimationClip Clip;
        public double Time;
        public static AnimationClipPlayable Create(UnityEngine.Playables.PlayableGraph graph, UnityEngine.AnimationClip clip) { return new AnimationClipPlayable { Clip = clip }; }
        public void SetSpeed(double speed) { }
        public void SetApplyFootIK(bool value) { }
        public void SetApplyPlayableIK(bool value) { }
        public void SetTime(double time) { Time = time; }
        public void SetDone(bool value) { }
    }
    public class AnimationMixerPlayable
    {
        public float[] Weights;
        public AnimationClipPlayable[] Clips;
        public static AnimationMixerPlayable Create(UnityEngine.Playables.PlayableGraph graph, int length)
        {
            return UnityEngine.Boundary.Mixer = new AnimationMixerPlayable { Weights = new float[length], Clips = new AnimationClipPlayable[length] };
        }
        public void SetInputWeight(int index, float weight) { Weights[index] = weight; }
    }
    public class AnimationPlayableOutput
    {
        public static AnimationPlayableOutput Create(UnityEngine.Playables.PlayableGraph graph, string name, UnityEngine.Animator animator) { return new AnimationPlayableOutput(); }
        public void SetSourcePlayable(AnimationMixerPlayable mixer) { }
    }
}
