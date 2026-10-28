using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal interface ICelestialSkyInputSource
    {
        bool TryRead(long revision, out CelestialSkyInput input, out CelestialSkyNotReady reason);
    }

    /// <summary>Pure scene/load state machine. No render, UI, persistence or game services.</summary>
    internal sealed class CelestialWorldOwner
    {
        internal long WorldRevision { get; private set; }
        internal CelestialSkyState Current { get; private set; }
        internal CelestialSkyNotReady NotReady { get; private set; }
        private bool hasWorld;
        private bool loading;
        private long publication;

        internal CelestialWorldOwner() { NotReady = CelestialSkyNotReady.NoWorld; }

        internal long BeginLoad()
        {
            WorldRevision = checked(WorldRevision + 1);
            hasWorld = true; loading = true;
            Invalidate(CelestialSkyNotReady.Loading);
            return WorldRevision;
        }

        internal bool CompleteLoad(long revision)
        {
            if (!hasWorld || revision != WorldRevision) return false;
            if (!loading) return true;
            loading = false;
            Invalidate(CelestialSkyNotReady.MissingInput);
            return true;
        }

        internal void Unload()
        {
            WorldRevision = checked(WorldRevision + 1);
            hasWorld = false; loading = false;
            Invalidate(CelestialSkyNotReady.NoWorld);
        }

        internal void Invalidate(CelestialSkyNotReady reason)
        {
            Current = null;
            NotReady = reason == CelestialSkyNotReady.None ? CelestialSkyNotReady.SourceUnavailable : reason;
        }

        internal bool Tick(ICelestialSkyInputSource source)
        {
            if (!hasWorld) { Invalidate(CelestialSkyNotReady.NoWorld); return false; }
            if (loading) { Invalidate(CelestialSkyNotReady.Loading); return false; }
            try
            {
                CelestialSkyInput input;
                CelestialSkyNotReady reason;
                if (source == null) { Invalidate(CelestialSkyNotReady.SourceUnavailable); return false; }
                if (!source.TryRead(WorldRevision, out input, out reason)) { Invalidate(reason); return false; }
                return Publish(input);
            }
            catch (Exception)
            {
                Invalidate(CelestialSkyNotReady.Computation);
                return false;
            }
        }

        private bool Publish(CelestialSkyInput input)
        {
            CelestialSkyNotReady reason = CelestialSkyMath.Validate(input);
            if (reason != CelestialSkyNotReady.None) { Invalidate(reason); return false; }
            if (input.WorldRevision != WorldRevision) { Invalidate(CelestialSkyNotReady.WrongWorld); return false; }
            if (Current?.Input.SameKey(input) == true) return true;
            long version = checked(publication + 1);
            CelestialSkyState next = CelestialSkyMath.Compute(input, version);
            publication = version; Current = next; NotReady = CelestialSkyNotReady.None;
            return true;
        }

        internal bool CanUse(CelestialSkyState state, CelestialSkyInput expected)
        {
            return Current != null && ReferenceEquals(Current, state) && Current.Input.SameKey(expected);
        }
    }
}
