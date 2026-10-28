using System;

namespace UnityEngine.UI
{
    // No UI rendering is claimed by these offline tests.
    public class Text
    {
        public int fontSize;
        public string text;
        public float preferredHeight;
    }
}

namespace UBOAT.Mods.RadioNavigation
{
    public interface IPlayerPositionProvider
    {
        bool TryGetPosition(out float lat, out float lon);
    }

    internal static class GameTimeResolver
    {
        internal static DateTime Now = new DateTime(1939, 9, 2, 10, 3, 7, DateTimeKind.Utc);
        public static DateTime GetUtcNow() { return Now; }
    }
}
