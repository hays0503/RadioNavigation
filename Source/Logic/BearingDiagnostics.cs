using System;
using System.Collections.Generic;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The [RadioNav][BEARING] diagnostic report behind
    /// SonneController.LogBearingDiagnostics. Extracted (SRP — report
    /// formatting is not controller logic) so the reciprocal-delta math
    /// and line format are pinned offline in BearingDiagnosticsTests.
    /// All numbers use InvariantCulture: the previous inline format relied
    /// on the ambient locale (comma decimals on RU systems, in tests and
    /// in Player.log). Pure C#, no Unity dependency.
    /// </summary>
    public static class BearingDiagnostics
    {
        public static List<string> BuildLines(float playerLat, float playerLon,
            BeaconStation[] stations, StationState[] states)
        {
            var lines = new List<string>(stations.Length + 1);
            lines.Add("[RadioNav][BEARING] player lat=" + playerLat.ToString("F6", CultureInfo.InvariantCulture)
                + " lon=" + playerLon.ToString("F6", CultureInfo.InvariantCulture));

            for (int i = 0; i < stations.Length; i++)
            {
                BeaconStation station = stations[i];
                float mapBoatToStation = states[i].CurrentBearing;
                float mapStationToBoat = states[i].BearingToBoat;
                float trueBoatToStation = GeoMath.InitialBearingDeg(
                    playerLat, playerLon,
                    (float)station.Latitude, (float)station.Longitude);
                float trueStationToBoat = GeoMath.InitialBearingDeg(
                    (float)station.Latitude, (float)station.Longitude,
                    playerLat, playerLon);
                float trueReciprocalDelta = ReciprocalDelta(trueStationToBoat, trueBoatToStation);
                float mapReciprocalDelta = ReciprocalDelta(mapStationToBoat, mapBoatToStation);

                lines.Add("[RadioNav][BEARING] index=" + i
                    + " name=" + station.Name
                    + " callsign=" + (station.History.IsNotNull() && station.History.Length.IsPositive()
                        ? station.History[0].Callsign : "?")
                    + " station=" + station.Latitude.ToString("F6", CultureInfo.InvariantCulture) + "," + station.Longitude.ToString("F6", CultureInfo.InvariantCulture)
                    + " mapBoatToStation=" + mapBoatToStation.ToString("F1", CultureInfo.InvariantCulture)
                    + " trueBoatToStation=" + trueBoatToStation.ToString("F1", CultureInfo.InvariantCulture)
                    + " trueStationToBoat=" + trueStationToBoat.ToString("F1", CultureInfo.InvariantCulture)
                    + " mapStationToBoat=" + mapStationToBoat.ToString("F1", CultureInfo.InvariantCulture)
                    + " distanceKm=" + GeoMath.HaversineKm(
                        (float)station.Latitude,
                        (float)station.Longitude,
                        playerLat,
                        playerLon).ToString("F1", CultureInfo.InvariantCulture)
                    + " trueReciprocalDelta=" + trueReciprocalDelta.ToString("F1", CultureInfo.InvariantCulture)
                    + " mapReciprocalDelta=" + mapReciprocalDelta.ToString("F1", CultureInfo.InvariantCulture)
                    + " baseAngle=" + station.BaseAngle.ToString("F1", CultureInfo.InvariantCulture)
                    + " beamHeading=" + station.BeamHeadingDegTrue.ToString("F1", CultureInfo.InvariantCulture));
            }

            return lines;
        }

        private static float ReciprocalDelta(float fromStation, float toStation)
        {
            float reciprocal = (fromStation + 180f) % 360f;
            float delta = Math.Abs(reciprocal - toStation);
            if (delta > 180f)
                delta = 360f - delta;
            return delta;
        }
    }
}
