using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// SUNDRIFT calibration lifecycle (v1.10) behind SextantController
    /// (P5.4 slice 4c, SRP): solved against the game sun on each open,
    /// persisted in PlayerPrefs, applied to all worksheet ephemeris via
    /// SunEphemerisCalibration. The controller keeps open/close/input and
    /// passes its provider, game-sun access and worksheet. Moved verbatim
    /// (load threshold, v1.13 no-resolve-mid-series rule, log strings).
    /// </summary>
    public sealed class SunCalibrator
    {
        private const string SunCalibrationPrefsKey = "RadioNav.SunCalibrationMinutes";
        private const string LogPrefix = "[RadioNav][Sextant]";

        public void Load()
        {
            float saved = PlayerPrefs.GetFloat(SunCalibrationPrefsKey, 0f);
            if (Math.Abs(saved) > 1e-4)
            {
                SunEphemerisCalibration.SetDelta(saved);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration loaded: {1:F2} min",
                    LogPrefix, saved));
            }
        }

        /// <summary>
        /// Solves the ephemeris time shift against the game sun direction
        /// (cheap: ~70 ephemeris evaluations once per open) and persists it.
        /// Keeps the previous calibration when the game sun is unavailable
        /// or the residual is too large to trust.
        /// </summary>
        internal void Solve(IPlayerPositionProvider positionProvider, SextantGameSun gameSun,
            SextantWorksheet worksheet)
        {
            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
                return;
            Vector3 gameDir;
            float gameAlt;
            if (!gameSun.TryGetSun(out gameDir, out gameAlt))
                return;

            // v1.13: never re-solve mid-series — observation 1 was recorded
            // under the stored Δt, a new Δt would put the two sights on
            // different ephemerides and skew the LOPs. Re-solving happens
            // only when the next open starts with an empty series.
            if (worksheet.SeriesActive)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration kept: dt={1:F2} min (series active)",
                    LogPrefix, SunEphemerisCalibration.DeltaMinutes));
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double delta, residual;
            if (SunEphemerisCalibration.SolveDelta(utc, latF, lonF,
                    gameDir.x, gameDir.y, gameDir.z, out delta, out residual))
            {
                SunEphemerisCalibration.SetDelta(delta);
                PlayerPrefs.SetFloat(SunCalibrationPrefsKey, (float)delta);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration solved: dt={1:F2} min residual={2:F3} deg",
                    LogPrefix, delta, residual));
            }
            else
            {
                Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                    "{0} SUNDRIFT calibration rejected: residual={1:F3} deg - keeping previous",
                    LogPrefix, residual));
            }
            worksheet.InvalidateCard(); // card shows the calibration line
        }
    }
}
