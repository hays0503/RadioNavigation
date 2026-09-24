using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Centred aiming window (PU-style reticle box: top + both sides, no bottom
    /// line) whose size EQUALS the identification cone (author decision
    /// 2026-09-22): the navigator sees the tolerance the ID demands. The box
    /// side maps the cone through the pinhole projection onto the ocular
    /// (tan-based, so the window matches the cone at every zoom — the old
    /// linear approximation ran 12% wide at ×1); the render-target resolution
    /// cancels out. Pure, no Unity.
    /// </summary>
    public static class SextantConeBox
    {
        /// <summary>Box side in canvas units for a cone, a PIP FOV (degrees)
        /// and the live ocular side. 0 when the FOV is unknown (the caller then
        /// leaves the box hidden).</summary>
        public static float BoxUnits(double coneDeg, double pipFovDeg, float ocularSizeUnits)
        {
            if (pipFovDeg <= 0.0 || coneDeg <= 0.0)
                return 0f;
            float ocular = ocularSizeUnits > 0f && !float.IsNaN(ocularSizeUnits)
                ? ocularSizeUnits : 600f;
            double halfFovRad = pipFovDeg * Math.PI / 360.0;
            double coneRad = coneDeg * Math.PI / 180.0;
            return (float)(Math.Tan(coneRad) / Math.Tan(halfFovRad) * ocular);
        }
    }
}
