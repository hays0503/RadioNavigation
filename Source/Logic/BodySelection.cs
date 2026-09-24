namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sticky body selection for the sextant (author decision 2026-11-04): a
    /// body confirmed by the 2.5 s hold stays selected after it leaves the
    /// aiming cone, so the player can look away, fine-tune the drum and still
    /// record the sight. Cleared only by the series reset (O), the sextant
    /// reset (open/pain) or a new confirmation of another body. The
    /// "in cone" flag stays separate: recording uses the selection, while the
    /// burst quality meter and the index-error calibration still require the
    /// body to be actually sighted. Pure, no Unity — unit-tested offline.
    /// </summary>
    public sealed class BodySelection
    {
        /// <summary>Suffix shown while the selected body is outside the cone.</summary>
        public const string OutOfConeSuffix =
            " \u00B7 \u0432\u043D\u0435 \u043F\u0440\u0438\u0446\u0435\u043B\u0430";

        /// <summary>Seconds a selection survives outside the cone before it is
        /// dropped (author decision 2026-11-04: the drum is set within ~5 s;
        /// after that the stale name and aim readout must clear).</summary>
        public const float LostTimeoutSeconds = 5f;

        private CelestialBody selected;

        /// <summary>The selected body (Kind=None while none).</summary>
        public CelestialBody Selected
        {
            get { return selected; }
        }

        /// <summary>True once a body has been confirmed by a hold.</summary>
        public bool HasSelection
        {
            get { return !selected.IsNone; }
        }

        /// <summary>Confirmation of a body: it becomes (or replaces) the
        /// selection. None is ignored — a failed hold never clears the
        /// previous selection.</summary>
        public void Select(CelestialBody body)
        {
            if (body.IsNone)
                return;
            selected = body;
        }

        /// <summary>Drops the selection (O key / sextant reset / pain / the
        /// out-of-cone timeout).</summary>
        public void Clear()
        {
            selected = CelestialBody.None();
        }

        /// <summary>Lost-time accumulator for the selection timeout: counts
        /// while the body is out of the cone, resets the moment it is back in.
        /// Invalid inputs keep the current value.</summary>
        public static float AdvanceLost(float lostSeconds, bool inCone, float dt)
        {
            if (float.IsNaN(lostSeconds) || lostSeconds < 0f)
                lostSeconds = 0f;
            if (inCone)
                return 0f;
            if (float.IsNaN(dt) || dt <= 0f)
                return lostSeconds;
            return lostSeconds + dt;
        }

        /// <summary>True when the selection has been out of the cone long
        /// enough to be dropped.</summary>
        public static bool TimedOut(float lostSeconds)
        {
            return !float.IsNaN(lostSeconds) && lostSeconds >= LostTimeoutSeconds;
        }

        /// <summary>Name text for the identification line: the plain name
        /// while the body is in the cone, with the "вне прицела" suffix when
        /// the selection survives outside it. Empty in, empty out.</summary>
        public static string DisplayName(string name, bool inCone)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            return inCone ? name : name + OutOfConeSuffix;
        }
    }
}
