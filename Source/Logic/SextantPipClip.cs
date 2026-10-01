namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The PIP cameras' clip planes, as a pure contract.
    ///
    /// Why this is not a constant inside SextantPipPose: the numbers are a
    /// contract with the world, and the project tests contracts. SextantPipPose
    /// itself is Unity-bound (Camera, Transform), so a value living there cannot
    /// be asserted offline at all - and this one had already caused a visible
    /// defect nobody could catch, because nothing about it was testable.
    /// </summary>
    public static class SextantPipClip
    {
        /// <summary>
        /// Far clip for the PIP cameras, in metres. NOT mirrored from the FPP eye
        /// camera (author's report, 2026-10-02).
        ///
        /// The eye camera's far clip is tuned for the bridge window - 40 m
        /// inside, fog-dependent outside (FPPCameraController.cs:942) - and the
        /// game rewrites it every frame, so copying it into the PIP sliced the
        /// ocean. Measured on a 2560x1440 frame: a razor-sharp horizontal
        /// boundary crossed the ocular about 7 degrees below the axis (r = +0.37)
        /// with sky above it and sea below, it moved as the boat pitched, and it
        /// was present ONLY inside the ocular - the main camera's far is 4500 and
        /// never showed it. It survived with the eye shadow switched off
        /// entirely, which is what cleared the lune, the lens, the constellation
        /// lines and the bezel in one measurement.
        ///
        /// The value must clear both the sea horizon and the celestial shell: the
        /// star layer reports radius 3825 (CameraResolver.WorldRadiusM is 3900)
        /// and the main camera sits at far 4500.
        /// </summary>
        public const double FarPlane = 9000.0;

        /// <summary>
        /// Radius of the celestial shell, from CameraResolver.WorldRadiusM (3900)
        /// after the safety factor StarsMesh applies. Recorded here so the test
        /// that bounds <see cref="FarPlane"/> cites a number rather than a story.
        /// </summary>
        public const double CelestialShellRadius = 3900.0;

        /// <summary>Far plane of the main camera, measured live (2026-10-02).</summary>
        public const double MainCameraFarPlane = 4500.0;

        /// <summary>
        /// Near clip floor. The eye camera's own near plane is mirrored so the
        /// interior keeps its depth precision; the floor only stops a zero or a
        /// negative value, which would collapse the projection.
        /// </summary>
        public const double MinNearPlane = 0.02;

        /// <summary>
        /// True when <paramref name="farPlane"/> reaches past both the celestial
        /// shell and the main camera's own reach, by at least
        /// <paramref name="margin"/>. The margin is deliberate: fog distance and
        /// the shell radius both move with weather and scale, so matching them is
        /// not good enough - the number has to beat them.
        /// </summary>
        public static bool FarPlaneClearsTheWorld(double farPlane, double margin)
        {
            if (double.IsNaN(farPlane) || double.IsInfinity(farPlane))
                return false;
            if (double.IsNaN(margin) || margin < 1.0)
                return false;
            return farPlane > MainCameraFarPlane * margin
                && farPlane > CelestialShellRadius * margin;
        }

        /// <summary>The shipped far plane, checked against the world.</summary>
        public static bool FarPlaneClearsTheWorld(double margin)
        {
            return FarPlaneClearsTheWorld(FarPlane, margin);
        }
    }
}
