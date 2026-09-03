using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Receiver selectivity curve and BFO beat clamp. Extracted from
    /// SonneController.UpdateStationGains (SRP — the gain formula is not
    /// array-management logic) so ShieldTests pins production code instead
    /// of a local copy. Pure BCL, unit-tested in StationGainModelTests.
    /// </summary>
    public static class StationGainModel
    {
        public static float ComputeGain(float tunedKHz, float stationKHz, float qualityFactor)
        {
            if (float.IsNaN(tunedKHz) || float.IsNaN(stationKHz) || stationKHz == 0f)
                return 0f;
            float f = tunedKHz * 1000f;
            float f0 = stationKHz * 1000f;
            float ratio = f / f0 - f0 / f;
            return 1f / (float)Math.Sqrt(1.0 + qualityFactor * qualityFactor * ratio * ratio);
        }

        public static float ComputeBeatFrequency(float offsetKHz)
        {
            return Math.Max(Constants.BfoMinHz,
                Math.Min(Constants.BfoMaxHz, Constants.BfoBaseHz + offsetKHz * 1000f));
        }
    }
}
