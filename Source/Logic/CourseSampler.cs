using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Course/speed over ground from the recent position track (10 s window,
    /// 2 s sampling cadence owned by the caller). Below the transfer speed
    /// threshold the boat counts as anchored: speed 0, course keeps its last
    /// value. Extracted from SextantWorksheet (SRP — track math is not card
    /// logic) so it runs offline on synthetic tracks in CourseSamplerTests.
    /// Pure C#, no Unity dependency.
    /// </summary>
    public sealed class CourseSampler
    {
        public const float SampleIntervalSeconds = 2f;

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

        public float CourseDeg
        {
            get { return courseDeg; }
        }

        public float SpeedKn
        {
            get { return speedKn; }
        }

        public void AddSample(DateTime utc, double latDeg, double lonDeg)
        {
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

        public void Reset()
        {
            samples.Clear();
            courseDeg = 0f;
            speedKn = 0f;
        }
    }
}
