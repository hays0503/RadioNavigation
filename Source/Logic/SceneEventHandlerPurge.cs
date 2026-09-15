using System;
using System.Reflection;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Hot-reload helper (F219). UBOAT recompiles a mod's Source on the fly and
    /// calls <c>InitializeAssembly</c>, which creates a BRAND NEW IUserMod
    /// instance in a newly loaded assembly — but the old assembly is never
    /// unloaded from the AppDomain. Its static-event subscriptions
    /// (<c>SceneEventsListener.OnSceneStart/OnSceneAwake</c>) survive and root
    /// the old ModBootstrapper, so every reload leaks one more handler and
    /// builds duplicate panels.
    ///
    /// This removes stale handlers whose declaring type + method name identify
    /// the mod's own bootstrap, without touching other subscribers. Pure and
    /// reflection-free so it is unit-testable offline; the caller supplies the
    /// delegate list obtained from the event's backing field.
    /// </summary>
    public static class SceneEventHandlerPurge
    {
        /// <summary>
        /// Returns <paramref name="handlers"/> with every entry whose
        /// <see cref="MethodInfo"/> declaring type full name equals
        /// <paramref name="declaringTypeFullName"/> and whose method name equals
        /// <paramref name="methodName"/> removed. Returns null when nothing
        /// remains.
        /// </summary>
        public static Delegate RemoveAll(Delegate handlers, string declaringTypeFullName, string methodName)
        {
            if (handlers == null)
                return null;

            Delegate kept = null;
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                Delegate handler = list[i];
                MethodInfo method = handler.Method;
                bool stale = method != null
                    && method.DeclaringType?.FullName == declaringTypeFullName
                    && method.Name == methodName;
                if (!stale)
                    kept = Delegate.Combine(kept, handler);
            }
            return kept;
        }
    }
}
