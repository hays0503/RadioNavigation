using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Centred aiming reticle whose GAP equals the identification cone
    /// (author decisions 2026-09-22 / 2026-11-04): the four crosshair arms
    /// start at the cone's angular radius, so everything inside the gap is
    /// identifiable and the arms' inner ends mark the tolerance. The old
    /// square window lied at its corners (a body at 1.0° H + 1.0° V was
    /// inside the box but outside the cone). The projection is exact
    /// perspective (tan(cone)/tan(pipFov/2) — the linear form overstated the
    /// window by 12.3% at ×1); the render-target resolution cancels out. The
    /// arms are short (1.5 × the gap radius, floor MinArmUnits) so the reticle
    /// stays compact at ×6 (author: "из перекрестия вычесть круг"). Pure,
    /// no Unity.
    /// </summary>
    public static class SextantConeBox
    {
        /// <summary>Arm length as a multiple of the gap radius (author
        /// 2026-11-04).</summary>
        public const float ArmRadiusMultiplier = 1.5f;

        /// <summary>Floor for the arm length in canvas units: at ×1 (or on a
        /// small ocular) 1.5 × the gap radius would be a few pixels — the
        /// reticle must stay visible.</summary>
        public const float MinArmUnits = 12f;

        /// <summary>Diameter of the tolerance circle in canvas units for a
        /// cone, a PIP FOV (degrees) and the live ocular side — the gap
        /// between opposite arms. 0 when the FOV is unknown (the caller then
        /// leaves the reticle hidden).</summary>
        public static float DiameterUnits(double coneDeg, double pipFovDeg, float ocularSizeUnits)
        {
            if (pipFovDeg <= 0.0 || coneDeg <= 0.0)
                return 0f;
            float ocular = ocularSizeUnits > 0f && !float.IsNaN(ocularSizeUnits)
                ? ocularSizeUnits : 600f;
            double halfFovRad = pipFovDeg * Math.PI / 360.0;
            double coneRad = coneDeg * Math.PI / 180.0;
            return (float)(Math.Tan(coneRad) / Math.Tan(halfFovRad) * ocular);
        }

        /// <summary>Radius of the tolerance circle (half the diameter) — the
        /// distance from the ocular centre to each arm's inner end.</summary>
        public static float RadiusUnits(double coneDeg, double pipFovDeg, float ocularSizeUnits)
        {
            return DiameterUnits(coneDeg, pipFovDeg, ocularSizeUnits) * 0.5f;
        }

        /// <summary>Length of one crosshair arm for a gap radius: 1.5 × the
        /// radius, floored at MinArmUnits so the reticle never vanishes.</summary>
        public static float ArmLengthUnits(float gapRadiusUnits)
        {
            if (gapRadiusUnits <= 0f || float.IsNaN(gapRadiusUnits))
                return MinArmUnits;
            float arm = gapRadiusUnits * ArmRadiusMultiplier;
            return arm < MinArmUnits ? MinArmUnits : arm;
        }
    }
}
