using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure JSON builder behind RadioNavProbe.Snapshot (no Unity
    /// dependencies, unit-testable): panel tuning, decoded text and
    /// per-station diagnostics with identity.
    /// </summary>
    internal static class RadioNavProbeJson
    {
        internal static string Build(
            bool visible, float angleDeg, float frequencyKHz, int zoneIndex,
            bool hasPosition, float latitude, float longitude,
            int dominantIndex, StationDiagnostics[] stations, string decoded,
            bool radioOn, int callsignChars, int callsignElementCount, bool keyerActive,
            bool keyerHasSequence, int keyerElementIndex, int callsignFlaggedElements,
            int callsignSeenTotal, int callsignSeenProcessed,
            int clearCount, string firstClearStack,
            int decodeAttempts, int decodeSuccess, int lastBufferLen, int lastExpectedLen)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\"loaded\":true");
            sb.Append(",\"visible\":");
            sb.Append(visible ? "true" : "false");
            sb.Append(",\"radioOn\":");
            sb.Append(radioOn ? "true" : "false");
            sb.Append(",\"csChars\":");
            sb.Append(callsignChars.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"csElemCount\":");
            sb.Append(callsignElementCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"keyerActive\":");
            sb.Append(keyerActive ? "true" : "false");
            sb.Append(",\"kSeq\":");
            sb.Append(keyerHasSequence ? "true" : "false");
            sb.Append(",\"kIdx\":");
            sb.Append(keyerElementIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"csEmitted\":");
            sb.Append(callsignFlaggedElements.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"csSeen\":");
            sb.Append(callsignSeenTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"csProc\":");
            sb.Append(callsignSeenProcessed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"clears\":");
            sb.Append(clearCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"decAtt\":");
            sb.Append(decodeAttempts.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"decOk\":");
            sb.Append(decodeSuccess.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"bufLen\":");
            sb.Append(lastBufferLen.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"expLen\":");
            sb.Append(lastExpectedLen.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"clearStack\":\"");
            sb.Append(Escape(firstClearStack ?? ""));
            sb.Append('"');
            sb.Append(",\"angleDeg\":");
            sb.Append(Float(angleDeg));
            sb.Append(",\"frequencyKHz\":");
            sb.Append(Float(frequencyKHz));
            sb.Append(",\"zone\":");
            sb.Append(zoneIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hasPosition\":");
            sb.Append(hasPosition ? "true" : "false");
            sb.Append(",\"lat\":");
            sb.Append(Float(latitude));
            sb.Append(",\"lon\":");
            sb.Append(Float(longitude));
            sb.Append(",\"dominant\":");
            sb.Append(dominantIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"decoded\":");
            if (decoded == null)
                sb.Append("null");
            else
            {
                sb.Append('"');
                sb.Append(Escape(decoded));
                sb.Append('"');
            }
            int count = stations != null ? stations.Length : 0;
            sb.Append(",\"stationCount\":");
            sb.Append(count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"stations\":[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                StationDiagnostics s = stations[i];
                sb.Append("{\"name\":\"");
                sb.Append(Escape(s.Name ?? "?"));
                sb.Append("\",\"callsign\":\"");
                sb.Append(Escape(s.Callsign ?? "?"));
                sb.Append('"');
                sb.Append(",\"gain\":");
                sb.Append(Float(s.Gain));
                sb.Append(",\"bearing\":");
                sb.Append(Float(s.CurrentBearing));
                sb.Append(",\"bearingToBoat\":");
                sb.Append(Float(s.BearingToBoat));
                sb.Append(",\"dots\":");
                sb.Append(s.DotCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"phase\":\"");
                sb.Append(s.SchedulerPhase.ToString());
                sb.Append('"');
                sb.Append(",\"dashes\":");
                sb.Append(s.DashCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"elapsed\":");
                sb.Append(Float(s.NavigationElapsed));
                sb.Append(",\"dominant\":");
                sb.Append(i == dominantIndex ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        internal static string Float(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string Escape(string value)
        {
            if (value == null)
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "").Replace("\n", " | ");
        }
    }
}
