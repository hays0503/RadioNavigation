namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Ocular render-target size policy (CELESTIAL_REALISM_PLAN P7): the PIP
    /// renders the ocular, so its resolution should track the ocular's
    /// on-screen size instead of a hard 512² (a 512² ocular is ~2× coarser
    /// than the eyes at 1080p). The ocular is a fraction of the screen height
    /// (SextantOcularSize, 2026-11-04), so the RT follows that fraction,
    /// clamped to a band that keeps the memory and the bloom-cut tuning sane.
    /// Pure, no Unity.
    /// </summary>
    public static class SextantPipSize
    {
        public const int MinSize = 256;
        public const int MaxSize = 1536;

        /// <summary>Used when the screen height is unknown (headless/tests).</summary>
        public const int FallbackSize = 512;

        /// <summary>Legacy: half the screen height, clamped.</summary>
        public static int ForScreenHeight(int screenHeight)
        {
            if (screenHeight <= 0)
                return FallbackSize;
            int size = screenHeight / 2;
            if (size < MinSize)
                return MinSize;
            if (size > MaxSize)
                return MaxSize;
            return size;
        }

        /// <summary>Render-target side for the ocular's on-screen pixel size
        /// (ocular units × uiScale). The ocular is a fraction of the screen
        /// height, so this is ~0.88 × screenHeight at the default fraction.
        /// Clamped to the band; unknown inputs fall back.</summary>
        public static int ForOcularPixels(float ocularPixels)
        {
            if (ocularPixels <= 0f || float.IsNaN(ocularPixels))
                return FallbackSize;
            int size = (int)ocularPixels;
            if (size < MinSize)
                return MinSize;
            if (size > MaxSize)
                return MaxSize;
            return size;
        }
    }
}
