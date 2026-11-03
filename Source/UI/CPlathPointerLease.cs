using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal interface ICPlathPointerBackend
    {
        bool Available { get; }
        bool BlockedByOthers(object owner);
        void Acquire(object owner);
        void Release(object owner);
        void Hover(bool interactive);
        void SuppressWorldPointer(bool suppress);
    }

    /// <summary>Owns our cursor/input tokens and the world-pointer suppression.</summary>
    internal sealed class CPlathPointerLease
    {
        readonly ICPlathPointerBackend backend;
        readonly object token = new object();
        bool hovered;
        internal bool Active { get; private set; }

        internal CPlathPointerLease(ICPlathPointerBackend backend)
        { this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); }

        internal bool Update(bool requested, bool focused)
        {
            if (!requested || !focused || !backend.Available || backend.BlockedByOthers(token))
            { Suspend(); return false; }
            if (!Active)
            {
                try { backend.Acquire(token); backend.SuppressWorldPointer(true); Active = true; }
                catch { backend.SuppressWorldPointer(false); backend.Release(token); throw; }
            }
            return true;
        }

        internal void SetHover(bool interactive)
        {
            interactive &= Active;
            if (hovered == interactive) return;
            backend.Hover(interactive); hovered = interactive;
        }

        internal void Suspend()
        {
            try { SetHover(false); }
            finally
            {
                if (Active)
                {
                    Active = false;
                    backend.SuppressWorldPointer(false);
                    backend.Release(token);
                }
            }
        }
    }

    internal sealed class CPlathPointerButton
    {
        bool wasActive, wasDown;
        internal bool Advance(bool active, bool down, bool selectable)
        {
            bool pressed = active && wasActive && down && !wasDown && selectable;
            wasActive = active; wasDown = down;
            return pressed;
        }
        internal void Suspend() { wasActive = false; wasDown = true; }
    }
}
