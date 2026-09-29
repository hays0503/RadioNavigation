using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sky render-queue policy. The correct occlusion route is the Deep
    /// pipeline's Sky stage (<c>ShaderTagId("Sky")</c>, drawn right after
    /// <c>DrawSkybox</c> and before the forward/G-buffer stages with
    /// <c>RenderQueueRange.opaque</c>): the celestial materials carry a Sky
    /// pass and default to <see cref="SkyStageQueue"/> (1020), below the
    /// UniStorm cloud composite at 1051, so the clouds blend OVER the mod's
    /// sky content. An old hypothesis — lowering the queue inside the
    /// transparent band — was disproved by RenderDoc (a queue of 1990 simply
    /// dropped the star field from the frame); that is why 1020 must stay
    /// below 2500.
    ///
    /// <see cref="SkyRenderQueueSwitch"/> is a live override for experiments,
    /// so the default can be tuned without a game restart. The decision math
    /// (clamp/off) is kept in the pure <see cref="Clamp"/> method so it is
    /// unit-testable; <see cref="TryApply"/> and <see cref="ApplySkyStage"/>
    /// are the Unity writes.
    /// </summary>
    internal static class SkyRenderQueuePolicy
    {
        /// <summary>Override off: leave the material's own render queue untouched.</summary>
        internal const int Off = 0;

        /// <summary>Geometry queue floor — below this lies background/skybox territory.</summary>
        internal const int MinQueue = 1000;

        /// <summary>Upper bound stays inside the Transparent queue band (3000+).</summary>
        internal const int MaxQueue = 3500;

        /// <summary>
        /// Queue every celestial material defaults to when its shader carries
        /// the Sky-stage pass. <c>DeepPipeline.RenderSky</c> draws the
        /// <c>ShaderTagId("Sky")</c> stage with <c>RenderQueueRange.opaque</c>
        /// (0…2500), immediately after <c>DrawSkybox</c> and before the
        /// forward/G-buffer stages. The mod's PointSky pass at queue 1020 sorts
        /// before UniStorm's cloud-composite Sky pass at queue 1051 (Background+51).
        /// Any value ≥ 2500 is
        /// filtered out of the Sky stage (and the deferred path then draws
        /// ForwardSRP only in the transparent range, i.e. after the clouds —
        /// the original bug).
        /// </summary>
        internal const int SkyStageQueue = 1020;

        /// <summary>
        /// Pure clamp. <see cref="Off"/> (0) and every negative value stay 0
        /// (the override is disabled); everything else lands in
        /// [MinQueue, MaxQueue].
        /// </summary>
        public static int Clamp(int queue)
        {
            if (queue <= Off)
                return Off;
            if (queue < MinQueue)
                return MinQueue;
            if (queue > MaxQueue)
                return MaxQueue;
            return queue;
        }

        /// <summary>
        /// Applies the override to a material. Returns false and does nothing
        /// when the material is missing or the override is off; otherwise
        /// writes renderQueue only when the clamped value differs.
        /// </summary>
        public static bool TryApply(Material material, int queue)
        {
            if (material == null || queue <= Off)
                return false;

            int clamped = Clamp(queue);
            if (material.renderQueue != clamped)
                material.renderQueue = clamped;
            return true;
        }

        /// <summary>
        /// Pushes the Sky-stage default queue onto a freshly created celestial
        /// material. <paramref name="hasSkyPass"/> is supplied by the caller
        /// (the material's shader must carry the <c>Sky</c> pass) so this
        /// policy stays free of Unity shader lookups and testable with the
        /// Material stub. The live override (<see cref="SkyRenderQueueSwitch"/>)
        /// still wins per frame through <see cref="TryApply"/>.
        /// </summary>
        public static bool ApplySkyStage(Material material, bool hasSkyPass)
        {
            if (material == null || !hasSkyPass)
                return false;
            if (material.renderQueue != SkyStageQueue)
                material.renderQueue = SkyStageQueue;
            return true;
        }
    }
}
