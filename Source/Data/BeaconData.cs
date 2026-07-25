using System;

namespace UBOAT.Mods.RadioNavigation
{
    public struct BeaconHistoryEntry
    {
        public DateTime? From;
        public DateTime? To;
        public string Callsign;
        public float? FrequencyKHz;
    }

    public struct BeaconStation
    {
        public string Name;
        public string Country;
        public double Latitude;
        public double Longitude;
        public DateTime Commissioned;
        public DateTime? Decommissioned;
        public int PowerW;
        public float BeamHeadingDegTrue;
        public float BeamHalfWidthDeg;
        public int CycleDurationSeconds;
        public float BaseAngle;
        public BeaconHistoryEntry[] History;

        public int[] GetDotCountToBearingTable()
        {
            var config = ConsolCycleConfig.GetForCycleDuration(CycleDurationSeconds);
            int maxCount = config.MaxNavigationCount;
            var table = new int[maxCount + 1];

            for (int i = 0; i <= maxCount; i++)
            {
                float ratio = maxCount > 0 ? (float)i / maxCount : 0f;
                float bearing = BeamHeadingDegTrue - BeamHalfWidthDeg + ratio * 2f * BeamHalfWidthDeg;
                table[i] = (int)((bearing + 360f) % 360f);
            }

            return table;
        }
    }

    public static class BeaconData
    {
        public static readonly BeaconStation[] Stations =
        {
            new BeaconStation
            {
                Name = "Bushmills",
                Country = "UK",
                Latitude = 55.2028,
                Longitude = -6.5,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 310.2f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 310.2f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1940, 1, 1),
                        Callsign = "MWN",
                        FrequencyKHz = 266.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Ploneis (Quimper)",
                Country = "France",
                Latitude = 48.018333,
                Longitude = -4.215278,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 2000,
                BeamHeadingDegTrue = 286.2f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 110.0f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1940, 1, 1),
                        Callsign = "FRQ",
                        FrequencyKHz = 257.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Stavanger-Varhaug",
                Country = "Norway",
                Latitude = 58.625556,
                Longitude = 5.630278,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 247f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 230.0f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1940, 1, 1),
                        Callsign = "LEC",
                        FrequencyKHz = 319.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Lugo (Outeiro de Rei)",
                Country = "Spain",
                Latitude = 43.248056,
                Longitude = -7.482222,
                Commissioned = new DateTime(1942, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 270f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 310.0f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1942, 1, 1),
                        Callsign = "LG",
                        FrequencyKHz = 285.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Sevilla",
                Country = "Spain",
                Latitude = 37.521389,
                Longitude = -6.021667,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 261f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 80.0f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1940, 1, 1),
                        Callsign = "SL",
                        FrequencyKHz = 315.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Andoya",
                Country = "Norway",
                Latitude = 69.148056,
                Longitude = 15.883333,
                Commissioned = new DateTime(1941, 1, 1),
                Decommissioned = null,
                PowerW = 3000,
                BeamHeadingDegTrue = 315f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 315f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1941, 1, 1),
                        Callsign = "LEX",
                        FrequencyKHz = 332.5f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Bjornoya",
                Country = "Norway",
                Latitude = 74.492778,
                Longitude = 19.059722,
                Commissioned = new DateTime(1941, 1, 1),
                Decommissioned = null,
                PowerW = 3000,
                BeamHeadingDegTrue = 180f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 180f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1941, 1, 1),
                        Callsign = "LJS",
                        FrequencyKHz = 332.5f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Jan Mayen",
                Country = "Norway",
                Latitude = 71.05,
                Longitude = -8.233333,
                Commissioned = new DateTime(1941, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 112.5f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 112.5f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1941, 1, 1),
                        Callsign = "LMC",
                        FrequencyKHz = 332.5f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Beauvais",
                Country = "France",
                Latitude = 49.682222,
                Longitude = 2.745556,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 300f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 300f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = 307.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Bayeux",
                Country = "France",
                Latitude = 49.283333,
                Longitude = -0.7,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 315f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 315f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = 306.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Petten",
                Country = "Netherlands",
                Latitude = 52.766667,
                Longitude = 4.666667,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 315f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 315f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = 481.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Plougourvest",
                Country = "France",
                Latitude = 48.55,
                Longitude = -4.083333,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 300f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 300f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = null
                    }
                }
            },
            new BeaconStation
            {
                Name = "Brieg",
                Country = "Germany",
                Latitude = 50.85,
                Longitude = 17.483333,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 330f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 330f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = 316.0f
                    }
                }
            },
            new BeaconStation
            {
                Name = "Husum",
                Country = "Germany",
                Latitude = 54.483333,
                Longitude = 9.05,
                Commissioned = new DateTime(1944, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                BeamHeadingDegTrue = 300f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 120,
                BaseAngle = 300f,
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1944, 1, 1),
                        Callsign = null,
                        FrequencyKHz = 481.0f
                    }
                }
            }
        };
    }
}
