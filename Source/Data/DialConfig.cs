using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public class BandConfig
    {
        public string Id;
        public string Color;
        public float RangeMin;
        public float RangeMax;
        public string Unit;
        public float MinorTickValue;
        public string MinorTickUnit;
        public float AngleStart;
        public float AngleEnd;
        public SortedList<float, float> ReferencePoints;
    }

    public static class DialConfig
    {
        public static readonly BandConfig[] Bands;

        static DialConfig()
        {
            Bands = new BandConfig[5];

            Bands[0] = new BandConfig
            {
                Id = "I", Color = "black", RangeMin = 70, RangeMax = 150, Unit = "MHz",
                MinorTickValue = 1, MinorTickUnit = "degree",
                AngleStart = 15f, AngleEnd = 168.4f,
                ReferencePoints = new SortedList<float, float>
                {
                    { 70, 15f }, { 80, 46.4f }, { 90, 69f }, { 100, 87.7f },
                    { 110, 105.7f }, { 120, 122.6f }, { 130, 138.8f }, { 140, 154.1f }, { 150, 168.4f }
                }
            };

            Bands[1] = new BandConfig
            {
                Id = "II", Color = "blue", RangeMin = 150, RangeMax = 340, Unit = "kHz",
                MinorTickValue = 1, MinorTickUnit = "kHz",
                AngleStart = 10.7f, AngleEnd = 169.4f,
                ReferencePoints = new SortedList<float, float>
                {
                    { 150, 10.7f }, { 160, 26.9f }, { 170, 40.6f }, { 180, 52.2f },
                    { 190, 62.5f }, { 200, 71.1f }, { 210, 79.3f }, { 220, 87.2f },
                    { 230, 95.5f }, { 240, 103.2f }, { 250, 110.9f }, { 260, 118.2f },
                    { 270, 125.7f }, { 280, 132.5f }, { 290, 139.2f }, { 300, 145.6f },
                    { 310, 151.7f }, { 320, 157.9f }, { 330, 163.7f }, { 340, 169.4f }
                }
            };

            Bands[2] = new BandConfig
            {
                Id = "III", Color = "yellow", RangeMin = 340, RangeMax = 760, Unit = "kHz",
                MinorTickValue = 5, MinorTickUnit = "kHz",
                AngleStart = 16.8f, AngleEnd = 167.9f,
                ReferencePoints = new SortedList<float, float>
                {
                    { 340, 16.8f }, { 360, 30.9f }, { 380, 43.2f }, { 400, 53.3f },
                    { 420, 62.4f }, { 440, 70.5f }, { 460, 77.9f }, { 480, 85.1f },
                    { 500, 92.4f }, { 520, 99.4f }, { 540, 106.2f }, { 560, 113f },
                    { 580, 119.4f }, { 600, 125.5f }, { 620, 131.5f }, { 640, 137.4f },
                    { 660, 142.9f }, { 680, 148.4f }, { 700, 153.7f }, { 720, 158.5f },
                    { 740, 163.3f }, { 760, 167.9f }
                }
            };

            Bands[3] = new BandConfig
            {
                Id = "IV", Color = "green", RangeMin = 720, RangeMax = 1600, Unit = "kHz",
                MinorTickValue = 5, MinorTickUnit = "kHz",
                AngleStart = 12f, AngleEnd = 162.8f,
                ReferencePoints = new SortedList<float, float>
                {
                    { 720, 12f }, { 740, 18.8f }, { 760, 24.8f }, { 780, 30.9f },
                    { 800, 36.8f }, { 820, 42f }, { 840, 46.8f }, { 860, 51.8f },
                    { 880, 56.1f }, { 900, 60.1f }, { 920, 64.2f }, { 940, 67.7f },
                    { 960, 71.3f }, { 980, 74.6f }, { 1000, 78f }, { 1100, 94.6f },
                    { 1200, 110.3f }, { 1300, 125.2f }, { 1400, 138.7f }, { 1500, 151.3f },
                    { 1600, 162.8f }
                }
            };

            Bands[4] = new BandConfig
            {
                Id = "V", Color = "red", RangeMin = 1600, RangeMax = 3600, Unit = "kHz",
                MinorTickValue = 10, MinorTickUnit = "kHz",
                AngleStart = 14.7f, AngleEnd = 169.3f,
                ReferencePoints = new SortedList<float, float>
                {
                    { 1600, 14.7f }, { 1700, 29.4f }, { 1800, 41.9f }, { 1900, 52.8f },
                    { 2000, 62.4f }, { 2100, 70.8f }, { 2200, 78.8f }, { 2300, 86.5f },
                    { 2400, 94.1f }, { 2500, 101.5f }, { 2600, 108.9f }, { 2700, 116f },
                    { 2800, 122.6f }, { 2900, 129.2f }, { 3000, 135.8f }, { 3100, 141.7f },
                    { 3200, 147.6f }, { 3300, 153.2f }, { 3400, 158.7f }, { 3500, 164f },
                    { 3600, 169.3f }
                }
            };
        }

        public static float AngleToFrequency(float angle, int bandIndex)
        {
            if (bandIndex.IsNegative() || bandIndex >= Bands.Length)
                return float.NaN;

            BandConfig band = Bands[bandIndex];
            SortedList<float, float> points = band.ReferencePoints;

            if (angle <= band.AngleStart)
                return band.RangeMin;
            if (angle >= band.AngleEnd)
                return band.RangeMax;

            float prevFreq = 0, prevAngle = 0;
            bool found = false;

            foreach (var kvp in points)
            {
                float freq = kvp.Key;
                float ang = kvp.Value;

                if (ang >= angle)
                {
                    if (!found)
                        return freq;

                    float t = (angle - prevAngle) / (ang - prevAngle);
                    return prevFreq + t * (freq - prevFreq);
                }

                prevFreq = freq;
                prevAngle = ang;
                found = true;
            }

            return band.RangeMax;
        }

        public static string FormatFrequency(float frequency, int bandIndex)
        {
            if (float.IsNaN(frequency))
                return "---";

            if (bandIndex.IsNegative() || bandIndex >= Bands.Length)
                return frequency.ToString("F0");

            BandConfig band = Bands[bandIndex];

            if (band.Unit == "MHz")
                return frequency.ToString("F1") + " MHz";

            return Mathf.RoundToInt(frequency) + " kHz";
        }
    }
}
