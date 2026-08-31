using System;

namespace UBOAT.Mods.RadioNavigation
{
    public enum BeaconStationRole : byte
    {
        DirectionalSonne,
        RadioBeaconOnly
    }

    public struct BeaconOperatingSchedule
    {
        public int DailyStartMinutesUtc;
        public int DailyEndMinutesUtc;
        public int DailyMaintenanceStartMinutesUtc;
        public int DailyMaintenanceEndMinutesUtc;
        public int WednesdayMaintenanceStartMinutesUtc;
        public int WednesdayMaintenanceEndMinutesUtc;

    }

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
        public BeaconStationRole Role;
        public float BeamHeadingDegTrue;
        public float NormalBearingForwardDegTrue;
        public float NormalBearingBackwardDegTrue;
        public float BeamHalfWidthDeg;
        public int CycleDurationSeconds;
        public float BaseAngle;
        public BeaconOperatingSchedule Schedule;
        public BeaconHistoryEntry[] History;
    }

    public static class BeaconData
    {
        // Historical CAP 59 Appendix I set. Later/post-war stations are not
        // part of the real Sonne profile and are intentionally excluded.
        public static readonly BeaconStation[] Stations =
        {
            new BeaconStation
            {
                Name = "Bushmills",
                Country = "UK",
                Latitude = 55.205556,
                Longitude = -6.467222,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                Role = BeaconStationRole.DirectionalSonne,
                BeamHeadingDegTrue = 310.2f,
                NormalBearingForwardDegTrue = 130.2f,
                NormalBearingBackwardDegTrue = 310.2f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 40,
                BaseAngle = 310.2f,
                Schedule = new BeaconOperatingSchedule
                {
                    DailyStartMinutesUtc = 0,
                    DailyEndMinutesUtc = 1440,
                    DailyMaintenanceStartMinutesUtc = 960,
                    DailyMaintenanceEndMinutesUtc = 975,
                    WednesdayMaintenanceStartMinutesUtc = 660,
                    WednesdayMaintenanceEndMinutesUtc = 720
                },
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
                Name = "Quimper/Ploneis",
                Country = "France",
                Latitude = 48.018333,
                Longitude = -4.215000,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                Role = BeaconStationRole.DirectionalSonne,
                BeamHeadingDegTrue = 286.2f,
                NormalBearingForwardDegTrue = 106.2f,
                NormalBearingBackwardDegTrue = 286.2f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 40,
                BaseAngle = 286.2f,
                Schedule = new BeaconOperatingSchedule
                {
                    DailyStartMinutesUtc = 0,
                    DailyEndMinutesUtc = 1440,
                    DailyMaintenanceStartMinutesUtc = 450,
                    DailyMaintenanceEndMinutesUtc = 465,
                    WednesdayMaintenanceStartMinutesUtc = -1,
                    WednesdayMaintenanceEndMinutesUtc = -1
                },
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
                Name = "Stavanger",
                Country = "Norway",
                Latitude = 58.625278,
                Longitude = 5.630278,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                Role = BeaconStationRole.DirectionalSonne,
                BeamHeadingDegTrue = 247f,
                NormalBearingForwardDegTrue = 67f,
                NormalBearingBackwardDegTrue = 247f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 60,
                BaseAngle = 247f,
                Schedule = new BeaconOperatingSchedule
                {
                    DailyStartMinutesUtc = 0,
                    DailyEndMinutesUtc = 1440,
                    DailyMaintenanceStartMinutesUtc = -1,
                    DailyMaintenanceEndMinutesUtc = -1,
                    WednesdayMaintenanceStartMinutesUtc = -1,
                    WednesdayMaintenanceEndMinutesUtc = -1
                },
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
                Name = "Lugo",
                Country = "Spain",
                Latitude = 43.248056,
                Longitude = -7.482222,
                Commissioned = new DateTime(1942, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                Role = BeaconStationRole.DirectionalSonne,
                BeamHeadingDegTrue = 270f,
                NormalBearingForwardDegTrue = 90f,
                NormalBearingBackwardDegTrue = 270f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 60,
                BaseAngle = 270f,
                Schedule = new BeaconOperatingSchedule
                {
                    DailyStartMinutesUtc = 0,
                    DailyEndMinutesUtc = 1440,
                    DailyMaintenanceStartMinutesUtc = -1,
                    DailyMaintenanceEndMinutesUtc = -1,
                    WednesdayMaintenanceStartMinutesUtc = -1,
                    WednesdayMaintenanceEndMinutesUtc = -1
                },
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
                Name = "Seville",
                Country = "Spain",
                Latitude = 37.521667,
                Longitude = -6.030000,
                Commissioned = new DateTime(1940, 1, 1),
                Decommissioned = null,
                PowerW = 1500,
                Role = BeaconStationRole.RadioBeaconOnly,
                BeamHeadingDegTrue = 261f,
                NormalBearingForwardDegTrue = 81f,
                NormalBearingBackwardDegTrue = 261f,
                BeamHalfWidthDeg = 90f,
                CycleDurationSeconds = 60,
                BaseAngle = 261f,
                Schedule = new BeaconOperatingSchedule
                {
                    DailyStartMinutesUtc = 360,
                    DailyEndMinutesUtc = 1200,
                    DailyMaintenanceStartMinutesUtc = -1,
                    DailyMaintenanceEndMinutesUtc = -1,
                    WednesdayMaintenanceStartMinutesUtc = -1,
                    WednesdayMaintenanceEndMinutesUtc = -1
                },
                History = new[]
                {
                    new BeaconHistoryEntry
                    {
                        From = new DateTime(1940, 1, 1),
                        Callsign = "SL",
                        FrequencyKHz = 315.0f
                    }
                }
            }
        };
    }
}
