using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CPlath.AnimationImport
{
    /// <summary>
    /// Input-neutral player for the four validated Generic clips. Add from mod
    /// source AFTER loading the script-free prefab/bundle. Never owns Z/RMB/PIP,
    /// native FPP visibility, materials or the game's camera lifecycle.
    /// </summary>
    public sealed class CPlathAdaptiveAnimationPlayer : MonoBehaviour
    {
        public enum Phase { Stowed, Raising, Aiming, EnteringRead, Reading, ExitingRead, Lowering }
        public Phase CurrentPhase { get; private set; }
        public bool IsVisible => CurrentPhase != Phase.Stowed;
        public event Action<bool> VisibilityChanged;
        public bool AutomaticUpdate = true;
        public float PlaybackSpeed = 1;
        public Transform EyeAnchor { get; private set; }
        public AnimationClip CurrentClip { get; private set; }
        public double CurrentTime { get; private set; }
        public float ClipTimeScale { get; private set; }
        public double SampleTime => CurrentTime*ClipTimeScale;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        Renderer[] ownedRenderers;
        bool equipped, reading, initialized, wasVisible;
        double clock;
        const double AimTime = 23.0 / 24; // Same authored aim used by Enter/Exit seams.

        public void Initialize(AnimationClip primary, AnimationClip enter, AnimationClip hold, AnimationClip exit, float clipTimeScale = 1)
        {
            if (initialized) throw new InvalidOperationException("Player already initialized");
            if (float.IsNaN(clipTimeScale) || float.IsInfinity(clipTimeScale) || clipTimeScale <= 0) throw new ArgumentOutOfRangeException(nameof(clipTimeScale));
            ClipTimeScale = clipTimeScale;
            clips = new[] { primary, enter, hold, exit };
            foreach (var clip in clips) if (!clip) throw new ArgumentNullException(nameof(clip));
            var animator = GetComponentInChildren<Animator>(true);
            if (!animator) throw new InvalidOperationException("Generic Animator missing");
            foreach (var node in GetComponentsInChildren<Transform>(true))
                if (node.name == "CPlath_EyeAnchor") EyeAnchor = node;
            if (!EyeAnchor) throw new InvalidOperationException("Verified eye registration missing");
            ownedRenderers = GetComponentsInChildren<Renderer>(true);
            animator.enabled = true; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("CPlathAdaptivePlayer");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetSpeed(0); playables[i].SetApplyFootIK(false); playables[i].SetApplyPlayableIK(false);
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "CPlath", animator).SetSourcePlayable(mixer);
            graph.Play(); initialized = true; CurrentPhase = Phase.Stowed; clock = 0;
            equipped = reading = wasVisible = false;
            Evaluate();
        }

        public void SetDesired(bool isEquipped, bool isReading)
        { equipped = isEquipped; reading = isReading; }

        public void Advance(float deltaTime)
        {
            if (!initialized) throw new InvalidOperationException("Initialize before advancing");
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            double dt = deltaTime * Mathf.Max(0, PlaybackSpeed);
            switch (CurrentPhase)
            {
                case Phase.Stowed: StepStowed(); break;
                case Phase.Raising: StepRaising(dt); break;
                case Phase.Aiming: StepAiming(); break;
                case Phase.EnteringRead: StepEnteringRead(dt); break;
                case Phase.Reading: StepReading(); break;
                case Phase.ExitingRead: StepExitingRead(dt); break;
                case Phase.Lowering: StepLowering(dt); break;
            }
            Evaluate();
        }

        void StepStowed()
        { if (equipped) { CurrentPhase = Phase.Raising; clock = 0; } }

        void StepRaising(double dt)
        {
            clock = Math.Max(0, Math.Min(AimTime, clock + (equipped ? dt : -dt)));
            if (!equipped && clock <= 0) CurrentPhase = Phase.Stowed;
            else if (equipped && clock >= AimTime) CurrentPhase = Phase.Aiming;
        }

        void StepAiming()
        {
            if (!equipped) { CurrentPhase = Phase.Lowering; clock = AimTime; }
            else if (reading) { CurrentPhase = Phase.EnteringRead; clock = 0; }
        }

        void StepEnteringRead(double dt)
        {
            bool forward = equipped && reading;
            clock = Math.Max(0, Math.Min(clips[1].length/ClipTimeScale, clock + (forward ? dt : -dt)));
            if (!forward && clock <= 0) CurrentPhase = Phase.Aiming;
            else if (forward && clock >= clips[1].length/ClipTimeScale) CurrentPhase = Phase.Reading;
        }

        void StepReading()
        { if (!equipped || !reading) { CurrentPhase = Phase.ExitingRead; clock = 0; } }

        void StepExitingRead(double dt)
        {
            bool reverse = equipped && reading;
            clock = Math.Max(0, Math.Min(clips[3].length/ClipTimeScale, clock + (reverse ? -dt : dt)));
            if (reverse && clock <= 0) CurrentPhase = Phase.Reading;
            else if (!reverse && clock >= clips[3].length/ClipTimeScale) CurrentPhase = Phase.Aiming;
        }

        void StepLowering(double dt)
        {
            clock = Math.Max(AimTime, Math.Min(clips[0].length/ClipTimeScale, clock + (equipped ? -dt : dt)));
            if (equipped && clock <= AimTime) CurrentPhase = Phase.Aiming;
            else if (!equipped && clock >= clips[0].length/ClipTimeScale) CurrentPhase = Phase.Stowed;
        }

        void Evaluate()
        {
            int active = 0; double time = 0;
            switch (CurrentPhase)
            {
                // Stowed samples the primary clip at zero, then still evaluates
                // the graph and updates renderer visibility/events below.
                case Phase.Stowed: break;
                case Phase.Raising: case Phase.Lowering: time = clock; break;
                case Phase.Aiming: time = AimTime; break;
                case Phase.EnteringRead: active = 1; time = clock; break;
                case Phase.Reading: active = 2; time = 0; break;
                case Phase.ExitingRead: active = 3; time = clock; break;
            }
            for (int i = 0; i < playables.Length; i++) mixer.SetInputWeight(i, i == active ? 1 : 0);
            playables[active].SetTime(time*ClipTimeScale); playables[active].SetDone(false);
            CurrentClip = clips[active]; CurrentTime = time;
            graph.Evaluate(0);
            foreach (var renderer in ownedRenderers) renderer.enabled = IsVisible;
            if (wasVisible != IsVisible) { wasVisible = IsVisible; VisibilityChanged?.Invoke(IsVisible); }
        }

        /// <summary>Optional late camera registration. Caller selects the correct interior/exterior camera.</summary>
        public void AlignToCamera(Transform camera)
        { AlignToCamera(camera, Quaternion.identity); }

        /// <summary>Register an inspection gaze while keeping the real camera unchanged.</summary>
        public void AlignToCamera(Transform camera, Quaternion eyeLocalViewOffset)
        { AlignToCamera(camera, eyeLocalViewOffset, Vector3.zero); }

        public void AlignToCamera(Transform camera, Quaternion eyeLocalViewOffset, Vector3 cameraRelativeEyeOffset)
        {
            if (!initialized || !camera) throw new InvalidOperationException("Player/camera not initialized");
            if ((transform.lossyScale - Vector3.one).sqrMagnitude > .00001f)
                throw new InvalidOperationException("Use an unscaled world-space wrapper");
            Vector3 localEye = transform.InverseTransformPoint(EyeAnchor.position);
            Quaternion localRotation = Quaternion.Inverse(transform.rotation) * EyeAnchor.rotation * eyeLocalViewOffset;
            Quaternion rotation = camera.rotation * Quaternion.Inverse(localRotation);
            Vector3 eyePosition = camera.TransformPoint(cameraRelativeEyeOffset);
            transform.SetPositionAndRotation(eyePosition - rotation * localEye, rotation);
        }

        public void BindNativeSkin(Material skin)
        {
            if (!skin) throw new ArgumentNullException(nameof(skin));
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.name == "FPP_Body") renderer.sharedMaterial = skin;
        }

        void LateUpdate() { if (AutomaticUpdate && initialized) Advance(Time.deltaTime); }
        void OnDisable()
        {
            if (graph.IsValid()) graph.Destroy();
            if (ownedRenderers != null) foreach (var renderer in ownedRenderers) renderer.enabled = false;
            if (wasVisible) { wasVisible = false; VisibilityChanged?.Invoke(false); }
            initialized = false; CurrentPhase = Phase.Stowed;
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
