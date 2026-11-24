using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Camera-relative walking sway/bob offsets for the handheld C. Plath sextant.</summary>
    public struct CPlathWalkBobPose
    {
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;
        public float PitchDeg;
        public float YawDeg;
        public float RollDeg;
    }

    /// <summary>
    /// Procedural figure-8 gait sway for the handheld sextant in FPP. Pure C# math
    /// so amplitude, frequency, sprint scaling and reading attenuation are tested offline.
    /// </summary>
    public sealed class CPlathWalkBob
    {
        private const float MaxStepDt = 0.1f;
        private const float WalkStepHz = 1.85f;
        private const float RunStepHz = 2.75f;
        private const float MoveAttackRate = 7.5f;
        private const float MoveReleaseRate = 6.0f;
        private const float RunBlendRate = 6.0f;
        private const float RunAmplitudeBoost = 0.45f;
        private const float TwoPi = (float)(2.0 * Math.PI);

        private float phase;
        private float moveWeight;
        private float runWeight;

        /// <summary>Clears phase and smoothing weights on stow or suspend.</summary>
        public void Reset()
        {
            phase = 0f;
            moveWeight = 0f;
            runWeight = 0f;
        }

        /// <summary>Advances the gait bob by <paramref name="dt"/> seconds.</summary>
        public CPlathWalkBobPose Step(float dt, bool walking, bool running, float attenuation)
        {
            float stepDt = SanitizeDt(dt);
            float scale = Clamp01(attenuation);
            float targetMove = walking ? 1f : 0f;
            float targetRun = walking && running ? 1f : 0f;

            moveWeight = SmoothToward(moveWeight, targetMove,
                targetMove > moveWeight ? MoveAttackRate : MoveReleaseRate, stepDt);
            runWeight = SmoothToward(runWeight, targetRun, RunBlendRate, stepDt);

            if (moveWeight <= 1e-4f)
            {
                moveWeight = 0f;
                phase = 0f;
                return new CPlathWalkBobPose();
            }

            float stepHz = WalkStepHz + (RunStepHz - WalkStepHz) * runWeight;
            phase += (float)Math.PI * stepHz * stepDt;
            if (phase >= TwoPi)
                phase %= TwoPi;

            float amplitude = moveWeight * scale * (1f + RunAmplitudeBoost * runWeight);
            double s1 = Math.Sin(phase);
            double c1 = Math.Cos(phase);
            double s2 = Math.Sin(2.0 * phase);
            double c2 = Math.Cos(2.0 * phase);

            return new CPlathWalkBobPose
            {
                OffsetX = (float)(0.0080 * amplitude * s1),
                OffsetY = (float)(-0.0055 * amplitude * c2),
                OffsetZ = (float)(0.0025 * amplitude * s2),
                PitchDeg = (float)(0.85 * amplitude * s2),
                YawDeg = (float)(-0.55 * amplitude * c1),
                RollDeg = (float)(-1.35 * amplitude * s1)
            };
        }

        private static float SanitizeDt(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f)
                return 0f;
            return dt > MaxStepDt ? MaxStepDt : dt;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
                return 0f;
            return value >= 1f ? 1f : value;
        }

        private static float SmoothToward(float current, float target, float rate, float dt)
        {
            if (dt <= 0f)
                return current;
            float factor = 1f - (float)Math.Exp(-rate * dt);
            return current + (target - current) * factor;
        }
    }
}
