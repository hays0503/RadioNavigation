namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Live-switchable sky render-queue override, driven by the MCP bridge
    /// (reflect_set). The five sky renderers push
    /// <see cref="QueueOverride"/> into their material every frame through
    /// <see cref="SkyRenderQueuePolicy.TryApply"/>, so a bridge write takes
    /// effect within a frame or two without a game restart.
    /// <see cref="SkyRenderQueuePolicy.Off"/> leaves every material alone.
    /// </summary>
    internal static class SkyRenderQueueSwitch
    {
        internal static int QueueOverride = SkyRenderQueuePolicy.Off;

        internal static void Set(int queue)
        {
            QueueOverride = queue;
        }

        internal static void Reset()
        {
            QueueOverride = SkyRenderQueuePolicy.Off;
        }
    }
}
