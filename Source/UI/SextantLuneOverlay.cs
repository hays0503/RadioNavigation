using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Drives the eye-pupil shadow ("lune dans le scope") in the ocular's
    /// aperture shader.
    ///
    /// The shadow follows the angular RATE of the view, not the mouse
    /// (author 2026-10-02): with the cursor locked the camera cannot turn, the
    /// ocular cannot turn with it, and there can be no shadow. A rate is exactly
    /// the right signal because it falls to zero on its own when the view stops —
    /// no thresholds, no filters — and it is zero while the game blocks the
    /// camera, which is the case the author reported.
    ///
    /// It does NOT follow the alidade: slewing the drum moves the instrument,
    /// not the view, and the author ruled that out ("убрать совсем"). It is not
    /// an input at all, which is why the rate is measured before the washout
    /// stabilization in SextantPipPose.
    ///
    /// Settings live in PlayerPrefs and are re-read every second, so the feel
    /// can be tuned live without rebuilding the bundle — see
    /// <see cref="SextantLuneMath"/> for the keys and their defaults. The hull's
    /// own swing comes along with the camera, which is why a working sea makes
    /// the shadow breathe; the author asked for exactly that.
    /// </summary>
    internal sealed class SextantLuneOverlay
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const float SettingsPollSeconds = 1f;

        private static readonly int OffsetId = Shader.PropertyToID("_LuneOffset");
        private static readonly int RadiusId = Shader.PropertyToID("_LuneRadius");
        private static readonly int StrengthId = Shader.PropertyToID("_LuneStrength");
        private static readonly int RimLightId = Shader.PropertyToID("_LuneRimLight");

        private Material material;
        private SextantLuneState state = SextantLuneState.Rest;
        private SextantAimRateSample rate;
        private float strength = (float)SextantLuneMath.DefaultStrength;
        private double maxUnits = SextantLuneMath.MaxOffsetUnits;
        private double rateFull = SextantLuneMath.RateFullDegPerSec;
        private double deadZone = SextantLuneMath.DeadZoneDegPerSec;
        private double rimLight = SextantLuneMath.RimLight;
        private int probePercent;
        private float settingsTimer;
        private float logTimer;
        private double lastYawRate;
        private double lastPitchRate;

        /// <summary>Current eye offset (aperture radii) — MCP/reflection probe.</summary>
        internal SextantLuneOffset Offset
        {
            get { return state.Offset; }
        }

        /// <summary>Seconds the input has been gone (the stop lag timer).</summary>
        internal double ReleaseHoldSeconds
        {
            get { return state.ReleaseHoldSeconds; }
        }

        /// <summary>Binds the shared aperture material (built with the overlay).</summary>
        public void Bind(Material apertureMaterial)
        {
            material = apertureMaterial;
            ReadSettings();
            Write();
        }

        /// <summary>Clears the shadow (panel closed / rebuilt).</summary>
        public void Reset()
        {
            state = SextantLuneState.Rest;
            rate = new SextantAimRateSample(0d, 0d);
            lastYawRate = 0d;
            lastPitchRate = 0d;
            ReadSettings();
            Write();
        }

        /// <summary>
        /// One frame: take the view's angular rate, move the shadow with eye
        /// inertia and push it into the material. No-ops while the panel is
        /// closed so a closed sextant can never show a stale crescent.
        /// </summary>
        public void Tick(float dt, bool open, double yawRateDegPerSec, double pitchRateDegPerSec)
        {
            lastYawRate = yawRateDegPerSec;
            lastPitchRate = pitchRateDegPerSec;
            PollSettings();

            if (!open || strength <= 0f)
            {
                if (state.Offset.Magnitude > 0d || state.ReleaseHoldSeconds > 0d)
                {
                    state = SextantLuneState.Rest;
                    rate = new SextantAimRateSample(0d, 0d);
                    Write();
                }
                return;
            }

            SextantLuneOffset target = probePercent > 0
                ? ProbeTarget()
                : SextantLuneMath.FromCameraMotion(yawRateDegPerSec, pitchRateDegPerSec,
                    maxUnits, deadZone, rateFull);

            SextantLuneState next = SextantLuneMath.Step(state, target, dt, maxUnits);
            bool moved = Mathf.Abs((float)(next.Offset.X - state.Offset.X)) >= 1e-4f
                || Mathf.Abs((float)(next.Offset.Y - state.Offset.Y)) >= 1e-4f;
            // The hold timer alone still counts as a change: it is what the lag
            // is made of, and the shadow keeps its size during it.
            bool holding = next.ReleaseHoldSeconds != state.ReleaseHoldSeconds;
            state = next;
            if (!moved && !holding)
                return;
            Write();
            LogRateLimited();
        }

        /// <summary>
        /// Verification path: force the shadow to a chosen fraction of the
        /// travel. The bridge cannot deliver a mouse, so the keyboard alone only
        /// reaches a fraction of the range, and the crescent's shape and edges
        /// cannot be measured on a paused frame without this. Off when the
        /// preference is 0.
        /// </summary>
        private SextantLuneOffset ProbeTarget()
        {
            double travel = maxUnits * probePercent / 100d;
            return new SextantLuneOffset(travel, 0d);
        }

        private void PollSettings()
        {
            settingsTimer -= Time.unscaledDeltaTime;
            if (settingsTimer > 0f)
                return;
            settingsTimer = SettingsPollSeconds;
            float strengthBefore = strength;
            double maxBefore = maxUnits;
            double rimBefore = rimLight;
            ReadSettings();
            if (Mathf.Abs(strengthBefore - strength) > 1e-4f || Math.Abs(maxBefore - maxUnits) > 1e-6
                || Math.Abs(rimBefore - rimLight) > 1e-6)
                Write();
        }

        private void ReadSettings()
        {
            strength = Mathf.Clamp01(PlayerPrefs.GetInt(
                SextantLuneMath.StrengthPrefsKey,
                (int)(SextantLuneMath.DefaultStrength * 100f)) / 100f);
            maxUnits = Sanitize(PlayerPrefs.GetInt(SextantLuneMath.MaxUnitsPrefsKey,
                (int)(SextantLuneMath.MaxOffsetUnits * 100f)), SextantLuneMath.MaxOffsetUnits) / 100d;
            rateFull = Sanitize(PlayerPrefs.GetInt(SextantLuneMath.RateFullPrefsKey,
                (int)SextantLuneMath.RateFullDegPerSec), SextantLuneMath.RateFullDegPerSec);
            deadZone = Sanitize(PlayerPrefs.GetInt(SextantLuneMath.DeadZonePrefsKey,
                (int)(SextantLuneMath.DeadZoneDegPerSec * 100d)),
                SextantLuneMath.DeadZoneDegPerSec * 100d) / 100d;
            // The conversion is the tested pure one, not arithmetic here: two
            // mutations of the inline version survived every gate (the ceiling
            // multiplied by zero, and a clamp at 1 instead of 0 which would have
            // made the author's A/B impossible). A substring gate cannot check
            // arithmetic, so the arithmetic is unit-tested instead.
            // One line on purpose: the SourceCheck gate requires the literal
            // "PlayerPrefs.GetInt(SextantLuneMath.RimLightPrefsKey", and a line
            // break between the call and its key made that condition impossible
            // to satisfy - the gate was red on a clean tree for a whole run.
            rimLight = SextantLuneMath.RimLightFrom(PlayerPrefs.GetInt(SextantLuneMath.RimLightPrefsKey, (int)(SextantLuneMath.RimLight * 1000d)));
            probePercent = PlayerPrefs.GetInt(SextantLuneMath.ProbePrefsKey, 0);
            if (probePercent < 0)
                probePercent = 0;
            if (probePercent > 100)
                probePercent = 100;
            if (material == null)
                return;
            SetFloatIfPresent(RadiusId, (float)SextantLuneMath.ShadowRadiusUnits);
            SetFloatIfPresent(StrengthId, strength);
            SetFloatIfPresent(RimLightId, (float)rimLight);
        }

        private static double Sanitize(int value, double fallback)
        {
            return value <= 0 ? fallback : value;
        }

        private void Write()
        {
            if (material == null)
                return;
            SextantLuneOffset centre = SextantLuneMath.ObstructionCentre(state.Offset);
            material?.SetVector(OffsetId, new Vector4((float)centre.X, (float)centre.Y, 0f, 0f));
            // The lit wall's brightness has to follow the displacement, not just
            // the settings poll: the wall is drawn from the shadow's distance
            // field, so without this it stays at full strength while it is being
            // carried inwards - a bright edge sweeping across the picture on
            // every fast turn (SextantLuneMath.WallLight).
            SetFloatIfPresent(RimLightId, (float)SextantLuneMath.WallLight(
                rimLight, state.Offset.Magnitude, maxUnits));
        }

        private void SetFloatIfPresent(int id, float value)
        {
            if (material?.HasProperty(id) == true)
                material.SetFloat(id, value);
        }

        private void LogRateLimited()
        {
            logTimer -= Time.unscaledDeltaTime;
            if (logTimer > 0f)
                return;
            logTimer = 1f;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} LUNE offset={1} hold={2:F2}s rate={3:F1},{4:F1}deg/s max={5:F3} full={6:F0} dead={7:F2} rim={8:F3} probe={9} strength={10:F2}",
                LogPrefix, state.Offset, state.ReleaseHoldSeconds, lastYawRate, lastPitchRate,
                maxUnits, rateFull, deadZone, rimLight, probePercent, strength));
        }
    }
}
