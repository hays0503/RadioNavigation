namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Frame-interval retry throttle for expensive fallbacks (scene-wide
    /// Finds) that would otherwise run every frame while unresolved.
    /// Pure frame math, unit-tested offline in FrameThrottleTests.
    /// No Unity dependency.
    /// </summary>
    public static class FrameThrottle
    {
        public static bool ShouldRetry(int lastFrame, int currentFrame, int intervalFrames)
        {
            return currentFrame - lastFrame >= intervalFrames;
        }
    }
}
