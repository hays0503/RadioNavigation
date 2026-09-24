using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Course/speed over ground from the recent position track. The cadence is
    /// GAME time, not wall time (CELESTIAL_REALISM_PLAN P7): the caller may
    /// feed every frame, the sampler accepts a point only when at least
    /// SampleIntervalSeconds of game time passed, so the 10 s window keeps
    /// 5–6 samples at any time compression. Pause (non-positive dt) is
    /// ignored, a skip/teleport gap or an absurd implied speed restarts the
    /// track. Below the transfer speed threshold the boat counts as anchored:
    /// speed 0, course keeps its last value. Pure C#, no Unity dependency.
    /// </summary>
    public sealed class CourseSampler
    {
        public const float SampleIntervalSeconds = 2f;

        /// <summary>Game-time gap above which the track is restarted (skip,
        /// teleport, load).</summary>
        public const double MaxSampleGapSeconds = 12.0;

        /// <summary>Implied speed above which the step is a teleport, not a
        /// run (a U-boat cannot do 50 kn).</summary>
        public const double MaxImpliedSpeedKn = 50.0;

        private const float SampleWindowSeconds = 10f;
        private const float MinTransferSpeedKn = 0.5f;

        private struct TrackSample
        {
            public DateTime Utc;
            public double Lat;
            public double Lon;
        }

        private readonly List<TrackSample> samples = new List<TrackSample>(16);
        private float courseDeg;
        private float speedKn;
        private bool hasLastSample;
        private DateTime lastSampleUtc;
        private double lastLat;
        private double lastLon;

        public float CourseDeg
        {
            get { return courseDeg; }
        }

        public float SpeedKn
        {
            get { return speedKn; }
        }

        /// <summary>Game-time seconds since the last accepted sample
        /// (MaxValue when the track is empty).</summary>
        public double SecondsSinceLastSample(DateTime utc)
        {
            if (!hasLastSample)
                return double.MaxValue;
            return (utc - lastSampleUtc).TotalSeconds;
        }

        /// <summary>True when the track is close enough to the queried instant
        /// to trust the transfer (the card flags a stale speed instead of
        /// silently using it). The gap is signed: a sight older than the last
        /// sample is just as unusable as one far in the future.</summary>
        public bool IsFresh(DateTime utc)
        {
            if (!hasLastSample)
                return false;
            double seconds = Math.Abs((utc - lastSampleUtc).TotalSeconds);
            return seconds <= MaxSampleGapSeconds;
        }

        public void AddSample(DateTime utc, double latDeg, double lonDeg)
        {
            if (!hasLastSample)
            {
                Seed(utc, latDeg, lonDeg);
                return;
            }

            double dtSeconds = (utc - lastSampleUtc).TotalSeconds;
            if (dtSeconds <= 0.0)
                return; // pause / backwards clock: the track stands still
            if (dtSeconds > MaxSampleGapSeconds)
            {
                Seed(utc, latDeg, lonDeg); // skip/load: restart the track
                return;
            }
            if (dtSeconds < SampleIntervalSeconds)
                return; // game-time cadence: too soon

            double stepKm = GeoMath.HaversineKm(
                (float)lastLat, (float)lastLon, (float)latDeg, (float)lonDeg);
            if (stepKm / 1.852 / (dtSeconds / 3600.0) > MaxImpliedSpeedKn)
            {
                Seed(utc, latDeg, lonDeg); // teleport inside the gap
                return;
            }

            lastSampleUtc = utc;
            lastLat = latDeg;
            lastLon = lonDeg;
            samples.Add(new TrackSample { Utc = utc, Lat = latDeg, Lon = lonDeg });
            while (samples.Count > 0 &&
                    (utc - samples[0].Utc).TotalSeconds > SampleWindowSeconds + 2.0)
                samples.RemoveAt(0);
            if (samples.Count < 2)
                return;

            TrackSample first = samples[0];
            TrackSample last = samples[samples.Count - 1];
            double spanHours = (last.Utc - first.Utc).TotalHours;
            if (spanHours < SampleWindowSeconds / 3600.0 * 0.6)
                return; // not enough baseline yet right after opening

            double distKm = GeoMath.HaversineKm(
                (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
            double kn = distKm / 1.852 / spanHours;
            if (kn >= MinTransferSpeedKn)
            {
                courseDeg = GeoMath.InitialBearingDeg(
                    (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
                speedKn = (float)kn;
            }
            else
            {
                speedKn = 0f;
            }
        }

        private void Seed(DateTime utc, double latDeg, double lonDeg)
        {
            samples.Clear();
            samples.Add(new TrackSample { Utc = utc, Lat = latDeg, Lon = lonDeg });
            hasLastSample = true;
            lastSampleUtc = utc;
            lastLat = latDeg;
            lastLon = lonDeg;
        }

        public void Reset()
        {
            samples.Clear();
            hasLastSample = false;
            lastSampleUtc = DateTime.MinValue;
            lastLat = 0.0;
            lastLon = 0.0;
            courseDeg = 0f;
            speedKn = 0f;
        }
    }
}
