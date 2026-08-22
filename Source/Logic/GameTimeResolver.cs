using System;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Resolves campaign time without leaking GameTime into pure astronomy code.</summary>
    internal static class GameTimeResolver
    {
        private static GameTime gameTime;

        public static DateTime GetUtcNow()
        {
            if (gameTime == null)
                gameTime = InjectionFramework.Instance.GetInstance<GameTime>();

            if (gameTime != null)
                return DateTime.SpecifyKind(gameTime.GmtDateTime, DateTimeKind.Utc);

            return DateTime.UtcNow;
        }

        public static void Reset()
        {
            gameTime = null;
        }
    }
}
