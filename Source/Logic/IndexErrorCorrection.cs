using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sextant index error (CELESTIAL_REALISM_PLAN P6): a real instrument reads
    /// a constant offset (collimation of the horizon and index glasses), found
    /// by sighting a known body and read off the drum. The value is applied to
    /// the raw arc BEFORE quantisation, so the committed Ho stays on the
    /// 1/600° grid, and is persisted between sessions. Pure math + PlayerPrefs.
    /// </summary>
    public static class IndexErrorCorrection
    {
        /// <summary>PlayerPrefs key (arcminutes).</summary>
        public const string PrefsKey = "RadioNav.SextantIndexMin";

        /// <summary>Largest accepted index error (arcminutes).</summary>
        public const double MaxArcMin = 5.0;

        /// <summary>Clamps an index error into ±MaxArcMin.</summary>
        public static double Clamp(double indexErrorArcMin)
        {
            if (double.IsNaN(indexErrorArcMin))
                return 0.0;
            if (indexErrorArcMin > MaxArcMin)
                return MaxArcMin;
            if (indexErrorArcMin < -MaxArcMin)
                return -MaxArcMin;
            return indexErrorArcMin;
        }

        /// <summary>Corrected arc: raw + index error (arcmin → degrees).</summary>
        public static double Apply(double arcDeg, double indexErrorArcMin)
        {
            return arcDeg + Clamp(indexErrorArcMin) / 60.0;
        }

        /// <summary>Index error recovered from a sight of a known body: the
        /// drum reads `arcDeg` while the true altitude is `trueAltDeg`.</summary>
        public static double SolveFromKnownAltitude(double arcDeg, double trueAltDeg)
        {
            return Clamp((trueAltDeg - arcDeg) * 60.0);
        }

        public static double Load()
        {
            return Clamp(PlayerPrefs.GetFloat(PrefsKey, 0f));
        }

        public static void Save(double indexErrorArcMin)
        {
            PlayerPrefs.SetFloat(PrefsKey, (float)Clamp(indexErrorArcMin));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
